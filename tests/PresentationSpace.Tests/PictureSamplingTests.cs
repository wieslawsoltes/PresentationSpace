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

    [Theory] [InlineData(false)] [InlineData(true)]
    public void ImageBudgetChargesActualRasterStorageIncludingHighBitDepth(bool highBitDepth)
    {
        // Original 16x8 RGBA16 PNG fixture, generated directly from scanlines.
        const string rgba16 = "iVBORw0KGgoAAAANSUhEUgAAABAAAAAIEAYAAACg5qPUAAABl0lEQVR4nC2SIZarMAAAK1ciV0ZWIpGRSCQyEolEIpErKyO/7BFyhB4hR+gR/mRCeWH6IGQmpY+Hn/MxcP6BAQ5whL8wwgAX+IQJjnCHEzxh5PwHZ5jhAt9whQUm+IEbrHCHX3j4NMv/GBAUB45fA4LiyPlpQFCc4GRAUNyemA0IijNcDQiKC9wMCIorPAwIdz7LDAZExUFlC4iKI8doQFScvNICouI2YzEgKs4wGRAVF7gbEBXXO/sLrx6QFA/u7bT7aUBS3JabDEg+mjhmA5LidmU1ICnOnDcDkuICDwOSsyu8DEg8c/b3Hww4Fffd9zEZ0EeTzer697b46pL9WltsM6DfezMOA/qcD+MyoM/9Ml49ICse4GhAVjz6YzZ5VrzARVlWvLvXU+Xmollx5jgMyN4t8DIgK67wZUB2Bp+ieICTAUXxCGcDiuIFrgYUxTvcbsVuQFGc7z2/4eXdovgDXwYUxV/4rwdUxcP99whwNqAqjnA1oCpOcDOgKm5LHgbU+/eoit/wz4Cq+OPe2+yq+Avba/oPKfHgAYyktKwAAAAASUVORK5CYII=";
        var asset = highBitDepth ? new PresentationAsset("rgba16", "image/png", rgba16) : PictureRenderingTests.Asset();
        using var encoded = SKImage.FromEncodedData(Convert.FromBase64String(asset.Base64));
        var raster = encoded.ToRasterImage(true);
        long bytes;
        try
        {
            using var pixels = raster.PeekPixels();
            Assert.NotNull(pixels);
            bytes = (long)pixels.RowBytes * raster.Height;
        }
        finally { if (!ReferenceEquals(encoded, raster)) raster.Dispose(); }
        var deck = PictureRenderingTests.Deck(new(), asset);
        using var renderer = new SlideRenderer { ImageCacheBudget = bytes };
        using var retained = PictureRenderingTests.Render(renderer, deck);
        Assert.Equal(bytes, renderer.ImageCacheStatistics.DecodedPixelBytes);
        Assert.Equal(1, renderer.ImageCacheStatistics.Entries);
        renderer.ImageCacheBudget = bytes - 1;
        using var temporary = PictureRenderingTests.Render(renderer, deck);
        Assert.Equal(0, renderer.ImageCacheStatistics.Entries);
        Assert.Equal(0, renderer.ImageCacheStatistics.DecodedPixelBytes);
        Assert.Equal(retained.Pixels, temporary.Pixels);
    }

}
