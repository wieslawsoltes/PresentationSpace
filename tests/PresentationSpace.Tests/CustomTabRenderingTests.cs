using System.Collections.Immutable;
using PresentationSpace.Core;
using PresentationSpace.Rendering.Skia;
using SkiaSharp;
using Xunit;

namespace PresentationSpace.Tests;

public sealed class CustomTabRenderingTests
{
    [Theory] [InlineData(TextTabAlignment.Left)] [InlineData(TextTabAlignment.Center)] [InlineData(TextTabAlignment.Right)] [InlineData(TextTabAlignment.Decimal)]
    public void PlacementMatchesMeasuredGlyphAdvances(TextTabAlignment alignment)
    {
        using var engine = new TextLayoutEngine(); var style = new TextStyle { FontSize = 24, TabStops = [new(220, alignment)] };
        var layout = engine.Measure("Label\t12.50", style, 500);
        var tab = Assert.Single(layout.Tabs); var field = engine.Measure("12.50", style, 500);
        float position = alignment switch { TextTabAlignment.Left => tab.FieldStart, TextTabAlignment.Center => (tab.FieldStart + tab.FieldEnd) / 2,
            TextTabAlignment.Decimal => tab.FieldStart + engine.Measure("12", style, 500).Width, _ => tab.FieldEnd };
        Assert.InRange(Math.Abs(position - 220), 0, .05f);
        Assert.InRange(Math.Abs(tab.FieldEnd - tab.FieldStart - field.Width), 0, .05f);
        Assert.Equal(5, tab.SourceIndex); Assert.Equal(0, tab.LineIndex); Assert.False(tab.Clamped);
    }
    [Fact] public void DecimalColumnsRemainAlignedAcrossDifferentNumberLengths()
    {
        using var engine = new TextLayoutEngine(); var style = new TextStyle { FontSize = 24, TabStops = [new(300, TextTabAlignment.Decimal)] };
        foreach (string number in new[] { "1.25", "12.5", "1234.00", "-5.125" })
        {
            var tab = Assert.Single(engine.Measure("\t" + number, style, 600).Tabs);
            float prefix = engine.Measure(number[..number.IndexOf('.')], style, 600).Width;
            Assert.InRange(Math.Abs(tab.FieldStart + prefix - 300), 0, .05f);
        }
        Assert.InRange(Math.Abs(Assert.Single(engine.Measure("\t1234", style, 600).Tabs).FieldEnd - 300), 0, .05f);
    }
    [Fact] public void ConsecutiveStopsProduceIndependentAlignedFields()
    {
        using var engine = new TextLayoutEngine(); var style = new TextStyle { FontSize = 20, TabStops = [new(100), new(250, TextTabAlignment.Center), new(460, TextTabAlignment.Right)] };
        var tabs = engine.Measure("\tA\tLonger text\t23.50", style, 600).Tabs;
        Assert.Equal(3, tabs.Length);
        Assert.Equal(100, tabs[0].FieldStart); Assert.InRange(Math.Abs((tabs[1].FieldStart + tabs[1].FieldEnd) / 2 - 250), 0, .05f);
        Assert.InRange(Math.Abs(tabs[2].FieldEnd - 460), 0, .05f);
    }
    [Fact] public void MixedSizeAndPaintChangesDoNotBreakRightAlignment()
    {
        var shape = SlideFactory.Text("\tMix small", 0, 0, 500, 160, 20) with { TextStyle = new() { FontSize = 20, TabStops = [new(420, TextTabAlignment.Right)] } };
        shape = RichText.Format(shape, 1, 3, s => s with { FontSize = 46, Bold = true, Color = "#CC0000" });
        using var renderer = new SlideRenderer(); var metrics = renderer.LayoutRichText(shape, 500);
        Assert.InRange(Math.Abs(Assert.Single(metrics.Tabs).FieldEnd - 420), 0, .05f);
        using var bitmap = new SKBitmap(500, 180); using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White); renderer.DrawRichText(canvas, shape, 0);
        Assert.Contains(bitmap.Pixels, c => c.Red > 140 && c.Green < 80 && c.Blue < 80);
    }
    [Fact] public void ParagraphAlignmentDoesNotTranslateExplicitStopsAgain()
    {
        using var engine = new TextLayoutEngine(); var style = new TextStyle { FontSize = 24, TabStops = [new(250, TextTabAlignment.Right)] };
        foreach (var alignment in Enum.GetValues<ParagraphAlignment>())
        {
            var metrics = engine.Measure("A\ttext", style with { Alignment = alignment }, 600);
            Assert.InRange(Math.Abs(Assert.Single(metrics.Tabs).FieldEnd - 250), 0, .05f);
            Assert.False(metrics.Lines[0].Justified);
        }
    }
    [Fact] public void ParagraphAndSoftLineBreaksResetTheTabCaret()
    {
        using var engine = new TextLayoutEngine(); var style = new TextStyle { TabStops = [new(200)] };
        var tabs = engine.Measure("\ta\n\tb\v\tc", style, 600).Tabs;
        Assert.Equal(3, tabs.Length); Assert.All(tabs, t => Assert.Equal(200, t.FieldStart));
        Assert.Equal(new[] { 0, 1, 2 }, tabs.Select(t => t.LineIndex));
    }
    [Fact] public void TabsRespectContentInsetsAndDoNotRewriteTheSource()
    {
        var text = "\tRight";
        var shape = SlideFactory.Text(text, 10, 20, 500, 100) with { TextBox = new() { MarginLeft = 30 }, TextStyle = new() { TabStops = [new(200, TextTabAlignment.Right)] } };
        using var renderer = new SlideRenderer();
        Assert.InRange(Math.Abs(Assert.Single(renderer.LayoutRichText(shape, 500).Tabs).FieldEnd - 200), 0, .05f);
        Assert.Equal(text, shape.Text); Assert.Equal(30, TextBoxModel.ContentBounds(shape).X - shape.Bounds.X);
    }
    [Fact] public void OutOfBoundsCustomStopsRemainClippedInsteadOfDisappearing()
    {
        using var engine = new TextLayoutEngine(); var style = new TextStyle { TabStops = [new(900)] };
        var metrics = engine.Measure("\tHidden", style, 400);
        Assert.True(metrics.Width > 900); Assert.Equal(900, Assert.Single(metrics.Tabs).FieldStart);
        using var bitmap = new SKBitmap(400, 100); using var canvas = new SKCanvas(bitmap); canvas.Clear(SKColors.White);
        engine.Draw(canvas, "\tHidden", style, new(0, 0, 400, 100), 0);
        Assert.All(bitmap.Pixels, c => Assert.Equal(SKColors.White, c));
    }
    [Fact] public void StopsWithNoTextStillHaveValidMetrics()
    {
        using var engine = new TextLayoutEngine(); var metrics = engine.Measure("\t\t", new() { TabStops = [new(100), new(200)] }, 400);
        Assert.Equal(2, metrics.Tabs.Length); Assert.Equal(200, metrics.Tabs[1].FieldEnd);
        Assert.All(metrics.Tabs, t => Assert.True(float.IsFinite(t.FieldEnd)));
    }
    [Fact] public void CustomTabDefinitionsParticipateInLayoutInvalidation()
    {
        using var engine = new TextLayoutEngine(); var style = new TextStyle { TabStops = [new(100)] };
        var first = engine.Measure("\tText", style, 500); var second = engine.Measure("\tText", style, 500);
        Assert.Same(first, second);
        var changed = engine.Measure("\tText", style with { TabStops = [new(300)] }, 500);
        Assert.Equal(300, Assert.Single(changed.Tabs).FieldStart); Assert.Equal(100, Assert.Single(first.Tabs).FieldStart);
    }
    [Fact] public void ReusedPaintPreservesPixelsAndCanvasState()
    {
        using var engine = new TextLayoutEngine(); using var bitmap = new SKBitmap(600, 200); using var canvas = new SKCanvas(bitmap);
        var style = new TextStyle { TabStops = [new(200, TextTabAlignment.Right)], Color = "#CC2020", Underline = true };
        canvas.Translate(10, 10); canvas.ClipRect(new(0, 0, 500, 120)); var matrix = canvas.TotalMatrix; var clip = canvas.DeviceClipBounds;
        canvas.Clear(SKColors.White); engine.Draw(canvas, "\tCache", style, new(0, 0, 500, 100), 0); var first = bitmap.Pixels;
        engine.Draw(canvas, "\tOther", style with { Color = "#2020CC" }, new(0, 0, 500, 100), 0);
        canvas.Clear(SKColors.White); engine.Draw(canvas, "\tCache", style, new(0, 0, 500, 100), 0);
        Assert.Equal(first, bitmap.Pixels); Assert.Equal(matrix, canvas.TotalMatrix); Assert.Equal(clip, canvas.DeviceClipBounds);
        engine.Dispose(); engine.Dispose();
        Assert.Throws<ObjectDisposedException>(() => engine.Draw(canvas, "", style, new(0, 0, 2, 2), 20));
    }
}
