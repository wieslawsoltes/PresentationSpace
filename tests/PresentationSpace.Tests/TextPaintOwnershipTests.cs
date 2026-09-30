using PresentationSpace.Core;
using PresentationSpace.Rendering.Skia;
using SkiaSharp;
using Xunit;

namespace PresentationSpace.Tests;

public sealed class TextPaintOwnershipTests
{
    [Fact]
    public void RendererCacheReleaseAllowsNewOwnedDrawingPaint()
    {
        var shape = SlideFactory.Text("A\t12.50", 0, 0, 400, 100, 24) with
        {
            TextStyle = new() { FontSize = 24, TabStops = [new(200, TextTabAlignment.Decimal)] }
        };
        using var bitmap = new SKBitmap(400, 100);
        using var canvas = new SKCanvas(bitmap);
        using var renderer = new SlideRenderer();
        canvas.Clear(SKColors.White);
        renderer.DrawRichText(canvas, shape);
        var expected = bitmap.Pixels;
        renderer.ClearRenderCache();
        Assert.Equal(0, renderer.TextCacheStatistics.Entries);
        canvas.Clear(SKColors.White);
        renderer.DrawRichText(canvas, shape);
        Assert.Equal(expected, bitmap.Pixels);
        // Existing Uno hosts release caches on unload, then reuse the renderer.
        renderer.Dispose();
        canvas.Clear(SKColors.White);
        renderer.DrawRichText(canvas, shape);
        Assert.Equal(expected, bitmap.Pixels);
    }
}
