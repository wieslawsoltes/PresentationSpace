using System.Collections.Immutable;
using PresentationSpace.Core;
using PresentationSpace.Rendering.Skia;
using SkiaSharp;
using Xunit;

namespace PresentationSpace.Tests;

public sealed class PictureRenderingTests
{
    internal static PresentationAsset Asset(string id="picture",SKColor? solid=null)
    {
        using var bitmap=new SKBitmap(80,40);
        for(int y=0;y<40;y++)for(int x=0;x<80;x++)bitmap.SetPixel(x,y,solid??(y<20?(x<40?SKColors.Red:SKColors.Lime):(x<40?SKColors.Blue:SKColors.Yellow)));
        using var image=SKImage.FromBitmap(bitmap); using var data=image.Encode(SKEncodedImageFormat.Png,100);
        return new(id,"image/png",Convert.ToBase64String(data.ToArray()));
    }
    internal static PresentationDocument Deck(PictureSpec? picture,PresentationAsset? asset=null)
    {
        asset??=Asset();
        return new(){Width=200,Height=200,Assets=ImmutableDictionary<string,PresentationAsset>.Empty.Add(asset.Id,asset),
            Slides=[new(){Shapes=[new(){Kind=ShapeKind.Image,AssetId=asset.Id,Bounds=new(20,20,160,160),Picture=picture,Fill="#00000000",StrokeWidth=0}]}]};
    }
    internal static SKBitmap Render(SlideRenderer renderer,PresentationDocument document)
    {
        var bitmap=new SKBitmap(200,200); using var canvas=new SKCanvas(bitmap);canvas.Clear(SKColors.White); renderer.Render(canvas,document,document.Slides[0]); return bitmap;
    }
    [Theory] [InlineData(false,false)] [InlineData(true,false)] [InlineData(false,true)] [InlineData(true,true)]
    public void FlipAxesReflectPicturePixelsWithoutChangingBounds(bool h,bool v)
    {
        using var renderer=new SlideRenderer(); var deck=Deck(new(){FlipHorizontal=h,FlipVertical=v}); using var b=Render(renderer,deck);
        Assert.Equal(v?(h?SKColors.Yellow:SKColors.Blue):(h?SKColors.Lime:SKColors.Red),b.GetPixel(60,60));
        Assert.Equal(new RectF(20,20,160,160),deck.Slides[0].Shapes[0].Bounds);
    }
    [Fact] public void LegacyContainAndExplicitStretchHaveDifferentLetterboxing()
    {
        using var renderer=new SlideRenderer();using var contain=Render(renderer,Deck(null));using var stretch=Render(renderer,Deck(new()));
        Assert.Equal(SKColors.White,contain.GetPixel(60,30));Assert.Equal(SKColors.Red,stretch.GetPixel(60,30));
    }
    [Fact] public void CropsSelectTheCorrectPixelsAndPreserveTheSource()
    {
        var deck=Deck(new(){Source=new(.5f,0,0,.5f)});var asset=deck.Assets["picture"];
        using var renderer=new SlideRenderer();using var b=Render(renderer,deck);
        Assert.Equal(SKColors.Lime,b.GetPixel(40,40));Assert.Equal(SKColors.Lime,b.GetPixel(160,160));Assert.Same(asset,deck.Assets["picture"]);
    }
    [Fact] public void NegativeSourceOffsetsCreateTransparentPadding()
    {
        using var renderer=new SlideRenderer();using var b=Render(renderer,Deck(new(){Source=new(-.5f,0,-.5f,0)}));
        Assert.Equal(SKColors.White,b.GetPixel(30,60)); Assert.Equal(SKColors.Red,b.GetPixel(80,60));
    }
    [Fact] public void EllipseMasksAreRenderedAndPictureAlphaDoesNotFadeTheirOutline()
    {
        var deck=Deck(new(){Mask=PictureMask.Ellipse,Opacity=.5f},Asset(solid:SKColors.Red));
        using var renderer=new SlideRenderer();using var b=Render(renderer,deck);
        Assert.Equal(SKColors.White,b.GetPixel(22,22)); var color=b.GetPixel(100,100);
        Assert.Equal(255,color.Red); Assert.InRange((int)color.Green,126,129); Assert.Equal(color.Green,color.Blue);
    }
    [Fact] public void CachedAndUncachedPicturePathsHaveMatchingPixelsAndHostState()
    {
        var deck=Deck(new(){Fit=PictureFit.Cover,Source=new(.1f,0,0,0),FlipHorizontal=true,Opacity=.6f});
        using var cached=new SlideRenderer();using var cold=new SlideRenderer{EnableSceneCache=false,EnablePictureCache=false,ImageCacheBudget=0};
        using var first=Render(cached,deck);using var second=Render(cold,deck); Assert.Equal(first.Pixels,second.Pixels);
        using var canvas=new SKCanvas(first);canvas.Translate(7,9);canvas.ClipRect(new(0,0,150,140));var matrix=canvas.TotalMatrix;var clip=canvas.DeviceClipBounds;
        cached.Render(canvas,deck,deck.Slides[0]);Assert.Equal(matrix,canvas.TotalMatrix);Assert.Equal(clip,canvas.DeviceClipBounds);
        Assert.Equal(0,cold.ImageCacheStatistics.Entries);
    }
    [Fact] public void LiveCropAndAssetReplacementInvalidateOnlyWhatChanged()
    {
        using var renderer=new SlideRenderer();var deck=Deck(new());using var initial=Render(renderer,deck);
        var edited=deck with{Slides=[deck.Slides[0] with{Shapes=[deck.Slides[0].Shapes[0] with{Picture=new(){Source=new(.5f,0,0,.5f)}}]}]};
        using var crop=Render(renderer,edited);Assert.Equal(SKColors.Lime,crop.GetPixel(60,60)); Assert.Equal(1,renderer.ImageCacheStatistics.Misses);
        var replaced=edited with{Assets=edited.Assets.SetItem("picture",Asset(solid:SKColors.Magenta))};
        using var next=Render(renderer,replaced);Assert.Equal(SKColors.Magenta,next.GetPixel(60,60));Assert.Equal(2,renderer.ImageCacheStatistics.Misses);
    }
    [Fact] public void ImageCacheHonorsEntryAndByteLimitsIncludingDisabledAndOversizedImages()
    {
        using var renderer=new SlideRenderer{MaximumCachedImages=1,ImageCacheBudget=12800};
        using var one=Render(renderer,Deck(new(),Asset("one")));using var two=Render(renderer,Deck(new(),Asset("two")));
        Assert.Equal(1,renderer.ImageCacheStatistics.Entries);Assert.Equal(12800,renderer.ImageCacheStatistics.DecodedPixelBytes);
        renderer.ImageCacheBudget=1;using var oversized=Render(renderer,Deck(new()));Assert.Equal(0,renderer.ImageCacheStatistics.Entries);
        Assert.Equal(0,renderer.ImageCacheStatistics.DecodedPixelBytes); Assert.Equal(SKColors.Red,oversized.GetPixel(60,60));
        renderer.ImageCacheBudget=12800;renderer.MaximumCachedImages=0;using var disabled=Render(renderer,Deck(new()));Assert.Equal(0,renderer.ImageCacheStatistics.Entries);
    }
    [Fact] public void ClearAndUnloadReleaseImageResourcesAndPermitReuse()
    {
        var renderer=new SlideRenderer();var deck=Deck(new());using var first=Render(renderer,deck);
        renderer.ClearImageCache();Assert.Equal(0,renderer.ImageCacheStatistics.Entries);using var second=Render(renderer,deck);Assert.Equal(first.Pixels,second.Pixels);
        renderer.Dispose();Assert.Equal(0,renderer.ImageCacheStatistics.DecodedPixelBytes);using var third=Render(renderer,deck);Assert.Equal(first.Pixels,third.Pixels);renderer.Dispose();
    }
    [Fact] public void DestinationOutsetsCannotPaintOutsideThePictureFrame()
    {
        using var renderer=new SlideRenderer();using var b=Render(renderer,Deck(new(){Destination=new(-.5f,0,-.5f,0)}));
        Assert.Equal(SKColors.White,b.GetPixel(10,100));Assert.Equal(SKColors.White,b.GetPixel(190,100));
    }
    [Fact] public void SampleUsesSharedPixelsWithSixIndependentNativeFrames()
    {
        var deck=PictureLayoutSample.Create();DocumentSerializer.Validate(deck);Assert.Single(deck.Assets);Assert.Equal(6,deck.Slides[0].Shapes.Count(s=>s.Kind==ShapeKind.Image));
        using var renderer=new SlideRenderer();var path=Environment.GetEnvironmentVariable("RENDER_DIAGNOSTICS");
        if(path is null)return;Directory.CreateDirectory(path);
        File.WriteAllBytes(Path.Combine(path,"picture-layout.png"),renderer.ExportPng(deck,deck.Slides[0]));
        File.WriteAllBytes(Path.Combine(path,"picture-layout.pdf"),renderer.ExportPdf(deck));
        File.WriteAllBytes(Path.Combine(path,"picture-layout.pptx"),PresentationSpace.Formats.PptxCodec.Export(deck).Data);
    }
}
