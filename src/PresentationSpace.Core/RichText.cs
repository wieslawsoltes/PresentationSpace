using System.Collections.Immutable;

namespace PresentationSpace.Core;

/// <summary>Immutable UTF-16 range operations. Plain Text remains the canonical string.</summary>
public static partial class RichText
{
    public static bool IsBoundary(string text, int index) => index >= 0 && index <= text.Length &&
        (index == 0 || index == text.Length || !(char.IsHighSurrogate(text[index - 1]) && char.IsLowSurrogate(text[index])));

    public static TextStyle StyleAt(SlideShape shape, int index)
    {
        int found = FirstRangeEndingAfter(shape.TextRanges, index);
        if (found < shape.TextRanges.Length && index >= shape.TextRanges[found].Start) return shape.TextRanges[found].Style;
        return shape.TextStyle;
    }
    private static int FirstRangeEndingAfter(ImmutableArray<TextRangeStyle> ranges, int index)
    {
        int lo = 0, hi = ranges.Length;
        while (lo < hi) { int mid = lo + (hi - lo) / 2; if (ranges[mid].Start + ranges[mid].Length <= index) lo = mid + 1; else hi = mid; }
        return lo;
    }

    public static IEnumerable<TextRangeStyle> Segments(SlideShape shape, int start, int length)
    {
        if (start < 0 || length < 0 || start > shape.Text.Length - length) throw new ArgumentOutOfRangeException(nameof(start));
        int end = start + length, position = start;
        for (int i = FirstRangeEndingAfter(shape.TextRanges, start); i < shape.TextRanges.Length; i++)
        {
            var range = shape.TextRanges[i];
            if (range.Start >= end) break;
            if (range.Start > position) { yield return new(position, Math.Min(end, range.Start) - position, shape.TextStyle); position = range.Start; }
            int stop = Math.Min(end, range.Start + range.Length);
            if (stop > position) { yield return new(position, stop - position, range.Style); position = stop; }
        }
        if (position < end) yield return new(position, end - position, shape.TextStyle);
    }

    public static SlideShape Format(SlideShape shape, int start, int length, Func<TextStyle, TextStyle> format)
    {
        ArgumentNullException.ThrowIfNull(format);
        if (start < 0 || length < 0 || start > shape.Text.Length - length || !IsBoundary(shape.Text, start) || !IsBoundary(shape.Text, start + length)) throw new ArgumentOutOfRangeException(nameof(start));
        if (length == 0) return shape;
        var pieces = Segments(shape, 0, start)
            .Concat(Segments(shape, start, length).Select(r => r with { Style = format(r.Style) }))
            .Concat(Segments(shape, start + length, shape.Text.Length - start - length));
        return shape with { TextRanges = Compact(pieces, shape.TextStyle) };
    }

    public static SlideShape Replace(SlideShape shape, int start, int length, string replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        if (start < 0 || length < 0 || start > shape.Text.Length - length || !IsBoundary(shape.Text, start) || !IsBoundary(shape.Text, start + length)) throw new ArgumentOutOfRangeException(nameof(start));
        int delta = replacement.Length - length;
        var parts = Segments(shape, 0, start).ToList();
        if (replacement.Length > 0) parts.Add(new(start, replacement.Length, StyleAt(shape, start < shape.Text.Length ? start : Math.Max(0, start - 1))));
        parts.AddRange(Segments(shape, start + length, shape.Text.Length - start - length).Select(r => r with { Start = r.Start + delta }));
        return shape with { Text = shape.Text.Remove(start, length).Insert(start, replacement), TextRanges = Compact(parts, shape.TextStyle) };
    }

    private static ImmutableArray<TextRangeStyle> Compact(IEnumerable<TextRangeStyle> parts, TextStyle fallback)
    {
        var result = new List<TextRangeStyle>();
        foreach (var part in parts)
        {
            if (part.Length <= 0 || part.Style == fallback) continue;
            if (result.Count > 0 && result[^1].Start + result[^1].Length == part.Start && result[^1].Style == part.Style)
                result[^1] = result[^1] with { Length = result[^1].Length + part.Length };
            else result.Add(part);
        }
        return result.ToImmutableArray();
    }

