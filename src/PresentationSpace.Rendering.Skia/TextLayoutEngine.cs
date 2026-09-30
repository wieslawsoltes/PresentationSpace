using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using HarfBuzzSharp;
using PresentationSpace.Core;
using SkiaSharp;
using SkiaSharp.HarfBuzz;
using HBBuffer = HarfBuzzSharp.Buffer;

namespace PresentationSpace.Rendering.Skia;

public sealed record TextLineMetrics(int Start, int Length, float Width, float Height, float Baseline,
    float Indent, bool ParagraphStart, bool RightToLeft)
{
    public float Top { get; init; }
    public float Left { get; init; }
    public bool Justified { get; init; }
    public bool ParagraphEnd { get; init; }
}
/// <summary>Observed tab-field placement in a visual line, in coordinates relative to the content box.</summary>
public readonly record struct TextTabMetrics(int SourceIndex, int LineIndex, float Stop, float FieldStart, float FieldEnd,
    TextTabAlignment Alignment, bool Custom, bool Clamped);
public sealed record TextLayoutMetrics(float Width, float Height, int GlyphCount,
    ImmutableArray<TextLineMetrics> Lines, bool HasMixedDirection)
{
    public ImmutableArray<TextTabMetrics> Tabs { get; init; } = [];
    public bool HasUnsupportedTabDirection { get; init; }
}
public readonly record struct TextLayoutCacheStatistics(long Hits, long Misses, int Entries, long ApproximateBytes);

/// <summary>
/// Single-thread-affine, disposable HarfBuzz/Skia text layout, shared by measurement and drawing.
/// Cached native blobs never escape; returned metrics contain only immutable managed values.
/// Supports horizontal single-direction paragraphs, not complete Unicode bidirectional layout.
/// </summary>
public sealed class TextLayoutEngine : IDisposable
{
    private readonly record struct Key(string Text, TextStyle Style, ImmutableArray<TextRangeStyle> Ranges, float Width, bool Wrap);
    private readonly record struct FontKey(string Family, float Size, bool Bold, bool Italic);
    private sealed class NativeFont : IDisposable
    {
        private readonly SKTypeface _face;
        private readonly bool _ownsFace;
        public SKFont Font { get; }
        public SKShaper Shaper { get; }
        public SKFontMetrics Metrics { get; }
        public float TabStop { get; }
        public NativeFont(SKTypeface face, bool ownsFace, FontKey key)
        {
            _face = face; _ownsFace = ownsFace;
            Font = new(face, key.Size) { Edging = SKFontEdging.Antialias, Subpixel = true };
            try { Metrics = Font.Metrics; TabStop = Math.Max(1, Font.MeasureText(" ") * 4); Shaper = new(face); }
            catch { Font.Dispose(); if (ownsFace) face.Dispose(); throw; }
        }
        public void Dispose() { Shaper.Dispose(); Font.Dispose(); if (_ownsFace) _face.Dispose(); }
    }
    private sealed record FontSection(int Start, int End, NativeFont Font);
    private sealed record ShapedRun(SKShaper.Result Glyphs, float[] Advances, bool Rtl);
    private sealed record GlyphSpan(NativeFont Font, int Start, SKShaper.Result Glyphs, float[] Advances, TextStyle[] Styles, bool Rtl);
    private sealed record Piece(int Start, int Length, List<GlyphSpan> Spans, float Width, TextStyle Style, bool Space = false, TextTabPlacement? Tab = null);
    private sealed record DrawRun(SKTextBlob Blob, SKColor Color, float X, float Y, float UnderlineStart, float UnderlineWidth, float UnderlineY, float UnderlineThickness);
    private sealed class Entry(Key key, TextLayoutMetrics metrics, List<DrawRun> runs, long bytes) : IDisposable
    {
        public Key Key { get; } = key;
        public TextLayoutMetrics Metrics { get; } = metrics;
        public List<DrawRun> Runs { get; } = runs;
        public long Bytes { get; } = bytes;
        public void Dispose() { foreach (var run in Runs) run.Blob.Dispose(); }
    }
    private readonly Dictionary<Key, LinkedListNode<Entry>> _entries = [];
    private readonly LinkedList<Entry> _lru = new();
    private ITypefaceResolver? _observedResolver;
    private long _observedVersion = -1, _bytes, _hits, _misses;
    private bool _disposed;
    private SKPaint? _paint;
    public ITypefaceResolver? TypefaceResolver { get; set; }
    public int MaximumCachedLayouts { get; set; } = 256;
    /// <summary>Conservative retained-text/glyph estimate, not an exact native-memory limit.</summary>
    public long CacheBudget { get; set; } = 8 * 1024 * 1024;
    public TextLayoutCacheStatistics CacheStatistics => new(_hits, _misses, _entries.Count, _bytes);

