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

    public static string FirstLine(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        int end = text.AsSpan().IndexOfAny('\r', '\n', '\v');
        int unicode = text.AsSpan().IndexOfAny('\u2028', '\u2029');
        if (unicode >= 0 && (end < 0 || unicode < end)) end = unicode;
        return end < 0 ? text : text[..end];
    }

    public static bool IsParagraphBreak(char value) => value is '\r' or '\n' or '\u2029';

    /// <summary>Style invariants shared by document edits, table validation and text layout.</summary>
    public static void ValidateStyle(TextStyle? style)
    {
        if (style is null || string.IsNullOrWhiteSpace(style.FontFamily) || style.FontFamily.Length > 256 ||
            !float.IsFinite(style.FontSize) || style.FontSize < 1 || style.FontSize > 2048 ||
            !float.IsFinite(style.LineSpacing) || style.LineSpacing <= 0 || style.LineSpacing > 10 ||
            !Enum.IsDefined(style.Alignment) || !Enum.IsDefined(style.VerticalAlignment) ||
            style.LineSpacingPoints is { } points && (!float.IsFinite(points) || points <= 0 || points > 10000) ||
            !TextBoxModel.ValidMargin(style.SpaceBefore) || !TextBoxModel.ValidMargin(style.SpaceAfter) ||
            style.ParagraphLeftMargin is { } left && !TextBoxModel.ValidMargin(left) ||
            !TextBoxModel.ValidMargin(style.ParagraphRightMargin) || !TextBoxModel.ValidMargin(style.DefaultTabSize) ||
            style.ParagraphIndent is { } indent && (!float.IsFinite(indent) || Math.Abs(indent) > 10000))
            throw new InvalidDataException("Invalid text style.");
        TextTabStops.Validate(style.TabStops);
    }

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
        '\u00a0' or '\u2007' or '\u2011' or '\u202f' or '\u2060' or '\ufeff' => TextTokenKind.Word,
        _ => char.IsWhiteSpace(c) ? TextTokenKind.Space : TextTokenKind.Word
    };

    /// <summary>Emergency wrapping never splits a grapheme, surrogate pair or explicit non-breaking group.</summary>
    public static bool AllowsEmergencyBreak(ReadOnlySpan<char> text) => text.IndexOfAny('\u00a0', '\u202f', '\u2060') < 0 && text.IndexOfAny('\ufeff', '\u2007', '\u2011') < 0;

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
