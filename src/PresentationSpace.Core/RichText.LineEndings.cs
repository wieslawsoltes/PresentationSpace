using System.Collections.Immutable;

namespace PresentationSpace.Core;

public static partial class RichText
{
    // Recognize separator-only normalization, not arbitrary edits or a soft/hard
    // break conversion. Offsets inside a collapsed CRLF map after the one output
    // separator; the CR's style wins when the two code units had different styles.
    private static bool TryReconcileLineEndings(SlideShape source, string text, out SlideShape normalized)
    {
        normalized = source;
        if (!source.Text.Contains('\r') && !text.Contains('\r')) return false;
        int oldIndex = 0, newIndex = 0, delta = 0;
        List<(int OldEnd, int NewEnd)>? changes = null;
        static bool Break(char c) => c is '\r' or '\n';
        static int Length(string s, int i) => s[i] == '\r' && i + 1 < s.Length && s[i + 1] == '\n' ? 2 : 1;
        while (oldIndex < source.Text.Length && newIndex < text.Length)
        {
            if (Break(source.Text[oldIndex]) && Break(text[newIndex]))
            {
                oldIndex += Length(source.Text, oldIndex); newIndex += Length(text, newIndex);
                if (newIndex - oldIndex != delta)
                {
                    delta = newIndex - oldIndex;
                    (changes ??= []).Add((oldIndex, newIndex));
                }
            }
            else if (source.Text[oldIndex] == text[newIndex]) { oldIndex++; newIndex++; }
            else return false;
        }
        if (oldIndex != source.Text.Length || newIndex != text.Length) return false;
        if (changes is null) { normalized = source with { Text = text }; return true; }
        int Map(int offset)
        {
            int low = 0, high = changes.Count;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (changes[middle].OldEnd <= offset) low = middle + 1; else high = middle;
            }
            return low == 0 ? offset : offset + changes[low - 1].NewEnd - changes[low - 1].OldEnd;
        }
        var ranges = ImmutableArray.CreateBuilder<TextRangeStyle>(source.TextRanges.Length);
        foreach (var range in source.TextRanges)
        {
            int start = Map(range.Start), length = Map(range.Start + range.Length) - start;
            if (length > 0) ranges.Add(range with { Start = start, Length = length });
        }
        normalized = source with { Text = text, TextRanges = ranges.ToImmutable() };
        return true;
    }
}