    // Existing controls edit flat text or a shape's base style. Carry explicit ranges through those changes.
    public static SlideShape Reconcile(SlideShape before, SlideShape after)
    {
        if (ReferenceEquals(before, after) || before.TextRanges.IsEmpty || before.TextRanges != after.TextRanges || (before.Text == after.Text && before.TextStyle == after.TextStyle)) return after;
        var adjusted = before;
        if (before.Text != after.Text)
        {
            // Native multiline input may change all CR/LF spellings at once. Do not
            // treat the text between the first and last delimiter as one replacement:
            // that would erase otherwise unchanged character/paragraph formatting.
            if (TryReconcileLineEndings(before, after.Text, out var normalized)) adjusted = normalized;
            else
            {
                int prefix = 0;
                while (prefix < before.Text.Length && prefix < after.Text.Length && before.Text[prefix] == after.Text[prefix]) prefix++;
                while (!IsBoundary(before.Text, prefix) || !IsBoundary(after.Text, prefix)) prefix--;
                int suffix = 0;
                while (suffix < before.Text.Length - prefix && suffix < after.Text.Length - prefix && before.Text[^(suffix + 1)] == after.Text[^(suffix + 1)]) suffix++;
                while (!IsBoundary(before.Text, before.Text.Length - suffix) || !IsBoundary(after.Text, after.Text.Length - suffix)) suffix--;
                adjusted = Replace(before, prefix, before.Text.Length - prefix - suffix, after.Text.Substring(prefix, after.Text.Length - prefix - suffix));
            }
        }
        var ranges = adjusted.TextRanges;
        if (before.TextStyle != after.TextStyle)
            ranges = ranges.Select(r => r with { Style = ApplyStyleChanges(r.Style, before.TextStyle, after.TextStyle) }).ToImmutableArray();
        return after with { TextRanges = Compact(ranges, after.TextStyle) };
    }

    internal static TextStyle ApplyStyleChanges(TextStyle style, TextStyle old, TextStyle value) => style with
    {
        FontFamily = old.FontFamily == value.FontFamily ? style.FontFamily : value.FontFamily,
        FontSize = old.FontSize == value.FontSize ? style.FontSize : value.FontSize,
        Bold = old.Bold == value.Bold ? style.Bold : value.Bold,
        Italic = old.Italic == value.Italic ? style.Italic : value.Italic,
        Underline = old.Underline == value.Underline ? style.Underline : value.Underline,
        Color = old.Color == value.Color ? style.Color : value.Color,
        Bullets = old.Bullets == value.Bullets ? style.Bullets : value.Bullets,
        Alignment = old.Alignment == value.Alignment ? style.Alignment : value.Alignment,
        VerticalAlignment = old.VerticalAlignment == value.VerticalAlignment ? style.VerticalAlignment : value.VerticalAlignment,
        LineSpacing = old.LineSpacing == value.LineSpacing ? style.LineSpacing : value.LineSpacing,
        LineSpacingPoints = old.LineSpacingPoints == value.LineSpacingPoints ? style.LineSpacingPoints : value.LineSpacingPoints,
        SpaceBefore = old.SpaceBefore == value.SpaceBefore ? style.SpaceBefore : value.SpaceBefore,
        SpaceAfter = old.SpaceAfter == value.SpaceAfter ? style.SpaceAfter : value.SpaceAfter,
        ParagraphLeftMargin = old.ParagraphLeftMargin == value.ParagraphLeftMargin ? style.ParagraphLeftMargin : value.ParagraphLeftMargin,
        ParagraphRightMargin = old.ParagraphRightMargin == value.ParagraphRightMargin ? style.ParagraphRightMargin : value.ParagraphRightMargin,
        ParagraphIndent = old.ParagraphIndent == value.ParagraphIndent ? style.ParagraphIndent : value.ParagraphIndent,
        DefaultTabSize = old.DefaultTabSize == value.DefaultTabSize ? style.DefaultTabSize : value.DefaultTabSize,
        TabStops = old.TabStops == value.TabStops ? style.TabStops : value.TabStops
    };

    public static Slide Reconcile(Slide before, Slide after)
    {
        if (ReferenceEquals(before, after) || before.Shapes == after.Shapes) return after;
        Dictionary<Guid, SlideShape>? lookup = null;
        ImmutableArray<SlideShape>.Builder? changed = null;
        for (int i = 0; i < after.Shapes.Length; i++)
        {
            var shape = after.Shapes[i];
            SlideShape? old = i < before.Shapes.Length && before.Shapes[i].Id == shape.Id ? before.Shapes[i] :
                (lookup ??= before.Shapes.ToDictionary(x => x.Id)).GetValueOrDefault(shape.Id);
            if (old is null || ReferenceEquals(old, shape)) continue;
            var result = Reconcile(old, TableModel.Reconcile(old, shape));
            if (!ReferenceEquals(result, shape)) (changed ??= after.Shapes.ToBuilder())[i] = result;
        }
        return changed is null ? after : after with { Shapes = changed.ToImmutable() };
    }

    public static PresentationDocument Reconcile(PresentationDocument before, PresentationDocument after)
    {
        if (ReferenceEquals(before, after) || before.Slides == after.Slides) return after;
        Dictionary<Guid, Slide>? lookup = null;
        ImmutableArray<Slide>.Builder? changed = null;
        for (int i = 0; i < after.Slides.Length; i++)
        {
            var slide = after.Slides[i];
            Slide? old = i < before.Slides.Length && before.Slides[i].Id == slide.Id ? before.Slides[i] :
                (lookup ??= before.Slides.ToDictionary(x => x.Id)).GetValueOrDefault(slide.Id);
            if (old is null || ReferenceEquals(old, slide)) continue;
            var result = Reconcile(old, slide);
            if (!ReferenceEquals(result, slide)) (changed ??= after.Slides.ToBuilder())[i] = result;
        }
        return changed is null ? after : after with { Slides = changed.ToImmutable() };
    }
}
