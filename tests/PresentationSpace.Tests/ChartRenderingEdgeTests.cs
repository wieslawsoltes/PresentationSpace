using System.Collections.Immutable;
using PresentationSpace.Core;
using PresentationSpace.Rendering.Skia;
using SkiaSharp;
using Xunit;

namespace PresentationSpace.Tests;

public sealed class ChartRenderingEdgeTests
{
    private static ChartSpec Data(ChartKind kind, params double?[] values) => new()
    {
        Kind = kind, ShowLegend = false, ShowValues = false,
        Categories = values.Select((_, i) => $"Category {i + 1}").ToImmutableArray(),
        Series = [new() { Values = values.ToImmutableArray() }]
    };

    [Theory, InlineData(double.Epsilon), InlineData(1e-310), InlineData(1e-30), InlineData(1), InlineData(1e30)]
    public void AxisNormalizationRetainsFinitePositions(double value)
    {
        var axis = ChartAxisScale.Create(-value, value);
        Assert.InRange(axis.Fraction(-value), 0, .5);
        Assert.InRange(axis.Fraction(value), .5, 1);
        Assert.Equal(.5, axis.Fraction(0), 10);
        Assert.All(axis.Ticks(), tick => { Assert.True(double.IsFinite(tick.Value)); Assert.InRange(tick.Fraction, 0, 1); });
    }
    [Fact] public void ZeroAxisIsFiniteAndPercentageAxisIsFixed()
    {
        var axis = ChartAxisScale.Create(0, 0); Assert.Equal(0, axis.Fraction(0));
        var percentage = ChartAxisScale.Create(0, .5, true); Assert.Equal(.5, percentage.Fraction(.5)); Assert.Equal(5, percentage.Ticks().Count());
    }
    [Theory, InlineData(ChartKind.Pie), InlineData(ChartKind.Doughnut)]
    public void SingleSlicePaintsAnEntireCircleAndDoughnutRetainsBackground(ChartKind kind)
    {
        using var surface = SKSurface.Create(new SKImageInfo(640, 360)); surface.Canvas.Clear(SKColors.White);
        using var renderer = new SlideRenderer(); renderer.RenderChart(surface.Canvas, Data(kind, 100), new(0, 0, 640, 360), new());
        using var image = surface.Snapshot(); using var bitmap = SKBitmap.FromImage(image);
        // With no title/legend the circular plot is centered at (320,182), outer radius 156.4.
        Assert.NotEqual(SKColors.White, bitmap.GetPixel(320, 55));
        Assert.NotEqual(SKColors.White, bitmap.GetPixel(320, 307));
        Assert.Equal(kind == ChartKind.Pie ? SlideRenderer.Color(ChartModel.PointColor(0)) : SKColors.White, bitmap.GetPixel(320, 182));
    }
    [Fact] public void SixChartTypesHaveDistinctContinuousDataRendering()
    {
        using var renderer = new SlideRenderer(); var images = new HashSet<string>();
        foreach (var kind in Enum.GetValues<ChartKind>())
        {
            using var surface = SKSurface.Create(new SKImageInfo(640, 360)); surface.Canvas.Clear(SKColors.White);
            renderer.RenderChart(surface.Canvas, Data(kind, 12, 24, 18), new(0, 0, 640, 360), new());
            using var image = surface.Snapshot(); using var png = image.Encode(); images.Add(Convert.ToBase64String(png.ToArray()));
        }
        Assert.Equal(6, images.Count);
    }
    [Fact] public void AreaGapsDoNotCreateSpuriousLineMarkers()
    {
        using var renderer = new SlideRenderer();
        using var surface = SKSurface.Create(new SKImageInfo(640, 360)); surface.Canvas.Clear(SKColors.White);
        renderer.RenderChart(surface.Canvas, Data(ChartKind.Area, 12, null, 18), new(0, 0, 640, 360), new());
        using var image = surface.Snapshot(); using var bitmap = SKBitmap.FromImage(image);
        int colored = bitmap.Pixels.Count(c => c.Red > c.Green + 30 && c.Red > c.Blue + 30);
        Assert.Equal(0, colored); // Isolated area points enclose no area; markers are a line-only feature.
    }
    [Theory, InlineData(ChartKind.Column), InlineData(ChartKind.Bar), InlineData(ChartKind.Line), InlineData(ChartKind.Area)]
    public void SubnormalValuesStillProduceFiniteVisibleCharts(ChartKind kind)
    {
        using var renderer = new SlideRenderer(); using var surface = SKSurface.Create(new SKImageInfo(640, 360)); surface.Canvas.Clear(SKColors.White);
        renderer.RenderChart(surface.Canvas, Data(kind, double.Epsilon, double.Epsilon * 2, double.Epsilon), new(0, 0, 640, 360), new());
        using var image = surface.Snapshot(); using var bitmap = SKBitmap.FromImage(image);
        Assert.True(bitmap.Pixels.Count(c => c.Red > c.Green + 30 && c.Red > c.Blue + 30) > 100);
    }
}
