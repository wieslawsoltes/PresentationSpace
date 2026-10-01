using PresentationSpace.Core;
using PresentationSpace.Rendering.Skia;
using SkiaSharp;
using Xunit;

namespace PresentationSpace.Tests;

public sealed class GradientRenderingTests
{
    private static GradientFill RedBlue => new() { Stops = [new(0, "#FF0000"), new(1, "#0000FF")] };
    private static SKBitmap Render(SlideRenderer renderer, PresentationDocument document)
    {
        var b = new SKBitmap((int)document.Width, (int)document.Height); using var c = new SKCanvas(b); c.Clear(SKColors.White); renderer.Render(c, document, document.Slides[0]); return b;
    }
    [Fact] public void HorizontalStopsAndAlphaHaveExpectedPixels()
    {
        using var b = new SKBitmap(100, 40); using var c = new SKCanvas(b); using var r = new SlideRenderer();
        c.Clear(SKColors.White); r.DrawGradient(c, RedBlue, new(0, 0, 100, 40));
        Assert.True(b.GetPixel(2, 20).Red > 245); Assert.True(b.GetPixel(97, 20).Blue > 245);
        var middle = b.GetPixel(50, 20); Assert.InRange(middle.Red, 124, 132); Assert.InRange(middle.Blue, 124, 132);
        c.Clear(SKColors.White); r.DrawGradient(c, new() { Stops = [new(0, "#FF0000", 0), new(1, "#FF0000", 0)] }, new(0, 0, 100, 40));
        Assert.Equal(SKColors.White, b.GetPixel(50, 20));
    }
    [Fact] public void DuplicateStopsProduceAHardEdge()
    {
        using var b = new SKBitmap(100, 40); using var c = new SKCanvas(b); using var r = new SlideRenderer();
        r.DrawGradient(c, new() { Stops = [new(0, "#FF0000"), new(.5f, "#FF0000"), new(.5f, "#0000FF"), new(1, "#0000FF")] }, new(0, 0, 100, 40));
        Assert.Equal(SKColors.Red, b.GetPixel(49, 20)); Assert.Equal(SKColors.Blue, b.GetPixel(50, 20));
    }
    [Fact] public void HostTransformClipAndSaveStateAreNotChanged()
    {
        using var b = new SKBitmap(100, 100); using var c = new SKCanvas(b); using var r = new SlideRenderer(); c.Clear(SKColors.White);
        c.Translate(10, 10); c.ClipRect(new(0, 0, 30, 30)); var matrix = c.TotalMatrix; var clip = c.LocalClipBounds; int saves = c.SaveCount;
        r.DrawGradient(c, RedBlue, new(0, 0, 80, 80));
        Assert.Equal(matrix, c.TotalMatrix); Assert.Equal(clip, c.LocalClipBounds); Assert.Equal(saves, c.SaveCount); Assert.Equal(SKColors.White, b.GetPixel(60, 60));
    }
    [Fact] public void GradientAndBackgroundEditsInvalidateRetainedPicturesAndScenes()
    {
        var shape = new SlideShape { Bounds = new(10, 10, 80, 30), StrokeWidth = 0, FillGradient = RedBlue };
        var d = new PresentationDocument { Width = 100, Height = 60, Slides = [new() { Shapes = [shape], BackgroundGradient = RedBlue }] };
        using var r = new SlideRenderer(); using var first = Render(r, d);
        var changed = d with { Slides = [d.Slides[0] with { BackgroundGradient = GradientModel.Reverse(RedBlue), Shapes = [shape with { FillGradient = GradientModel.Reverse(RedBlue) }] }] };
        using var next = Render(r, changed);
        Assert.NotEqual(first.GetPixel(12, 20), next.GetPixel(12, 20)); Assert.NotEqual(first.GetPixel(2, 50), next.GetPixel(2, 50));
        using var repeated = Render(r, changed); Assert.Equal(next.Pixels, repeated.Pixels); Assert.True(r.CacheStatistics.SceneHits > 0);
        var solid = changed with { Slides = [changed.Slides[0] with { Shapes = [shape with { Fill = "#00FF00", FillGradient = null }] }] };
        using var cleared = Render(r, solid); Assert.Equal(SKColors.Lime, cleared.GetPixel(30, 20));
    }
    [Fact] public void CacheLimitsTemporaryOwnershipAndReuseAreSafe()
    {
        using var b = new SKBitmap(100, 100); using var c = new SKCanvas(b); using var r = new SlideRenderer { MaximumCachedGradients = 3 };
        var fill = RedBlue;
        for (int i = 0; i < 20; i++) r.DrawGradient(c, fill, new(i, i, 40, 40));
        Assert.Equal(3, r.GradientCacheStatistics.Entries);
        r.DrawGradient(c, fill, new(19, 19, 40, 40)); Assert.True(r.GradientCacheStatistics.Hits > 0);
        r.GradientCacheBudget = 1; r.DrawGradient(c, fill, new(0, 0, 40, 40)); Assert.Equal(0, r.GradientCacheStatistics.Entries);
        r.MaximumCachedGradients = 0; r.DrawGradient(c, fill, new(0, 0, 40, 40)); r.Dispose();
        r.DrawGradient(c, fill, new(0, 0, 40, 40)); Assert.True(b.GetPixel(2, 20).Red > 230);
    }
    [Fact] public void CellsRenderGradientsWithoutShadingTheirTextOrNeighbors()
    {
        var table = TableModel.Create(1, 2) with { HeaderRow = false, BandedRows = false };
        table = table with { Cells = [table.Cells[0] with { FillGradient = RedBlue }, table.Cells[1] with { Fill = "#00FF00" }] };
        using var b = new SKBitmap(200, 60); using var c = new SKCanvas(b); using var r = new SlideRenderer();
        r.RenderTable(c, table, new(0, 0, 200, 60));
        Assert.True(b.GetPixel(5, 30).Red > 230); Assert.True(b.GetPixel(94, 30).Blue > 230); Assert.Equal(SKColors.Lime, b.GetPixel(150, 30));
    }
    [Fact] public void ShapeMaskIsRetainedInPngAndPdfExports()
    {
        var d = new PresentationDocument { Width = 120, Height = 120, Slides = [new() { Shapes = [new() { Kind = ShapeKind.Ellipse, Bounds = new(10, 10, 100, 100), StrokeWidth = 0, FillGradient = RedBlue }] }] };
        using var r = new SlideRenderer(); using var bitmap = SKBitmap.Decode(r.ExportPng(d,  d.Slides[0], 120));
        Assert.Equal(SKColors.White, bitmap.GetPixel(12, 12)); Assert.NotEqual(SKColors.White, bitmap.GetPixel(60, 60));
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(r.ExportPdf(d), 0, 4));
    }
    [Fact] public void SampleArtifactsContainAllContainerTypes()
    {
        string? root = Environment.GetEnvironmentVariable("RENDER_DIAGNOSTICS"); if (root is null) return;
        Directory.CreateDirectory(root); using var renderer = new SlideRenderer(); var d = GradientSample.Create();
        File.WriteAllBytes(Path.Combine(root, "gradient-layout.png"), renderer.ExportPng(d, d.Slides[0], 1280));
        File.WriteAllBytes(Path.Combine(root, "gradient-layout.pdf"), renderer.ExportPdf(d));
        File.WriteAllBytes(Path.Combine(root, "gradient-layout.pptx"), PresentationSpace.Formats.PptxCodec.Export(d).Data);
    }
}