    public TextLayoutMetrics Measure(string text, TextStyle style, float width, ImmutableArray<TextRangeStyle> ranges = default) =>
        Measure(text, style, width, ranges, true);

    public TextLayoutMetrics Measure(string text, TextStyle style, float width, ImmutableArray<TextRangeStyle> ranges, bool wrap)
    {
        var entry = Get(text, style, width, ranges, wrap, out bool temporary);
        try { return entry.Metrics; } finally { if (temporary) entry.Dispose(); }
    }

    /// <summary>Draw with uniform insets. Measurement and painting share advances and baselines.</summary>
    public void Draw(SKCanvas canvas, string text, TextStyle style, RectF bounds, float padding = 3, ImmutableArray<TextRangeStyle> ranges = default) =>
        Draw(canvas, text, style, bounds, TextBoxSpec.Uniform(padding), ranges);

    /// <summary>Draw with explicit text-body insets and wrapping. The inset content rectangle is clipped without replacing the host clip.</summary>
    public void Draw(SKCanvas canvas, string text, TextStyle style, RectF bounds, TextBoxSpec box, ImmutableArray<TextRangeStyle> ranges = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(canvas); TextBoxModel.Validate(box);
        if (!float.IsFinite(bounds.X) || !float.IsFinite(bounds.Y) || !float.IsFinite(bounds.Right) || !float.IsFinite(bounds.Bottom) ||
            bounds.Width <= 0 || bounds.Height <= 0) throw new ArgumentOutOfRangeException(nameof(bounds));
        var content = TextBoxModel.Inset(bounds, box);
        if (content.Width <= 0 || content.Height <= 0) return;
        var entry = Get(text, style, content.Width, ranges, box.Wrap, out bool temporary);
        try
        {
            float free = Math.Max(0, content.Height - entry.Metrics.Height);
            float top = content.Y + (style.VerticalAlignment switch { Core.VerticalAlignment.Middle => free / 2, Core.VerticalAlignment.Bottom => free, _ => 0 });
            canvas.Save();
            try
            {
                canvas.ClipRect(new(content.X, content.Y, content.Right, content.Bottom));
                // The engine is single-thread-affine; this private paint does not escape.
                // Reuse avoids a managed/native paint allocation on every warm draw.
                var paint = _paint ??= new SKPaint { IsAntialias = true };
                foreach (var run in entry.Runs)
                {
                    paint.Color = run.Color;
                    canvas.DrawText(run.Blob, content.X + run.X, top + run.Y, paint);
                    if (run.UnderlineWidth > 0)
                        canvas.DrawRect(content.X + run.X + run.UnderlineStart, top + run.Y + run.UnderlineY,
                            run.UnderlineWidth, run.UnderlineThickness, paint);
                }
            }
            finally { canvas.Restore(); }
        }
        finally { if (temporary) entry.Dispose(); }
    }

