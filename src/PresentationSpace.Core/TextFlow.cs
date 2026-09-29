using System.Collections.Immutable;
using System.Globalization;

namespace PresentationSpace.Core;

public enum TextTokenKind { Word, Space, Tab, ParagraphBreak, LineBreak, Opportunity }

/// <summary>A UTF-16 source range. CRLF is one paragraph break; no normalized copy of the text is made.</summary>
public readonly record struct TextToken(int Start, int Length, TextTokenKind Kind);

/// <summary>Shared text-flow primitives. This is a space/hyphen wrapping policy, not full UAX #14.</summary>
public static class TextFlow
{
    public const int MaximumTextLength = 1_000_000;

    public static ImmutableArray<TextToken> Tokenize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > MaximumTextLength) throw new InvalidDataException("Text exceeds one million UTF-16 code units.");
        var result = ImmutableArray.CreateBuilder<TextToken>();
        var starts = StringInfo.ParseCombiningCharacters(text);
        // Classify whole extended graphemes. In particular, a combining mark after
        // a space or hyphen must never become an independently wrapped fragment.
        TextTokenKind At(int index)
        {
            int length = (index + 1 < starts.Length ? starts[index + 1] : text.Length) - starts[index];
            var kind = Kind(text[starts[index]]);
            return length > 1 && kind == TextTokenKind.Space ? TextTokenKind.Word : kind;
        }
        for (int i = 0; i < starts.Length;)
        {
            int first = i;
            var kind = At(i);
            i++;
            if (kind is TextTokenKind.Space or TextTokenKind.Word)
                while (i < starts.Length && At(i) == kind &&
                    !(kind == TextTokenKind.Word && text[starts[i - 1]] is '-' or '\u2010')) i++;
            int end = i < starts.Length ? starts[i] : text.Length;
            result.Add(new(starts[first], end - starts[first], kind));
        }
        return result.ToImmutable();
    }

    private static TextTokenKind Kind(char c) => c switch
    {
        '\r' or '\n' or '\u2029' => TextTokenKind.ParagraphBreak,
        '\v' or '\u2028' => TextTokenKind.LineBreak,
        '\t' => TextTokenKind.Tab,
        '\u200b' => TextTokenKind.Opportunity,
        '\u00a0' or '\u202f' or '\u2060' or '\ufeff' => TextTokenKind.Word,
        _ => char.IsWhiteSpace(c) ? TextTokenKind.Space : TextTokenKind.Word
    };

    /// <summary>Emergency wrapping never splits a grapheme, surrogate pair or explicit non-breaking group.</summary>
    public static bool AllowsEmergencyBreak(ReadOnlySpan<char> text) => text.IndexOfAny('\u00a0', '\u202f', '\u2060') < 0 && text.IndexOf('\ufeff') < 0;

    public static ImmutableArray<int> GraphemeBoundaries(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > MaximumTextLength) throw new InvalidDataException("Text exceeds one million UTF-16 code units.");
        var starts = StringInfo.ParseCombiningCharacters(text);
        var result = ImmutableArray.CreateBuilder<int>(starts.Length + 1);
        result.AddRange(starts); result.Add(text.Length);
        return result.MoveToImmutable();
    }
}
