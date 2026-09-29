using PresentationSpace.Core;
using PresentationSpace.Rendering.Skia;
using SkiaSharp;
using Xunit;

namespace PresentationSpace.Tests;

public sealed class TextRunReuseTests
{
    [Fact] public void ReusedGlyphsKeepSourceSpecificPaintAndUnderline()
    {
        var style = new TextStyle { FontSize = 32, Color = "#CC2020" };
        var shape = SlideFactory.Text("office office office", 0, 0, 500, 100) with { TextStyle = style };
        shape = RichText.Format(shape, 7, 6, s => s with { Color = "#2020CC", Underline = true });
        using var renderer = new SlideRenderer();
        var plain = renderer.LayoutRichText(shape with { TextRanges = [] }, 500);
        var mixed = renderer.LayoutRichText(shape, 500);
        Assert.Equal(plain.Width, mixed.Width); Assert.Equal(plain.GlyphCount, mixed.GlyphCount);
        using var bitmap = new SKBitmap(500, 100); using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White); renderer.DrawRichText(canvas, shape);
        Assert.True(bitmap.Pixels.Count(c => c.Red > 130 && c.Blue < 90) > 40);
        Assert.True(bitmap.Pixels.Count(c => c.Blue > 130 && c.Red < 90) > 40);
    }

    [Fact] public void IndexedFontsPreserveEqualMetricsAcrossRepeatedParagraphs()
    {
        const string line = "office office";
        var shape = SlideFactory.Text(line + "\n" + line, 0, 0, 500, 300, 20);
        shape = RichText.Format(shape, 0, 6, s => s with { FontSize = 52, Bold = true });
        shape = RichText.Format(shape, line.Length + 1, 6, s => s with { FontSize = 52, Bold = true });
        using var renderer = new SlideRenderer(); var layout = renderer.LayoutRichText(shape, 500);
        Assert.Equal(2, layout.Lines.Length);
        Assert.Equal(layout.Lines[0].Width, layout.Lines[1].Width);
        Assert.Equal(layout.Lines[0].Height, layout.Lines[1].Height);
        Assert.InRange(Math.Abs(layout.Lines[1].Baseline - layout.Lines[0].Baseline - layout.Lines[0].Height), 0, .001);
    }

    [Fact] public void FontChangesInsideAnExtendedGraphemeDoNotSplitIt()
    {
        var plain = SlideFactory.Text("a\u0301b", 0, 0, 500, 100, 24);
        var ranged = RichText.Format(plain, 1, 1, s => s with { FontSize = 80, Bold = true });
        using var renderer = new SlideRenderer();
        var first = renderer.LayoutRichText(plain, 500); var next = renderer.LayoutRichText(ranged, 500);
        Assert.Equal(first.Width, next.Width); Assert.Equal(first.Height, next.Height);
        Assert.Equal(first.GlyphCount, next.GlyphCount);
    }
}
