using System.Collections.Immutable;
using PresentationSpace.Core;
using Xunit;

namespace PresentationSpace.Tests;

public sealed class MultilineInputTests
{
    [Theory]
    [InlineData("\n")] [InlineData("\r\n")] [InlineData("\r")]
    public void NativeTabDefinitionsAcceptEveryDesktopAndBrowserLineEnding(string newline)
    {
        var expected = ImmutableArray.Create(new TextTabStop(130, TextTabAlignment.Decimal), new TextTabStop(250, TextTabAlignment.Right));
        var parsed = TextTabStops.Parse(newline + "130 Decimal" + newline + newline + "250 Right" + newline);
        TabAssert.Equal(expected, parsed);
        Assert.Equal("130 Decimal\n250 Right", TextTabStops.Format(parsed));
        Assert.Throws<InvalidDataException>(() => TextTabStops.Parse("250 Right" + newline + "130 Decimal"));
        Assert.Throws<InvalidDataException>(() => TextTabStops.Parse("130 Left" + newline + "130 Right"));
        Assert.Throws<InvalidDataException>(() => TextTabStops.Parse(string.Join(newline, Enumerable.Range(0, 33).Select(i => i + " Left"))));
    }

    [Fact] public void MixedLineEndingsRemainSeparateDefinitions()
    {
        var stops = TextTabStops.Parse("10 Left\r20 Center\r\n30 Right\n40 Decimal\r");
        Assert.Equal(4, stops.Length);
        Assert.Equal(new TextTabStop(40, TextTabAlignment.Decimal), stops[3]);
    }

    [Theory]
    [InlineData("\n", "\r")] [InlineData("\r\n", "\r")] [InlineData("\r\n", "\n")]
    [InlineData("\r", "\r\n")] [InlineData("\n", "\r\n")] [InlineData("\r", "\n")]
    public void NativeSeparatorNormalizationPreservesEveryParagraphsStyles(string before, string after)
    {
        string[] words = ["Alpha", "Béta 🧭", "Gamma"];
        var shape = SlideFactory.Text(string.Join(before, words), 0, 0, 600, 300);
        int middle = words[0].Length + before.Length, last = middle + words[1].Length + before.Length;
        shape = RichText.Format(shape, 0, words[0].Length, s => s with { Bold = true });
        shape = RichText.Format(shape, middle, words[1].Length, s => s with { Italic = true, Color = "#234567", TabStops = [new(100)] });
        shape = RichText.Format(shape, last, words[2].Length, s => s with { Underline = true, FontSize = 39 });
        var result = RichText.Reconcile(shape, shape with { Text = string.Join(after, words) });
        int newMiddle = words[0].Length + after.Length, newLast = newMiddle + words[1].Length + after.Length;
        Assert.Equal(RichText.StyleAt(shape, middle), RichText.StyleAt(result, newMiddle));
        Assert.Equal(RichText.StyleAt(shape, last), RichText.StyleAt(result, newLast));
        Assert.True(RichText.StyleAt(result, 0).Bold);
        Assert.Equal(words[1].Length, result.TextRanges.Single(r => r.Start == newMiddle).Length);
        // The native editor now applies ordinary incremental changes to the remapped draft.
        var inserted = RichText.Reconcile(result, result with { Text = result.Text.Insert(newMiddle + 1, "x") });
        Assert.True(RichText.StyleAt(inserted, newMiddle + 1).Italic);
        Assert.True(RichText.StyleAt(inserted, newLast + 1).Underline);
        Assert.Equal(39, RichText.StyleAt(inserted, newLast + 1).FontSize);
    }

    [Fact] public void NormalizationRetainsEmptyParagraphsAndSoftBreaks()
    {
        var shape = SlideFactory.Text("a\r\n\r\nb\vc\u2028d\rEND\n", 0, 0, 400, 200);
        shape = RichText.Format(shape, shape.Text.IndexOf("END", StringComparison.Ordinal), 3, s => s with { Bold = true });
        var nextText = "a\n\nb\vc\u2028d\nEND\n";
        var result = RichText.Reconcile(shape, shape with { Text = nextText });
        Assert.Equal(nextText, result.Text);
        Assert.True(RichText.StyleAt(result, nextText.IndexOf("END", StringComparison.Ordinal)).Bold);
        Assert.Equal(3, Assert.Single(result.TextRanges).Length);
    }

    [Fact] public void CollapsedCrLfUsesFirstCodeUnitsStyleAndDoesNotLeakIntoNextParagraph()
    {
        var shape = SlideFactory.Text("A\r\nB", 0, 0, 400, 200);
        shape = RichText.Format(shape, 1, 1, s => s with { Bold = true });
        shape = RichText.Format(shape, 2, 1, s => s with { Italic = true });
        var result = RichText.Reconcile(shape, shape with { Text = "A\nB" });
        Assert.True(RichText.StyleAt(result, 1).Bold);
        Assert.False(RichText.StyleAt(result, 1).Italic);
        Assert.Equal(shape.TextStyle, RichText.StyleAt(result, 2));
        Assert.Single(result.TextRanges);
    }

    [Fact] public void NonNormalizationChangesStillUseOrdinaryEditReconciliation()
    {
        var shape = RichText.Format(SlideFactory.Text("A\rB\rC", 0, 0, 400, 200), 4, 1, s => s with { Underline = true });
        var result = RichText.Reconcile(shape, shape with { Text = "A\rBB\rC" });
        Assert.True(RichText.StyleAt(result, 5).Underline);
        Assert.False(RichText.StyleAt(result, 2).Underline);
    }
}