    public void Clear()
    {
        foreach (var entry in _lru) entry.Dispose();
        _entries.Clear(); _lru.Clear(); _bytes = 0;
    }
    public void Dispose() { if (_disposed) return; Clear(); _paint?.Dispose(); _paint = null; _disposed = true; }
    private void Trim()
    {
        while (_lru.First is { } first && (_entries.Count > Math.Max(0, MaximumCachedLayouts) || _bytes > Math.Max(0, CacheBudget)))
        { _entries.Remove(first.Value.Key); _bytes -= first.Value.Bytes; first.Value.Dispose(); _lru.RemoveFirst(); }
    }
    private Entry Get(string text, TextStyle style, float width, ImmutableArray<TextRangeStyle> ranges, bool wrap, out bool temporary)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(text); ArgumentNullException.ThrowIfNull(style);
        if (!float.IsFinite(width) || width <= 0 || width > 100000) throw new ArgumentOutOfRangeException(nameof(width));
        if (ranges.IsDefault) ranges = [];
        long version = (TypefaceResolver as IVersionedTypefaceResolver)?.Version ?? 0;
        if (!ReferenceEquals(TypefaceResolver, _observedResolver) || version != _observedVersion)
        { Clear(); _observedResolver = TypefaceResolver; _observedVersion = version; }
        Trim();
        var key = new Key(text, style, ranges, width, wrap);
        if (_entries.TryGetValue(key, out var cached))
        { _hits++; _lru.Remove(cached); _lru.AddLast(cached); temporary = false; return cached.Value; }
        Validate(text, style, ranges);
        _misses++;
        var result = Build(key);
        temporary = MaximumCachedLayouts <= 0 || CacheBudget <= 0 || result.Bytes > CacheBudget;
        if (!temporary) { _entries[key] = _lru.AddLast(result); _bytes += result.Bytes; Trim(); }
        return result;
    }
    private static void Validate(string text, TextStyle style, ImmutableArray<TextRangeStyle> ranges)
    {
        if (text.Length > TextFlow.MaximumTextLength) throw new InvalidDataException("Text exceeds one million UTF-16 code units.");
        TextFlow.ValidateStyle(style); int end = 0;
        foreach (var range in ranges)
        {
            if (range is null || range.Start < end || range.Length <= 0 || range.Start > text.Length - range.Length ||
                !RichText.IsBoundary(text, range.Start) || !RichText.IsBoundary(text, range.Start + range.Length)) throw new InvalidDataException("Invalid text range.");
            TextFlow.ValidateStyle(range.Style); end = range.Start + range.Length;
        }
    }

    private Entry Build(Key key)
    {
        var source = new SlideShape { Text = key.Text, TextStyle = key.Style, TextRanges = key.Ranges };
        var fonts = new Dictionary<FontKey, NativeFont>();
        var draw = new List<DrawRun>();
        var metrics = ImmutableArray.CreateBuilder<TextLineMetrics>();
        int glyphCount = 0;
        bool mixed = false, unsupportedTabDirection = false;
        ImmutableArray<TextTabMetrics>.Builder? tabMetrics = null;
        NativeFont Font(TextStyle style)
        {
            var fontKey = new FontKey(style.FontFamily, style.FontSize, style.Bold, style.Italic);
            if (fonts.TryGetValue(fontKey, out var font)) return font;
            if (fonts.Count >= 256) throw new InvalidDataException("A text layout supports at most 256 distinct font/size combinations.");
            var supplied = TypefaceResolver?.Resolve(style);
            var face = supplied ?? SKTypeface.FromFamilyName(style.FontFamily, new SKFontStyle(style.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
                SKFontStyleWidth.Normal, style.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright));
            return fonts[fontKey] = new(face, supplied is null, fontKey);
        }
        // Establish font-only sections once at extended-grapheme boundaries.
        // Repeated width probes must not rescan every character and resolve its
        // font again; paint-only changes still remain in the same shaping span.
        var sections = new List<FontSection>();
        void IndexFonts()
        {
            if (key.Ranges.IsEmpty)
            { sections.Add(new(0, key.Text.Length, Font(key.Style))); return; }
            int start = 0;
            NativeFont? font = null;
            TextStyle? previous = null;
            foreach (int offset in StringInfo.ParseCombiningCharacters(key.Text))
            {
                var style = RichText.StyleAt(source, offset);
                if (ReferenceEquals(style, previous)) continue;
                previous = style;
                var next = Font(style);
                if (ReferenceEquals(next, font)) continue;
                if (font is not null) sections.Add(new(start, offset, font));
                start = offset; font = next;
            }
            if (font is not null) sections.Add(new(start, key.Text.Length, font));
        }
        int SectionAt(int offset)
        {
            int low = 0, high = sections.Count;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (sections[middle].End <= offset) low = middle + 1; else high = middle;
            }
            return low;
        }
        // Per-build reuse is bounded separately from retained layout caching.
        // Glyph arrays contain no source-dependent paint styles, and are reused
        // only with the identical resolved font/size and exact text fragment.
        // This also avoids reshaping identical prefixes on each line of a long URL.
        var shapedRuns = new Dictionary<(NativeFont Font, string Text), ShapedRun>();
        int reusedCharacters = 0;
        ShapedRun ShapeRun(NativeFont font, string value)
        {
            var runKey = (font, value);
            if (shapedRuns.TryGetValue(runKey, out var cached)) return cached;
            using var buffer = new HBBuffer(); buffer.AddUtf16(value); buffer.GuessSegmentProperties();
            var shaped = font.Shaper.Shape(buffer, font.Font);
            var positions = buffer.GlyphPositions;
            var advances = new float[positions.Length];
            float scale = font.Font.Size / 512f;
            for (int i = 0; i < positions.Length; i++) advances[i] = positions[i].XAdvance * scale;
            var result = new ShapedRun(shaped, advances, buffer.Direction == Direction.RightToLeft);
            if (shapedRuns.Count < 256 && value.Length <= 4096 && reusedCharacters + value.Length <= 32768)
            { shapedRuns.Add(runKey, result); reusedCharacters += value.Length; }
            return result;
        }
        Piece Shape(int start, int length, bool whitespace = false)
        {
            var spans = new List<GlyphSpan>();
            float used = 0;
            int end = start + length;
            for (int i = SectionAt(start); i < sections.Count && sections[i].Start < end; i++)
            {
                var section = sections[i];
                int first = Math.Max(start, section.Start), last = Math.Min(end, section.End);
                var run = ShapeRun(section.Font, key.Text.Substring(first, last - first));
                // Uniform text needs no repeated per-glyph style references.
                var styles = key.Ranges.IsEmpty ? Array.Empty<TextStyle>() : new TextStyle[run.Glyphs.Codepoints.Length];
                for (int g = 0; g < styles.Length; g++)
                    styles[g] = RichText.StyleAt(source, first + (int)run.Glyphs.Clusters[g]);
                spans.Add(new(section.Font, first, run.Glyphs, run.Advances, styles, run.Rtl));
                used += run.Glyphs.Width;
            }
            return new(start, length, spans, used, RichText.StyleAt(source, start), whitespace);
        }
        Piece Bullet(TextStyle style)
        {
            var font = Font(style); var run = ShapeRun(font, "•");
            return new(0, 0, [new(font, 0, run.Glyphs, run.Advances,
                Enumerable.Repeat(style, run.Glyphs.Codepoints.Length).ToArray(), false)], run.Glyphs.Width, style);
        }
        void Emit(Piece piece, float x, float baseline)
        {
            var spans = piece.Spans;
            bool rtl = spans.FirstOrDefault()?.Rtl == true;
            for (int si = 0; si < spans.Count; si++)
            {
                var span = spans[rtl ? spans.Count - 1 - si : si];
                var result = span.Glyphs;
                float pen = 0;
                for (int start = 0; start < result.Codepoints.Length;)
                {
                    int end = start + 1;
                    var style = span.Styles.Length == 0 ? piece.Style : span.Styles[start];
                    while (end < result.Codepoints.Length && (span.Styles.Length == 0 ||
                        (span.Styles[end].Color == style.Color && span.Styles[end].Underline == style.Underline))) end++;
                    using var builder = new SKTextBlobBuilder();
                    var run = builder.AllocateRawPositionedRun(span.Font.Font, end - start);
                    float underlineStart = pen;
                    for (int g = start; g < end; g++)
                    { run.Glyphs[g - start] = (ushort)result.Codepoints[g]; run.Positions[g - start] = result.Points[g]; pen += span.Advances[g]; }
                    var blob = builder.Build();
                    if (blob is not null)
                    {
                        draw.Add(new(blob, SlideRenderer.Color(style.Color, SKColors.Black), x, baseline, underlineStart,
                            style.Underline ? Math.Max(0, pen - underlineStart) : 0, Math.Max(2, style.FontSize * .09f), Math.Max(1, style.FontSize * .045f)));
                        glyphCount += end - start;
                    }
                    start = end;
                }
                x += result.Width;
            }
        }
        // Only aligned custom fields need look-ahead. Reuse those same shaped pieces
        // when they are laid out, and release the field dictionary at the next delimiter.
        Dictionary<int, Piece>? preparedField = null;
        float DecimalPosition(Piece piece, int sourceOffset)
        {
            float x = 0;
            foreach (var span in piece.Spans)
            {
                var glyphs = span.Glyphs;
                for (int g = 0; g < glyphs.Clusters.Length; g++)
                    if (span.Start + glyphs.Clusters[g] >= sourceOffset) return x + glyphs.Points[g].X;
                x += glyphs.Width;
            }
            return piece.Width;
        }
        (float Width, float? Decimal) PrepareField(ImmutableArray<TextToken> tokens, int index)
        {
            preparedField = new();
            float width = 0, trailing = 0;
            float? decimalPosition = null;
            for (int i = index + 1; i < tokens.Length; i++)
            {
                var token = tokens[i];
                if (token.Kind is TextTokenKind.Tab or TextTokenKind.ParagraphBreak or TextTokenKind.LineBreak) break;
                if (token.Kind == TextTokenKind.Opportunity) continue;
                var piece = Shape(token.Start, token.Length, token.Kind == TextTokenKind.Space);
                preparedField.Add(token.Start, piece);
                int dot = key.Text.AsSpan(token.Start, token.Length).IndexOf('.');
                if (dot >= 0 && decimalPosition is null) decimalPosition = width + DecimalPosition(piece, token.Start + dot);
                width += piece.Width;
                trailing = piece.Space ? trailing + piece.Width : 0;
            }
            return (Math.Max(0, width - trailing), decimalPosition);
        }
        var pieces = new List<Piece>();
        var paragraphStyle = key.Style;
        Piece? bullet = null;
        bool firstLine = true, paragraphRtl = false, sawLtr = false, sawRtl = false;
        float usedWidth = 0, y = 0, maxWidth = 0, inkBottom = 0;
        int lineStart = 0;
        float TextIndent() => TextBoxModel.LeftMargin(paragraphStyle) +
            (firstLine && bullet is null ? TextBoxModel.FirstIndent(paragraphStyle) : 0);
        float Available() => Math.Max(1, key.Width - TextIndent() - paragraphStyle.ParagraphRightMargin);
        void StartParagraph(int offset)
        {
            paragraphStyle = RichText.StyleAt(source, offset);
            firstLine = true; sawLtr = sawRtl = paragraphRtl = false;
            bullet = paragraphStyle.Bullets ? Bullet(paragraphStyle) : null;
            y += paragraphStyle.SpaceBefore;
        }
        void Finish(int end, bool paragraphEnd = false, bool automatic = false)
        {
            // Exclude trailing breaking spaces from alignment, while keeping their source indexes.
            while (pieces.Count > 0 && pieces[^1].Space && pieces[^1].Tab is not { Custom: true }) { usedWidth -= pieces[^1].Width; pieces.RemoveAt(pieces.Count - 1); }
            usedWidth = Math.Max(0, usedWidth);
            float ascent = 0, descent = 0, leading = 0, size = 0;
            void Include(TextStyle style)
            {
                var m = Font(style).Metrics;
                ascent = Math.Max(ascent, -m.Ascent); descent = Math.Max(descent, m.Descent);
                leading = Math.Max(leading, m.Leading); size = Math.Max(size, style.FontSize);
            }
            if (pieces.Count == 0 || bullet is not null) Include(paragraphStyle);
            foreach (var piece in pieces) { if (piece.Spans.Count == 0) Include(piece.Style); else foreach (var span in piece.Spans) Include(span.Styles.FirstOrDefault() ?? piece.Style); }
            float ink = ascent + descent;
            float height = paragraphStyle.LineSpacingPoints ?? Math.Max(ink + Math.Max(0, leading), size * paragraphStyle.LineSpacing);
            // Exact spacing may intentionally overlap lines, but not clip the final descent.
            float baseline = y + ascent + Math.Max(0, height - ink) / 2;
            float indent = TextIndent(), available = Available();
            float free = Math.Max(0, available - usedWidth);
            bool customTabs = pieces.Any(p => p.Tab is { Custom: true });
            unsupportedTabDirection |= customTabs && (paragraphRtl || sawRtl);
            // Explicit stops are absolute content coordinates, not subject to a second
            // whole-line alignment translation. Tabbed lines are never justified.
            float align = customTabs ? 0 : paragraphStyle.Alignment switch { ParagraphAlignment.Center => free / 2, ParagraphAlignment.Right => free, _ => 0 };
            int spaces = 0;
            bool eligible = automatic && paragraphStyle.Alignment == ParagraphAlignment.Justify && !paragraphRtl;
            if (eligible)
            {
                bool seenWord = false;
                foreach (var piece in pieces)
                {
                    if (!piece.Space) seenWord = true;
                    else if (piece.Spans.Count == 0) { spaces = 0; break; } // Tab stops must not be stretched.
                    else if (seenWord) spaces += piece.Length;
                }
            }
            float extra = spaces == 0 ? 0 : free / spaces;
            float width = indent + usedWidth + paragraphStyle.ParagraphRightMargin + (extra > 0 ? free : 0);
            if (firstLine && bullet is not null)
            {
                float marker = paragraphRtl ? align + usedWidth + indent - bullet.Width : align + TextBoxModel.LeftMargin(paragraphStyle) + TextBoxModel.FirstIndent(paragraphStyle);
                Emit(bullet, marker, baseline);
            }
            float x = align + (paragraphRtl && bullet is not null ? 0 : indent), left = x;
            bool afterWord = false;
            TextTabMetrics? currentTab = null;
            for (int i = 0; i < pieces.Count; i++)
            {
                var piece = pieces[paragraphRtl ? pieces.Count - 1 - i : i];
                if (piece.Tab is { } tab)
                {
                    tabMetrics ??= ImmutableArray.CreateBuilder<TextTabMetrics>();
                    if (currentTab is { } prior) tabMetrics.Add(prior with { FieldEnd = x });
                    currentTab = new(piece.Start, metrics.Count, tab.Stop, x + piece.Width, x + piece.Width,
                        tab.Alignment, tab.Custom, tab.Clamped);
                }
                Emit(piece, x, baseline); x += piece.Width;
                if (!piece.Space) afterWord = true;
                else if (afterWord && extra > 0) x += extra * piece.Length;
            }
            if (currentTab is { } lastTab) tabMetrics!.Add(lastTab with { FieldEnd = x });
            metrics.Add(new(lineStart, Math.Max(0, end - lineStart), width, height, baseline, indent, firstLine, paragraphRtl)
                { Top = y, Left = left, Justified = extra > 0, ParagraphEnd = paragraphEnd });
            maxWidth = Math.Max(maxWidth, width); inkBottom = Math.Max(inkBottom, baseline + descent); y += height;
            if (paragraphEnd) y += paragraphStyle.SpaceAfter;
            pieces.Clear(); usedWidth = 0; firstLine = false; lineStart = end;
            mixed |= sawLtr && sawRtl;
        }
        (Piece Piece, int Count) FitPrefix(TextToken token, ImmutableArray<int> boundaries, int position, float available)
        {
            int remaining = boundaries.Length - 1 - position;
            int low = 1, high = 1;
            Piece best = Shape(token.Start + boundaries[position], boundaries[position + 1] - boundaries[position]);
            while (high < remaining && best.Width <= available)
            {
                low = high; high = Math.Min(remaining, high * 2);
                var probe = Shape(token.Start + boundaries[position], boundaries[position + high] - boundaries[position]);
                if (probe.Width > available) break;
                best = probe; low = high;
            }
            while (low + 1 < high)
            {
                int middle = low + (high - low) / 2;
                var probe = Shape(token.Start + boundaries[position], boundaries[position + middle] - boundaries[position]);
                if (probe.Width <= available) { low = middle; best = probe; } else high = middle;
            }
            return (best, low);
        }
        try
        {
            IndexFonts();
            StartParagraph(0);
            var tokens = TextFlow.Tokenize(key.Text);
            for (int tokenIndex = 0; tokenIndex < tokens.Length; tokenIndex++)
            {
                var token = tokens[tokenIndex];
                if (token.Kind is TextTokenKind.ParagraphBreak or TextTokenKind.LineBreak)
                {
                    preparedField = null;
                    Finish(token.Start, paragraphEnd: token.Kind == TextTokenKind.ParagraphBreak);
                    lineStart = token.Start + token.Length;
                    if (token.Kind == TextTokenKind.ParagraphBreak) StartParagraph(lineStart);
                    continue;
                }
                if (token.Kind == TextTokenKind.Opportunity) continue;
                if (token.Kind is TextTokenKind.Space or TextTokenKind.Tab)
                {
                    Piece space;
                    if (token.Kind == TextTokenKind.Tab)
                    {
                        var style = RichText.StyleAt(source, token.Start);
                        float stop = paragraphStyle.DefaultTabSize > 0 ? paragraphStyle.DefaultTabSize : Font(style).TabStop;
                        preparedField = null;
                        float caret = TextIndent() + usedWidth;
                        var placement = TextTabStops.Place(paragraphStyle.TabStops, caret, stop);
                        if (placement.Custom && placement.Alignment != TextTabAlignment.Left)
                        {
                            var field = PrepareField(tokens, tokenIndex);
                            placement = TextTabStops.Place(paragraphStyle.TabStops, caret, stop, field.Width, field.Decimal);
                        }
                        space = new(token.Start, token.Length, [], Math.Max(0, placement.Start - caret), style, true, placement);
                    }
                    else space = preparedField is not null && preparedField.TryGetValue(token.Start, out var readySpace) ? readySpace : Shape(token.Start, token.Length, true);
                    pieces.Add(space); usedWidth += space.Width; continue;
                }
                foreach (var rune in key.Text.AsSpan(token.Start, token.Length).EnumerateRunes())
                {
                    var category = Rune.GetUnicodeCategory(rune);
                    if (category is not (UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter or UnicodeCategory.OtherLetter or UnicodeCategory.ModifierLetter)) continue;
                    bool rtl = rune.Value is >= 0x590 and <= 0x8ff or >= 0xfb1d and <= 0xfdff or >= 0xfe70 and <= 0xfeff or >= 0x10800 and <= 0x10fff or >= 0x1e800 and <= 0x1eeff;
                    if (!sawLtr && !sawRtl) paragraphRtl = rtl;
                    sawRtl |= rtl; sawLtr |= !rtl;
                }
                bool canSplit = key.Wrap && TextFlow.AllowsEmergencyBreak(key.Text.AsSpan(token.Start, token.Length));
                // Do not shape an entire long token just to discard it and shape every prefix again.
                // All prefix probes still use HarfBuzz; no guessed character-width shortcut.
                Piece? word = preparedField is not null && preparedField.TryGetValue(token.Start, out var readyWord) ? readyWord :
                    token.Length <= 512 || !canSplit ? Shape(token.Start, token.Length) : null;
                float available = Available();
                if (!key.Wrap) { pieces.Add(word!); usedWidth += word!.Width; continue; }
                bool hasContent = pieces.Any(p => !p.Space);
                if (word is not null && usedWidth + word.Width <= available)
                { pieces.Add(word); usedWidth += word.Width; continue; }
                if (word is null && hasContent)
                {
                    var probeBounds = TextFlow.GraphemeBoundaries(key.Text.Substring(token.Start, token.Length));
                    var probe = FitPrefix(token, probeBounds, 0, Math.Max(1, available - usedWidth));
                    if (probe.Count == probeBounds.Length - 1 && usedWidth + probe.Piece.Width <= available)
                    { pieces.Add(probe.Piece); usedWidth += probe.Piece.Width; continue; }
                }
                // A deliberately out-of-box stop is not silently discarded. Preserve
                // the anchor and clip overflow; later words can wrap normally. For an
                // overwide aligned field clamped to the caret, normal wrapping applies.
                if (pieces.LastOrDefault()?.Tab is { Custom: true, Clamped: false } anchor &&
                    anchor.Start >= key.Width - paragraphStyle.ParagraphRightMargin)
                {
                    word ??= Shape(token.Start, token.Length);
                    pieces.Add(word); usedWidth += word.Width; continue;
                }
                if (hasContent) Finish(token.Start, automatic: true);
                else if (pieces.Count > 0)
                {
                    // Oversized leading whitespace must not manufacture an empty visual line.
                    pieces.Clear(); usedWidth = 0;
                }
                available = Available();
                if (word is not null && (word.Width <= available || !canSplit))
                { pieces.Add(word); usedWidth += word.Width; continue; }
                var boundaries = TextFlow.GraphemeBoundaries(key.Text.Substring(token.Start, token.Length));
                int position = 0;
                while (position < boundaries.Length - 1)
                {
                    var next = FitPrefix(token, boundaries, position, Available());
                    pieces.Add(next.Piece); usedWidth += next.Piece.Width; position += next.Count;
                    if (position < boundaries.Length - 1) Finish(token.Start + boundaries[position], automatic: true);
                }
            }
            Finish(key.Text.Length, paragraphEnd: true);
            var info = new TextLayoutMetrics(maxWidth, Math.Max(y, inkBottom), glyphCount, metrics.ToImmutable(), mixed)
                { Tabs = tabMetrics?.ToImmutable() ?? [], HasUnsupportedTabDirection = unsupportedTabDirection };
            long bytes = 256 + key.Text.Length * 2L + key.Ranges.Length * 160L + glyphCount * 32L + metrics.Count * 96L + draw.Count * 160L + (tabMetrics?.Count ?? 0) * 48L;
            return new(key, info, draw, bytes);
        }

        catch { foreach (var run in draw) run.Blob.Dispose(); throw; }
        finally { foreach (var font in fonts.Values) font.Dispose(); }
    }
}
