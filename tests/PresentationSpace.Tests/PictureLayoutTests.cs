using PresentationSpace.Core;
using Xunit;

namespace PresentationSpace.Tests;

public sealed class PictureLayoutTests
{
    [Fact] public void LegacyPicturesKeepContainWhileExplicitPicturesStretch()
    {
        var shape = new SlideShape {Kind=ShapeKind.Image};
        Assert.Equal(PictureFit.Contain,PictureModel.Resolve(shape).Fit);
        Assert.Equal(PictureFit.Stretch,new PictureSpec().Fit);
    }
    [Theory] [InlineData(PictureFit.Stretch,0,0,200,200)] [InlineData(PictureFit.Contain,0,50,200,100)] [InlineData(PictureFit.Cover,0,0,200,200)]
    public void FitsUseExactSourceAndDestinationGeometry(PictureFit fit,float x,float y,float w,float h)
    {
        var result=PictureModel.Place(new(){Fit=fit},new(0,0,200,200),100,50);
        Assert.Equal(new RectF(x,y,w,h),result.Destination); Assert.False(result.Empty);
        Assert.Equal(fit==PictureFit.Cover?new RectF(25,0,50,50):new RectF(0,0,100,50),result.Source);
    }
    [Fact] public void CroppingAndDestinationOffsetsAreIndependent()
    {
        var result=PictureModel.Place(new(){Source=new(.25f,0,.25f,0),Destination=new(.1f,.2f,.1f,.2f)},new(10,20,200,100),100,50);
        Near(new(25,0,50,50),result.Source); Near(new(30,40,160,60),result.Destination);
    }
    [Fact] public void SourceOutsetsAreTransparentRatherThanStretchedEdgePixels()
    {
        var result=PictureModel.Place(new(){Source=new(-.5f,0,-.5f,0)},new(0,0,200,100),100,50);
        Near(new(0,0,100,50),result.Source); Near(new(50,0,100,100),result.Destination);
    }
    [Fact] public void OffImageWindowsProduceEmptyPlacement()
    {
        var result=PictureModel.Place(new(){Source=new(2,0,-2,0)},new(0,0,200,100),100,50);
        Assert.True(result.Empty);
    }
    [Theory] [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(-11)] [InlineData(11)] [InlineData(1)]
    public void InvalidCropIsRejected(float left) => Assert.Throws<InvalidDataException>(()=>PictureModel.Validate(new(){Source=new(left)}));
    [Theory] [InlineData(-.01f)] [InlineData(1.01f)] [InlineData(float.NaN)]
    public void InvalidAlphaIsRejected(float opacity) => Assert.Throws<InvalidDataException>(()=>PictureModel.Validate(new(){Opacity=opacity}));
    [Theory] [InlineData(0,100)] [InlineData(100,0)] [InlineData(float.NaN,100)] [InlineData(100,float.PositiveInfinity)]
    public void InvalidLayoutBoundsAreRejected(float w,float h) => Assert.Throws<ArgumentOutOfRangeException>(()=>PictureModel.Frame(new(),w,h,100,50));
    [Fact] public void InsetsAndEnumsAreValidated()
    {
        Assert.Throws<InvalidDataException>(()=>PictureModel.Validate(new(){Source=null!}));
        Assert.Throws<InvalidDataException>(()=>PictureModel.Validate(new(){Fit=(PictureFit)10}));
        Assert.Throws<InvalidDataException>(()=>PictureModel.Validate(new(){Mask=(PictureMask)10}));
        Assert.Throws<InvalidDataException>(()=>PictureModel.Validate(new(){Destination=new(.5f,0,.5f,0)}));
    }
    [Fact] public void PictureEditsAreUndoableAndPreserveOriginalAssetAndGeometry()
    {
        var shape=new SlideShape{Kind=ShapeKind.Image,Name="Picture",Text="",Rotation=27};
        var session=new EditorSession(new(){Slides=[new(){Shapes=[shape]}]}); session.Select(shape.Id);
        var picture=new PictureSpec{Source=new(.2f,.1f),FlipHorizontal=true,Opacity=.6f};
        session.Apply("Crop picture",s=>PictureModel.Apply(s,picture));
        Assert.Equal(picture,session.PrimaryShape!.Picture); Assert.Equal(shape.Bounds,session.PrimaryShape.Bounds);
        session.Undo(); Assert.Same(shape,session.PrimaryShape); session.Redo(); Assert.Equal(picture,session.PrimaryShape!.Picture);
        Assert.Throws<InvalidOperationException>(()=>PictureModel.Apply(new(){Kind=ShapeKind.Text},picture));
    }
    [Fact] public void NativeVersionAndResizingPreserveFractionalFraming()
    {
        var picture=new PictureSpec{Fit=PictureFit.Cover,Source=new(.25f,0,.1f,0),Mask=PictureMask.Ellipse,Opacity=.5f};
        var shape=new SlideShape{Kind=ShapeKind.Image,Picture=picture};
        var original=new PresentationDocument{Slides=[new(){Shapes=[shape]}]};
        var copy=DocumentSerializer.Deserialize(DocumentSerializer.Serialize(original));
        Assert.Equal(7,copy.SchemaVersion); Assert.Equal(picture,copy.Slides[0].Shapes[0].Picture);
        var resized=DocumentLayout.Resize(original,960,720).Slides[0].Shapes[0];
        Assert.Same(picture,resized.Picture); Assert.Equal(shape.Id,resized.Id);
        Assert.Equal(shape.Bounds.Width*.75f,resized.Bounds.Width);
    }
    [Fact] public void NonPictureShapesCannotCarryPictureProperties()
    {
        Assert.Throws<InvalidDataException>(()=>DocumentSerializer.Validate(new(){Slides=[new(){Shapes=[new(){Picture=new()}]}]}));
    }
    [Fact] public void EllipsePictureHitTestingUsesTheVisibleMaskAndRotation()
    {
        var shape=new SlideShape{Kind=ShapeKind.Image,Bounds=new(50,50,200,100),Picture=new(){Mask=PictureMask.Ellipse},Rotation=37};
        Assert.False(Geometry.HitTest(shape,Geometry.Rotate(new(51,51),shape.Bounds.Center,37)));
        Assert.True(Geometry.HitTest(shape,shape.Bounds.Center));
    }
    [Fact] public void RandomFramesRemainFiniteAndMatchTheirAffineSourceMapping()
    {
        var random=new Random(72931);
        for(int n=0;n<400;n++)
        {
            float left=(float)random.NextDouble()*.4f, top=(float)random.NextDouble()*.4f;
            var spec=new PictureSpec{Source=new(left,top,.1f,.1f),Fit=(PictureFit)(n%3)};
            var frame=PictureModel.Frame(spec,517,293,1600,900); var placed=PictureModel.Place(spec,new(10,20,517,293),1600,900);
            Assert.False(placed.Empty); Assert.True(float.IsFinite(placed.Destination.Right));
            Near(new(frame.Source.X*1600,frame.Source.Y*900,frame.Source.Width*1600,frame.Source.Height*900),placed.Source);
            Near(new(10+frame.Destination.X*517,20+frame.Destination.Y*293,frame.Destination.Width*517,frame.Destination.Height*293),placed.Destination);
        }
    }
    private static void Near(RectF a,RectF b)
    {
        Assert.InRange(Math.Abs(a.X-b.X),0,.001f); Assert.InRange(Math.Abs(a.Y-b.Y),0,.001f);
        Assert.InRange(Math.Abs(a.Width-b.Width),0,.001f); Assert.InRange(Math.Abs(a.Height-b.Height),0,.001f);
    }
}
