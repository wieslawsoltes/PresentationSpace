using System.Collections.Immutable;
using System.Globalization;
using PresentationSpace.Core;
using Xunit;

namespace PresentationSpace.Tests;

public sealed class StrokeModelTests
{
    [Fact] public void LegacyDefaultsAndExplicitNoneAreDifferent()
    {
        var arrow = new SlideShape { Kind = ShapeKind.Arrow };
        Assert.Equal(LineEndKind.Triangle, StrokeModel.Resolve(arrow).End.Kind);
        Assert.Equal(LineEndKind.None, StrokeModel.Resolve(arrow with { Outline = new() }).End.Kind);
        Assert.Equal(StrokeCap.Flat, StrokeModel.Resolve(new() { Kind = ShapeKind.Image }).Cap);
    }
    [Theory] [InlineData(0,0,200,0)] [InlineData(200,0,0,0)] [InlineData(0,200,0,0)] [InlineData(10,30,20,5)] [InlineData(30,5,10,20)] [InlineData(12,12,12,12)]
    public void LinesKeepEndpointOrderAndZeroExtents(float x1,float y1,float x2,float y2)
    {
        var shape = StrokeModel.Line(new(x1,y1),new(x2,y2),true); var endpoints = StrokeModel.Endpoints(shape);
        Assert.Equal(new PointF(x1,y1),endpoints.Start); Assert.Equal(new PointF(x2,y2),endpoints.End);
        var d = new PresentationDocument { Slides = [new() { Shapes = [shape] }] }; DocumentSerializer.Validate(d);
        var copy = DocumentSerializer.Deserialize(DocumentSerializer.Serialize(d));
        Assert.Equal(9, copy.SchemaVersion); Assert.Equal(endpoints, StrokeModel.Endpoints(copy.Slides[0].Shapes[0]));
    }
    [Fact] public void DirectionChangesHitTestingAndKeepsRotatedResizeFinite()
    {
        var line = StrokeModel.Line(new(100,0),new(0,100));
        Assert.True(Geometry.HitTest(line,new(80,20),0)); Assert.False(Geometry.HitTest(line,new(20,20),0));
        var horizontal = StrokeModel.Line(new(0,0),new(100,0));
        Assert.True(Geometry.HitTest(horizontal,new(50,0),0));
        var resized = Geometry.ResizeRotated(horizontal.Bounds,30,3,new(20,0),true);
        Assert.True(float.IsFinite(resized.X)); Assert.Equal(0,resized.Height); Assert.Equal(120,resized.Width);
    }
    [Fact] public void NoOpApplyKeepsIdentityEvenWithNewArrays()
    {
        var shape = new SlideShape { Outline = new() { CustomDashes = [new(4,2),new(0,1)] } };
        var settings = StrokeModel.Settings(shape) with { Style = shape.Outline with { CustomDashes = [new(4,2),new(0,1)] } };
        Assert.Same(shape, StrokeModel.Apply(shape,settings));
    }
    [Fact] public void DashesUseInvariantCultureAndAllNativeLineSeparators()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pl-PL");
            var values = StrokeModel.ParseDashes("4.25 2\r0 1\r\n1.25 3\n");
            Assert.Equal(3,values.Length); Assert.Equal("4.25 2\n0 1\n1.25 3",StrokeModel.FormatDashes(values));
            Assert.True(values.AsSpan().SequenceEqual(StrokeModel.ParseDashes(StrokeModel.FormatDashes(values)).AsSpan()));
        }
        finally { CultureInfo.CurrentCulture = before; }
    }
    [Theory] [InlineData("NaN 1")] [InlineData("1 Infinity")] [InlineData("-1 2")] [InlineData("0 0")] [InlineData("4")] [InlineData("4 2 1")] [InlineData("1000.00001 2")] [InlineData("1e-100 0")]
    public void InvalidAuthoringFails(string text) => Assert.Throws<InvalidDataException>(()=>StrokeModel.ParseDashes(text));
    [Fact] public void DashCountIsBounded() => Assert.Throws<InvalidDataException>(()=>StrokeModel.ParseDashes(string.Join('\n',Enumerable.Repeat("1 1",33))));
    [Theory] [InlineData(-1)] [InlineData(0)] [InlineData(101)] [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)]
    public void InvalidMiterFails(float miter) => Assert.Throws<InvalidDataException>(()=>StrokeModel.Validate(new StrokeSpec { MiterLimit=miter }));
    [Fact] public void InvalidEnumsAndNullEndsFail()
    {
        Assert.Throws<InvalidDataException>(()=>StrokeModel.Validate(new StrokeSpec { Dash=(StrokeDash)99 }));
        Assert.Throws<InvalidDataException>(()=>StrokeModel.Validate(new StrokeSpec { Begin=null! }));
        Assert.Throws<InvalidDataException>(()=>StrokeModel.Validate(new StrokeSpec { End=new() { Width=(LineEndSize)9 } }));
        Assert.Throws<InvalidDataException>(()=>StrokeModel.Validate(new StrokeSpec { CustomDashes=default }));
    }
    [Fact] public void SchemaAndUnsupportedKindsAreGuarded()
    {
        var ordinary = new PresentationDocument { Slides=[new() { Shapes=[new()] }] };
        Assert.Equal(1,DocumentSerializer.Deserialize(DocumentSerializer.Serialize(ordinary)).SchemaVersion);
        var advanced = ordinary with { Slides=[new() { Shapes=[new() { Outline=new() }] }] };
        Assert.Equal(9,DocumentSerializer.Deserialize(DocumentSerializer.Serialize(advanced)).SchemaVersion);
        Assert.Throws<InvalidDataException>(()=>DocumentSerializer.Validate(ordinary with { Slides=[new() { Shapes=[new() { Kind=ShapeKind.Table,Outline=new() }] }] }));
        Assert.Throws<InvalidDataException>(()=>DocumentSerializer.Validate(ordinary with { Slides=[new() { Shapes=[new() { LineDirection=new() }] }] }));
        Assert.Throws<InvalidDataException>(()=>DocumentSerializer.Validate(ordinary with { Slides=[new() { Shapes=[new() { Bounds=new(0,0,10,0) }] }] }));
    }
    [Fact] public void SizingScalesWidthsNotRelativeDashAndArrowRatios()
    {
        var shape=StrokeModel.Line(new(20,40),new(200,40),true) with { StrokeWidth=8,Outline=new() { CustomDashes=[new(4,2)] } };
        var d=new PresentationDocument { Slides=[new() { Shapes=[shape] }] }; var next=DocumentLayout.Resize(d,640,360).Slides[0].Shapes[0];
        Assert.Equal(4,next.StrokeWidth); Assert.Equal(0,next.Bounds.Height); Assert.Same(shape.Outline,next.Outline); Assert.Equal(shape.LineDirection,next.LineDirection);
    }
    [Fact] public void SolidColorClearsOnlyGradient()
    {
        var shape=new SlideShape { Outline=new() { Dash=StrokeDash.Dot,Gradient=GradientSample.Preset("Ocean") } };
        var next=StrokeModel.SolidColor(shape,"#FF0000"); Assert.Equal("#FF0000",next.Stroke); Assert.Null(next.Outline!.Gradient); Assert.Equal(StrokeDash.Dot,next.Outline.Dash);
    }
    [Fact] public void UndoAndSnapshotGuardRetainIndependentProperties()
    {
        var shape=StrokeModel.Line(new(0,0),new(100,0)); var d=new PresentationDocument { Slides=[new() { Shapes=[shape,new()] }] };
        var session=new EditorSession(d);session.Select(shape.Id);var target=SelectionEditSnapshot.Capture(session);
        Assert.True(target.TryApply(session,"Dash",s=>StrokeModel.Apply(s,StrokeModel.Settings(s) with {Style=new() {Dash=StrokeDash.Dot}})));
        session.Undo();Assert.Null(session.PrimaryShape!.Outline);session.Redo();Assert.Equal(StrokeDash.Dot,session.PrimaryShape!.Outline!.Dash);
        session.Select(d.Slides[0].Shapes[1].Id);Assert.False(target.TryApply(session,"Stale",s=>s with {StrokeWidth=999}));
    }
}
