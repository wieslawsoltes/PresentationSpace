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
    float Indent, bool ParagraphStart, bool RightToLeft);
public sealed record TextLayoutMetrics(float Width, float Height, int GlyphCount,
    ImmutableArray<TextLineMetrics> Lines, bool HasMixedDirection);
public readonly record struct TextLayoutCacheStatistics(long Hits, long Misses, int Entries, long ApproximateBytes);

/// <summary>
/// Single-thread-affine, disposable HarfBuzz/Skia text layout, shared by measurement and drawing.
/// Cached native blobs never escape; returned metrics contain only immutable managed values.
/// Supports horizontal single-direction paragraphs, not complete Unicode bidirectional layout.
/// </summary>
public sealed class TextLayoutEngine : IDisposable
{
    private readonly record struct Key(string Text, TextStyle Style, ImmutableArray<TextRangeStyle> Ranges, float Width);
    private readonly record struct FontKey(string Family, float Size, bool Bold, bool Italic);
    private sealed class NativeFont : IDisposable
    {
        private readonly SKTypeface _face;
        private readonly bool _ownsFace;
        public SKFont Font { get; }
        public SKShaper Shaper { get; }
        public NativeFont(SKTypeface face, bool ownsFace, FontKey key)
        {
            _face = face; _ownsFace = ownsFace;
            Font = new(face, key.Size) { Edging = SKFontEdging.Antialias, Subpixel = true };
            try { Shaper = new(face); }
            catch { Font.Dispose(); if (ownsFace) face.Dispose(); throw; }
        }
        public void Dispose() { Shaper.Dispose(); Font.Dispose(); if (_ownsFace) _face.Dispose(); }
    }
    private sealed record GlyphSpan(NativeFont Font, int Start, SKShaper.Result Glyphs, float[] Advances, TextStyle[] Styles, bool Rtl);
    private sealed record Piece(int Start, int Length, List<GlyphSpan> Spans, float Width, TextStyle Style, bool Space = false);
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
    public ITypefaceResolver? TypefaceResolver { get; set; }
    public int MaximumCachedLayouts { get; set; } = 256;
    /// <summary>Conservative retained-text/glyph estimate, not an exact native-memory limit.</summary>
    public long CacheBudget { get; set; } = 8 * 1024 * 1024;
    public TextLayoutCacheStatistics CacheStatistics => new(_hits, _misses, _entries.Count, _bytes);

    public TextLayoutMetrics Measure(string text, TextStyle style, float width, ImmutableArray<TextRangeStyle> ranges = default)
    {
        var entry = Get(text, style, width, ranges, out bool temporary);
        try { return entry.Metrics; } finally { if (temporary) entry.Dispose(); }
    }

