using PresentationSpace.Core;
using PresentationSpace.Rendering.Skia;
using SkiaSharp;
using Xunit;

namespace PresentationSpace.Tests;

public sealed class StrokeRenderingTests
{
    private static SlideShape Line()=>StrokeModel.Line(new(20,40),new(180,40)) with {Stroke="#000000",StrokeWidth=4,Outline=new() {Cap=StrokeCap.Flat}};
    private static SKBitmap Draw(SlideRenderer renderer,SlideShape shape,bool cache=true)
    {
        var b=new SKBitmap(220,120);using var c=new SKCanvas(b);c.Clear(SKColors.White);
        var d=new PresentationDocument {Width=220,Height=120,Slides=[new() {Shapes=[shape]}]};
        renderer.EnablePictureCache=cache;renderer.EnableSceneCache=cache;renderer.Render(c,d,d.Slides[0]);return b;
    }
    [Fact] public void FlatRoundAndSquareCapsHaveDifferentExtents()
    {
        using var r=new SlideRenderer();using var flat=Draw(r,Line());using var square=Draw(r,Line() with {Outline=new() {Cap=StrokeCap.Square}});
        Assert.Equal(SKColors.White,flat.GetPixel(18,40));Assert.NotEqual(SKColors.White,square.GetPixel(18,40));
        using var round=Draw(r,Line() with {Outline=new() {Cap=StrokeCap.Round}});Assert.NotEqual(SKColors.White,round.GetPixel(19,40));
    }
    [Fact] public void CustomDashesHaveRealGapsAndDoNotLeakIntoSolidLines()
    {
        using var r=new SlideRenderer();var line=Line() with {Outline=new() {Cap=StrokeCap.Flat,CustomDashes=[new(2,2)]}};
        using var dashed=Draw(r,line,false);Assert.Equal(SKColors.Black,dashed.GetPixel(23,40));Assert.Equal(SKColors.White,dashed.GetPixel(31,40));
        using var solid=Draw(r,line with {Outline=new() {Cap=StrokeCap.Flat}},false);Assert.Equal(SKColors.Black,solid.GetPixel(31,40));
    }
    [Fact] public void ZeroWidthDoesNotRenderHairlinesOrArrowheads()
    {
        using var r=new SlideRenderer();using var b=Draw(r,Line() with {Kind=ShapeKind.Arrow,Outline=null,StrokeWidth=0});Assert.All(b.Pixels,c=>Assert.Equal(SKColors.White,c));
    }
    [Theory] [InlineData(LineEndKind.Triangle)] [InlineData(LineEndKind.Stealth)] [InlineData(LineEndKind.Diamond)] [InlineData(LineEndKind.Oval)] [InlineData(LineEndKind.OpenArrow)]
    public void MarkerTypesHaveVisiblePixelsOutsideTheShaft(LineEndKind kind)
    {
        using var r=new SlideRenderer();var s=Line() with {Outline=new() {Cap=StrokeCap.Flat,Begin=new() {Kind=kind,Width=LineEndSize.Large,Length=LineEndSize.Large},End=new() {Kind=kind,Width=LineEndSize.Large,Length=LineEndSize.Large}}};
        using var b=Draw(r,s);int left=0,right=0;
        for(int y=30;y<38;y++)for(int x=20;x<41;x++){if(b.GetPixel(x,y)!=SKColors.White)left++;if(b.GetPixel(200-x,y)!=SKColors.White)right++;}
        Assert.True(left>0);Assert.True(right>0);
    }
    [Fact] public void DirectionFlipsAffectPaintingAndInvalidateCachedGeometry()
    {
        var s=StrokeModel.Line(new(20,20),new(180,100)) with {Stroke="#000000",StrokeWidth=4};using var r=new SlideRenderer();
        using var before=Draw(r,s);using var after=Draw(r,s with {LineDirection=new(true,false)});
        Assert.NotEqual(SKColors.White,before.GetPixel(60,40));Assert.Equal(SKColors.White,after.GetPixel(60,40));Assert.NotEqual(SKColors.White,after.GetPixel(140,40));
    }
    [Fact] public void OutlineGradientDoesNotBecomeTheInteriorFill()
    {
        var gradient=new GradientFill {Stops=[new(0,"#FF0000"),new(1,"#0000FF")]};var s=new SlideShape {Bounds=new(20,20,160,80),Fill="#FFFFFF",StrokeWidth=6,Outline=new() {Gradient=gradient}};
        using var r=new SlideRenderer();using var b=Draw(r,s);Assert.Equal(SKColors.White,b.GetPixel(100,60));Assert.True(b.GetPixel(21,50).Red>230);Assert.True(b.GetPixel(179,50).Blue>230);
    }
    [Fact] public void ExplicitNoneSuppressesLegacyArrowAndCacheTracksStyle()
    {
        using var r=new SlideRenderer();var s=Line() with {Kind=ShapeKind.Arrow,Outline=null};using var before=Draw(r,s);using var after=Draw(r,s with {Outline=new() {Cap=StrokeCap.Flat}});
        Assert.NotEqual(before.Pixels,after.Pixels);Assert.Equal(SKColors.White,after.GetPixel(170,35));
    }
    [Fact] public void CachedAndUncachedTranslatedRotatedOutlinesAreIdentical()
    {
        var s=Line() with {Rotation=13,Outline=new() {Dash=StrokeDash.Dot,Gradient=GradientSample.Preset("Sunset"),End=new() {Kind=LineEndKind.Triangle}}};
        using var r=new SlideRenderer();using var cached=Draw(r,s);using var direct=Draw(r,s,false);Assert.Equal(cached.Pixels,direct.Pixels);
    }
    [Theory] [InlineData(ShapeKind.Ellipse)] [InlineData(ShapeKind.RoundRectangle)] [InlineData(ShapeKind.Triangle)] [InlineData(ShapeKind.Diamond)] [InlineData(ShapeKind.Line)] [InlineData(ShapeKind.Arrow)]
    public void FractionalRotatedGradientGeometryMatchesAcrossCacheModes(ShapeKind kind)
    {
        var s=Line() with {Kind=kind,Bounds=new(21.75f,20.125f,157.375f,69.5f),Rotation=23.75f,
            Fill="#FFFFFF",FillGradient=GradientSample.Preset("Ocean"),Outline=new() {Cap=StrokeCap.Square,Dash=StrokeDash.DashDot,
                Gradient=GradientSample.Preset("Violet"),Join=StrokeJoin.Miter,End=new() {Kind=LineEndKind.Triangle}},LineDirection=null};
        using var r=new SlideRenderer();using var cached=Draw(r,s);using var direct=Draw(r,s,false);
        Assert.Equal(cached.Pixels,direct.Pixels);
    }
    [Fact] public void StrokeResourcesEvictReleaseAndRecreateSafely()
    {
        using var b=new SKBitmap(220,120);using var c=new SKCanvas(b);using var r=new SlideRenderer {MaximumCachedStrokePatterns=3};
        var s=Line() with {Outline=new() {Dash=StrokeDash.Dash}};
        for(int i=1;i<15;i++)r.DrawLine(c,s with {StrokeWidth=i});Assert.Equal(3,r.StrokeCacheStatistics.Entries);
        r.DrawLine(c,s with {StrokeWidth=14});Assert.True(r.StrokeCacheStatistics.Hits>0);
        r.StrokePatternCacheBudget=1;r.DrawLine(c,s);Assert.Equal(0,r.StrokeCacheStatistics.Entries);
        r.ClearRenderCache();r.Dispose();r.DrawLine(c,s);Assert.Equal(0,r.StrokeCacheStatistics.Entries);
    }
    [Fact] public void StandaloneDrawingPreservesHostState()
    {
        using var b=new SKBitmap(220,120);using var c=new SKCanvas(b);using var r=new SlideRenderer();
        c.Translate(5,3);c.ClipRect(new(0,0,100,100));var matrix=c.TotalMatrix;var clip=c.LocalClipBounds;int count=c.SaveCount;
        r.DrawLine(c,Line() with {Outline=new() {Begin=new() {Kind=LineEndKind.Oval},End=new() {Kind=LineEndKind.OpenArrow}}});
        Assert.Equal(matrix,c.TotalMatrix);Assert.Equal(clip,c.LocalClipBounds);Assert.Equal(count,c.SaveCount);
    }
    [Theory] [InlineData(StrokeCap.Flat,false)] [InlineData(StrokeCap.Round,true)] [InlineData(StrokeCap.Square,true)]
    public void CollapsedSegmentsUseCapsNotUndefinedArrowDirections(StrokeCap cap,bool visible)
    {
        using var r=new SlideRenderer();using var b=Draw(r,StrokeModel.Line(new(60,40),new(60,40),true) with {Stroke="#000000",StrokeWidth=6,Outline=new() {Cap=cap,End=new() {Kind=LineEndKind.Triangle}}});
        Assert.Equal(visible,b.GetPixel(60,40)!=SKColors.White);
    }
    [Fact] public void SampleArtifactsUseTheProductionExporters()
    {
        var d=StrokeSample.Create();using var r=new SlideRenderer();byte[] png=r.ExportPng(d,d.Slides[0],1280);byte[] pdf=r.ExportPdf(d);
        Assert.StartsWith("%PDF",System.Text.Encoding.ASCII.GetString(pdf,0,4));Assert.NotEmpty(png);
        string? root=Environment.GetEnvironmentVariable("RENDER_DIAGNOSTICS");if(root is null)return;Directory.CreateDirectory(root);
        File.WriteAllBytes(Path.Combine(root,"outline-layout.png"),png);File.WriteAllBytes(Path.Combine(root,"outline-layout.pdf"),pdf);
        File.WriteAllBytes(Path.Combine(root,"outline-layout.pptx"),PresentationSpace.Formats.PptxCodec.Export(d).Data);
    }
}
