using System.Collections.Immutable;
using System.Text;

namespace PresentationSpace.Core;

/// <summary>Content operations shared by the command layer and reusable editors.</summary>
public static class RichTextEditing
{
    public static SlideShape ReplaceAll(SlideShape shape, string find, string replacement, StringComparison comparison = StringComparison.OrdinalIgnoreCase)
    {
        ArgumentNullException.ThrowIfNull(find);
        ArgumentNullException.ThrowIfNull(replacement);
        if (find.Length == 0) return shape;
        var matches = new List<int>();
        int position = 0;
        while (position <= shape.Text.Length - find.Length)
        {
            int match = shape.Text.IndexOf(find, position, comparison);
            if (match < 0) break;
            if (RichText.IsBoundary(shape.Text, match) && RichText.IsBoundary(shape.Text, match + find.Length)) matches.Add(match);
            position = match + find.Length;
        }
        if (matches.Count == 0) return shape;
        var text = new StringBuilder(shape.Text.Length);
        var ranges = new List<TextRangeStyle>();
        using var segments = RichText.Segments(shape, 0, shape.Text.Length).GetEnumerator();
        bool available = segments.MoveNext();
        void Advance(int index)
        {
            while (available && segments.Current.Start + segments.Current.Length <= index) available = segments.MoveNext();
        }
        void Emit(string value, int offset, int length, TextStyle style)
        {
            if (length == 0) return;
            if (style != shape.TextStyle)
            {
                if (ranges.Count > 0 && ranges[^1].Style == style && ranges[^1].Start + ranges[^1].Length == text.Length)
                    ranges[^1] = ranges[^1] with { Length = ranges[^1].Length + length };
                else ranges.Add(new(text.Length, length, style));
            }
            text.Append(value, offset, length);
        }
        void Copy(int index, int end)
        {
            Advance(index);
            while (index < end)
            {
                int stop = available ? Math.Min(end, segments.Current.Start + segments.Current.Length) : end;
                Emit(shape.Text, index, stop - index, available ? segments.Current.Style : shape.TextStyle);
                index = stop; Advance(index);
            }
        }
        position = 0;
        foreach (int match in matches)
        {
            Copy(position, match); Advance(match);
            Emit(replacement, 0, replacement.Length, available ? segments.Current.Style : shape.TextStyle);
            position = match + find.Length;
        }
        Copy(position, shape.Text.Length);
        return shape with { Text = text.ToString(), TextRanges = ranges.ToImmutableArray() };
    }

    public static SlideShape FormatParagraphs(SlideShape shape, int start, int length, Func<TextStyle, TextStyle> format)
    {
        ArgumentNullException.ThrowIfNull(format);
        if (start < 0 || length < 0 || start > shape.Text.Length - length || !RichText.IsBoundary(shape.Text, start) || !RichText.IsBoundary(shape.Text, start + length)) throw new ArgumentOutOfRangeException(nameof(start));
        if (shape.Text.Length == 0) return shape with { TextStyle = format(shape.TextStyle) };
        // CRLF is one delimiter. A caret between its code units belongs to
        // the preceding paragraph; soft breaks do not start another paragraph.
        int first = start;
        if (first > 0 && first < shape.Text.Length && shape.Text[first - 1] == '\r' && shape.Text[first] == '\n') first--;
        while (first > 0 && !TextFlow.IsParagraphBreak(shape.Text[first - 1])) first--;
        int end = length == 0 ? start : start + length - 1;
        while (end < shape.Text.Length && !TextFlow.IsParagraphBreak(shape.Text[end])) end++;
        if (end < shape.Text.Length)
        {
            if (shape.Text[end] == '\r' && end + 1 < shape.Text.Length && shape.Text[end + 1] == '\n') end += 2;
            else end++;
        }
        if (first == end) return shape with { TextStyle = format(shape.TextStyle) };
        return RichText.Format(shape, first, end - first, format);
    }
}
