using System.IO.Compression;
using DocumentFormat.OpenXml.Validation;
using PresentationSpace.Core;
using PresentationSpace.Formats;
using PresentationSpace.Rendering.Skia;
using SkiaSharp;
using Xunit;
using OpenXmlPresentation = DocumentFormat.OpenXml.Packaging.PresentationDocument;

namespace PresentationSpace.Tests;

public sealed class ChartPresentationTests
{
    [Theory, InlineData("#FFFFFF"), InlineData("#00000000"), InlineData("#F2F7FE")]
    public void ChartAreaBackgroundSurvivesNativeSchemaAndRoundTrip(string background)
    {
        var chart = new ChartSpec { Background = background, Categories = ["A"], Series = [new() { Values = [12] }] };
        var document = new PresentationDocument { Slides = [new() { Shapes = [ChartModel.Apply(new(), chart)] }] };
        byte[] bytes = PptxCodec.Export(document).Data;
        using var input = new MemoryStream(bytes); using var package = OpenXmlPresentation.Open(input, false);
        Assert.Empty(new OpenXmlValidator().Validate(package));
        Assert.Equal(background, PptxCodec.Import(bytes).Document.Slides[0].Shapes[0].Chart!.Background);
    }
    [Fact] public void OpaqueChartAreaHidesObjectsBehindItAndTransparentAreaDoesNot()
    {
        var chart = new ChartSpec { Categories = ["A"], Series = [new() { Values = [12] }] };
        using var surface = SKSurface.Create(new SKImageInfo(640, 360)); using var renderer = new SlideRenderer();
        surface.Canvas.Clear(SKColors.Red); renderer.RenderChart(surface.Canvas, chart, new(0, 0, 640, 360), new());
        using var opaque = surface.Snapshot(); using var first = SKBitmap.FromImage(opaque); Assert.Equal(SKColors.White, first.GetPixel(2, 2));
        surface.Canvas.Clear(SKColors.Red); renderer.RenderChart(surface.Canvas, chart with { Background = "#00000000" }, new(0, 0, 640, 360), new());
        using var transparent = surface.Snapshot(); using var second = SKBitmap.FromImage(transparent); Assert.Equal(SKColors.Red, second.GetPixel(2, 2));
    }
    [Fact] public void InsertingAChartCreatesReadableBoundsAndVersionTwoData()
    {
        var session = new EditorSession(); session.Insert(ShapeKind.Chart); var shape = session.PrimaryShape!;
        Assert.NotNull(shape.Chart); Assert.True(shape.Chart.ShowLegend); Assert.Equal("#FFFFFF", shape.Chart.Background);
        Assert.InRange(shape.Bounds.Right, 1, session.Document.Width); Assert.InRange(shape.Bounds.Bottom, 1, session.Document.Height);
        Assert.True(shape.Bounds.Width >= session.Document.Width * .5f);
        Assert.Equal(2, DocumentSerializer.Deserialize(DocumentSerializer.Serialize(session.Document)).SchemaVersion);
    }
}
