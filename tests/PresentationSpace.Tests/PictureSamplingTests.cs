using System.Collections.Immutable;
using PresentationSpace.Core;
using PresentationSpace.Rendering.Skia;
using SkiaSharp;
using Xunit;

namespace PresentationSpace.Tests;

public sealed class PictureSamplingTests
{
    [Theory] [InlineData(false)] [InlineData(true)]
    public void MinificationAveragesHighFrequencyPixelsWithAndWithoutCropping(bool cropped)
    {
        using var bitmap = new SKBitmap(256, 128);
        for (int y = 0; y < bitmap.Height; y++) for (int x = 0; x < bitmap.Width; x++)
            bitmap.SetPixel(x, y, x % 8 == 0 ? SKColors.White : SKColors.Black);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        var asset = new PresentationAsset("stripes", "image/png", Convert.ToBase64String(encoded.ToArray()));
        var doc = PictureRenderingTests.Deck(new() { Source = cropped ? new(.25f, 0, .25f, 0) : PictureInsets.Empty }, asset);
        doc = doc with { Slides = [doc.Slides[0] with { Shapes = [doc.Slides[0].Shapes[0] with { Bounds = new(20, 20, 8, 8) }] }] };
        using var renderer = new SlideRenderer();
        using var rendered = PictureRenderingTests.Render(renderer, doc);
        // Each output pixel integrates many full eight-pixel periods. Dropping
        // minification filtering produces aliasing instead of the expected 1/8 gray.
        var center = rendered.GetPixel(24, 24);
        Assert.InRange((int)center.Red, 29, 35);
        Assert.Equal(center.Red, center.Green); Assert.Equal(center.Red, center.Blue);
        using var uncached = new SlideRenderer { ImageCacheBudget = 0 };
        using var comparison = PictureRenderingTests.Render(uncached, doc);
        Assert.Equal(rendered.Pixels, comparison.Pixels);
    }

    [Fact]
    public void PictureTransparencyDoesNotReduceItsOpaqueBorder()
    {
        var doc = PictureRenderingTests.Deck(new() { Opacity = .25f }, PictureRenderingTests.Asset(solid: SKColors.Red));
        doc = doc with { Slides = [doc.Slides[0] with { Shapes = [doc.Slides[0].Shapes[0] with { Stroke = "#000000", StrokeWidth = 6 }] }] };
        using var renderer = new SlideRenderer(); using var rendered = PictureRenderingTests.Render(renderer, doc);
        Assert.Equal(SKColors.Black, rendered.GetPixel(20, 100));
        Assert.InRange((int)rendered.GetPixel(100, 100).Green, 189, 193);
    }
}