    /// <summary>Draw within the supplied box. Font advances, wrapping and baseline metrics are identical to Measure.</summary>
    public void Draw(SKCanvas canvas, string text, TextStyle style, RectF bounds, float padding = 3, ImmutableArray<TextRangeStyle> ranges = default)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        if (!float.IsFinite(padding) || padding < 0 || !float.IsFinite(bounds.X) || !float.IsFinite(bounds.Y) ||
            !float.IsFinite(bounds.Right) || !float.IsFinite(bounds.Bottom) || bounds.Width <= 0 || bounds.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(bounds));
        var entry = Get(text, style, Math.Max(1, bounds.Width - 2 * padding), ranges, out bool temporary);
        try
        {
            float free = Math.Max(0, bounds.Height - 2 * padding - entry.Metrics.Height);
            float top = bounds.Y + padding + (style.VerticalAlignment switch { Core.VerticalAlignment.Middle => free / 2, Core.VerticalAlignment.Bottom => free, _ => 0 });
            canvas.Save();
            try
            {
                canvas.ClipRect(new(bounds.X, bounds.Y, bounds.Right, bounds.Bottom));
                using var paint = new SKPaint { IsAntialias = true };
                foreach (var run in entry.Runs)
                {
                    paint.Color = run.Color;
                    canvas.DrawText(run.Blob, bounds.X + padding + run.X, top + run.Y, paint);
                    if (run.UnderlineWidth > 0)
                        canvas.DrawRect(bounds.X + padding + run.X + run.UnderlineStart, top + run.Y + run.UnderlineY,
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
    public void Dispose() { if (_disposed) return; Clear(); _disposed = true; }
    private void Trim()
    {
        while (_lru.First is { } first && (_entries.Count > Math.Max(0, MaximumCachedLayouts) || _bytes > Math.Max(0, CacheBudget)))
        { _entries.Remove(first.Value.Key); _bytes -= first.Value.Bytes; first.Value.Dispose(); _lru.RemoveFirst(); }
    }
    private Entry Get(string text, TextStyle style, float width, ImmutableArray<TextRangeStyle> ranges, out bool temporary)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(text); ArgumentNullException.ThrowIfNull(style);
        if (!float.IsFinite(width) || width <= 0 || width > 100000) throw new ArgumentOutOfRangeException(nameof(width));
        if (ranges.IsDefault) ranges = [];
        long version = (TypefaceResolver as IVersionedTypefaceResolver)?.Version ?? 0;
        if (!ReferenceEquals(TypefaceResolver, _observedResolver) || version != _observedVersion)
        { Clear(); _observedResolver = TypefaceResolver; _observedVersion = version; }
        Trim();
        var key = new Key(text, style, ranges, width);
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
        bool mixed = false;
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
        Piece Shape(int start, int length, bool whitespace = false)
        {
            string value = key.Text.Substring(start, length);
            var spans = new List<GlyphSpan>();
            var boundaries = TextFlow.GraphemeBoundaries(value);
            float used = 0;
            for (int i = 0; i < boundaries.Length - 1;)
            {
                int first = i;
                var style = RichText.StyleAt(source, start + boundaries[i]);
                var font = Font(style);
                // Paint-only style boundaries must not disable kerning, ligatures or Arabic joining.
                while (++i < boundaries.Length - 1 && ReferenceEquals(Font(RichText.StyleAt(source, start + boundaries[i])), font)) { }
                string slice = value.Substring(boundaries[first], boundaries[i] - boundaries[first]);
                using var buffer = new HBBuffer(); buffer.AddUtf16(slice); buffer.GuessSegmentProperties();
                var shaped = font.Shaper.Shape(buffer, font.Font);
                var positions = buffer.GlyphPositions;
                var advances = new float[positions.Length];
                var styles = new TextStyle[positions.Length];
                for (int g = 0; g < positions.Length; g++)
                {
                    advances[g] = positions[g].XAdvance * (font.Font.Size / 512f);
                    styles[g] = RichText.StyleAt(source, start + boundaries[first] + (int)shaped.Clusters[g]);
                }
                spans.Add(new(font, start + boundaries[first], shaped, advances, styles, buffer.Direction == Direction.RightToLeft));
                used += shaped.Width;
            }
            return new(start, length, spans, used, RichText.StyleAt(source, start), whitespace);
        }
        Piece Bullet(TextStyle style)
        {
            var font = Font(style);
            using var buffer = new HBBuffer(); buffer.AddUtf16("•"); buffer.GuessSegmentProperties();
            var result = font.Shaper.Shape(buffer, font.Font);
            return new(0, 0, [new(font, 0, result, buffer.GlyphPositions.Select(p => p.XAdvance * font.Font.Size / 512f).ToArray(),
                Enumerable.Repeat(style, result.Codepoints.Length).ToArray(), false)], result.Width, style);
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
                    var style = span.Styles[start];
                    while (end < result.Codepoints.Length && span.Styles[end].Color == style.Color && span.Styles[end].Underline == style.Underline) end++;
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
        var pieces = new List<Piece>();
        var paragraphStyle = key.Style;
        Piece? bullet = null;
        bool firstLine = true, paragraphRtl = false, sawLtr = false, sawRtl = false;
        float indent = 0, usedWidth = 0, y = 0, maxWidth = 0;
        int lineStart = 0;
        void StartParagraph(int offset)
        {
            paragraphStyle = RichText.StyleAt(source, offset);
            firstLine = true; sawLtr = sawRtl = paragraphRtl = false;
            bullet = paragraphStyle.Bullets ? Bullet(paragraphStyle) : null;
            indent = bullet is null ? 0 : paragraphStyle.FontSize * 1.25f;
        }
        void Finish(int end)
        {
            // Trailing breaking spaces do not skew centered/right aligned text. Source indexes are retained.
            while (pieces.Count > 0 && pieces[^1].Space) { usedWidth -= pieces[^1].Width; pieces.RemoveAt(pieces.Count - 1); }
            float ascent = 0, descent = 0, leading = 0, size = 0;
            void Include(TextStyle style)
            {
                var m = Font(style).Font.Metrics;
                ascent = Math.Max(ascent, -m.Ascent); descent = Math.Max(descent, m.Descent);
                leading = Math.Max(leading, m.Leading); size = Math.Max(size, style.FontSize);
            }
            if (pieces.Count == 0 || bullet is not null) Include(paragraphStyle);
            foreach (var piece in pieces) { if (piece.Spans.Count == 0) Include(piece.Style); else foreach (var span in piece.Spans) Include(span.Styles.FirstOrDefault() ?? piece.Style); }
            float ink = ascent + descent;
            float height = Math.Max(ink + Math.Max(0, leading), size * paragraphStyle.LineSpacing);
            float baseline = y + ascent + (height - ink) / 2;
            float width = usedWidth + indent;
            float align = paragraphStyle.Alignment switch { ParagraphAlignment.Center => (key.Width - width) / 2, ParagraphAlignment.Right => key.Width - width, _ => 0 };
            if (firstLine && bullet is not null) Emit(bullet, align + (paragraphRtl ? usedWidth + indent - bullet.Width : 0), baseline);
            float x = align + (paragraphRtl ? 0 : indent);
            for (int i = 0; i < pieces.Count; i++)
            {
                var piece = pieces[paragraphRtl ? pieces.Count - 1 - i : i];
                Emit(piece, x, baseline); x += piece.Width;
            }
            metrics.Add(new(lineStart, Math.Max(0, end - lineStart), width, height, baseline, indent, firstLine, paragraphRtl));
            maxWidth = Math.Max(maxWidth, width); y += height;
            pieces.Clear(); usedWidth = 0; firstLine = false; lineStart = end;
            mixed |= sawLtr && sawRtl;
        }
        try
        {
            StartParagraph(0);
            foreach (var token in TextFlow.Tokenize(key.Text))
            {
                if (token.Kind is TextTokenKind.ParagraphBreak or TextTokenKind.LineBreak)
                {
                    Finish(token.Start); lineStart = token.Start + token.Length;
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
                        float stop = Math.Max(1, Font(style).Font.MeasureText(" ") * 4);
                        float advance = stop - usedWidth % stop;
                        space = new(token.Start, token.Length, [], advance, style, true);
                    }
                    else space = Shape(token.Start, token.Length, true);
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
                var word = Shape(token.Start, token.Length);
                float available = Math.Max(1, key.Width - indent);
                if (usedWidth > 0 && usedWidth + word.Width > available) Finish(token.Start);
                if (word.Width <= available || !TextFlow.AllowsEmergencyBreak(key.Text.AsSpan(token.Start, token.Length)))
                { pieces.Add(word); usedWidth += word.Width; continue; }
                // Exponential probing bounds each prefix search by its line capacity,
                // not the whole remaining word. This avoids quadratic long-URL wrapping.
                var boundaries = TextFlow.GraphemeBoundaries(key.Text.Substring(token.Start, token.Length));
                int position = 0;
                while (position < boundaries.Length - 1)
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
                    pieces.Add(best); usedWidth += best.Width; position += low;
                    if (position < boundaries.Length - 1) Finish(token.Start + boundaries[position]);
                }
            }
            Finish(key.Text.Length);
            var info = new TextLayoutMetrics(maxWidth, y, glyphCount, metrics.ToImmutable(), mixed);
            long bytes = 256 + key.Text.Length * 2L + key.Ranges.Length * 128L + glyphCount * 32L + metrics.Count * 80L + draw.Count * 160L;
            return new(key, info, draw, bytes);
        }
        catch { foreach (var run in draw) run.Blob.Dispose(); throw; }
        finally { foreach (var font in fonts.Values) font.Dispose(); }
    }
}
