using System.Collections.Immutable;
using PresentationSpace.Core;
using Xunit;

namespace PresentationSpace.Tests;

public sealed class GradientModelTests
{
    [Theory] [InlineData("\n")] [InlineData("\r")] [InlineData("\r\n")]
    public void StopDefinitionsRespectNativeLineEndings(string separator)
    {
        var stops = GradientModel.ParseStops("0 #ff0000 100" + separator + "50% #00FF00 40%" + separator + "100 #0000FF");
        Assert.Equal(3, stops.Length); Assert.Equal(new GradientStop(.5f, "#00FF00", .4f), stops[1]);
        Assert.True(stops.SequenceEqual(GradientModel.ParseStops(GradientModel.FormatStops(stops))));
    }
    [Theory] [InlineData("")] [InlineData("0 #123456")] [InlineData("0 red\n100 #000000")] [InlineData("0 #ABC\n100 #000000")]
    [InlineData("80 #000000\n20 #FFFFFF")] [InlineData("NaN #000000\n100 #FFFFFF")]
    [InlineData("0 #000000 101\n100 #FFFFFF")] [InlineData("-1 #000000\n100 #FFFFFF")] [InlineData("0 #000000\n101 #FFFFFF")]
    public void InvalidStopDefinitionsFail(string text) => Assert.Throws<InvalidDataException>(() => GradientModel.ParseStops(text));
    [Theory] [InlineData(-1)] [InlineData(360)] [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)]
    public void InvalidAnglesFail(float angle) => Assert.Throws<InvalidDataException>(() => GradientModel.Validate(new() { Angle = angle }));
    [Fact] public void CountsAndNullStopsAreBounded()
    {
        Assert.Throws<InvalidDataException>(() => GradientModel.Validate(new() { Stops = default }));
        Assert.Throws<InvalidDataException>(() => GradientModel.Validate(new() { Stops = [null!, new(1, "#FFFFFF")] }));
        Assert.Throws<InvalidDataException>(() => GradientModel.ParseStops(string.Join('\n', Enumerable.Repeat("0 #123456", 65))));
        Assert.Throws<InvalidDataException>(() => GradientModel.ParseStops(new string(' ', 8193)));
    }
    [Fact] public void ReversalRetainsHardEdgesAndOpacity()
    {
        var fill = new GradientFill { Stops = [new(.1f, "#FF0000"), new(.5f, "#FF0000", .4f), new(.5f, "#0000FF"), new(.9f, "#0000FF")] };
        var reverse = GradientModel.Reverse(fill); Assert.Equal("#0000FF", reverse.Stops[0].Color);
        Assert.Equal(.4f, reverse.Stops[2].Opacity); Assert.Equal(.5f, reverse.Stops[1].Offset); Assert.Equal(.5f, reverse.Stops[2].Offset);
        Assert.Equal(4, fill.Stops.Length);
    }
    [Theory] [InlineData(0, 1, 0)] [InlineData(90, 0, 1)] [InlineData(180, -1, 0)] [InlineData(270, 0, -1)]
    public void CardinalGeometryHasFullSpan(float angle, int x, int y)
    {
        var v = GradientModel.Vector(new() { Angle = angle }, new(10, 20, 300, 100));
        Assert.InRange(Math.Abs(v.End.X - v.Start.X - x * 300), 0, .001);
        Assert.InRange(Math.Abs(v.End.Y - v.Start.Y - y * 100), 0, .001);
    }
    [Fact] public void ScaledAngleUsesAspectRatioAndFixedAngleCancelsModelRotation()
    {
        var bounds = new RectF(0, 0, 300, 100);
        var v = GradientModel.Vector(new() { Angle = 45 }, bounds);
        Assert.InRange((v.End.X - v.Start.X) / (v.End.Y - v.Start.Y), 2.999, 3.001);
        var unscaled = GradientModel.Vector(new() { Angle = 45, Scaled = false }, bounds);
        Assert.InRange((unscaled.End.X - unscaled.Start.X) / (unscaled.End.Y - unscaled.Start.Y), .999, 1.001);
        var fixedFill = GradientModel.Vector(new() { RotateWithShape = false }, bounds, 90);
        Assert.InRange(Math.Abs(fixedFill.End.X - fixedFill.Start.X), 0, .001); Assert.True(fixedFill.End.Y < fixedFill.Start.Y);
    }
    [Fact] public void NativeSchemaEightPreservesAllGradientContainers()
    {
        var document = GradientSample.Create(); var json = DocumentSerializer.Serialize(document); var copy = DocumentSerializer.Deserialize(json);
        Assert.Equal(8, copy.SchemaVersion);
        Assert.True(GradientModel.Equivalent(document.Slides[0].BackgroundGradient, copy.Slides[0].BackgroundGradient));
        foreach (var (a, b) in document.Slides[0].Shapes.Zip(copy.Slides[0].Shapes))
        {
            Assert.True(GradientModel.Equivalent(a.FillGradient, b.FillGradient));
            if (a.Table is not null) foreach (var (c, d) in a.Table.Cells.Zip(b.Table!.Cells)) Assert.True(GradientModel.Equivalent(c.FillGradient, d.FillGradient));
        }
        Assert.Equal(1, DocumentSerializer.Deserialize(DocumentSerializer.Serialize(new())).SchemaVersion);
        foreach (int version in Enumerable.Range(1, 8)) Assert.Equal(version, DocumentSerializer.Deserialize(DocumentSerializer.Serialize(new() { SchemaVersion = version })).SchemaVersion);
    }
    [Fact] public void ResizingAndUndoPreserveNormalizedStopsAndContent()
    {
        var d = GradientSample.Create(); var shape = d.Slides[0].Shapes.First(s => s.FillGradient is not null);
        var session = new EditorSession(d); session.Select(shape.Id);
        session.Apply("Gradient", s => GradientModel.Apply(s, GradientSample.Preset("Violet")));
        session.Undo(); Assert.Same(shape, session.PrimaryShape);
        var copy = DocumentLayout.Resize(d, 960, 720);
        Assert.Same(d.Slides[0].BackgroundGradient, copy.Slides[0].BackgroundGradient);
        Assert.Same(shape.FillGradient, copy.Slides[0].Shapes.First(s => s.Id == shape.Id).FillGradient);
        Assert.Equal(d.Slides[0].Shapes.Select(s => s.Text), copy.Slides[0].Shapes.Select(s => s.Text));
    }
    [Theory] [InlineData(ShapeKind.Chart)] [InlineData(ShapeKind.Line)] [InlineData(ShapeKind.Table)]
    public void UnsupportedObjectKindsRejectShapeFill(ShapeKind kind) => Assert.Throws<InvalidOperationException>(() => GradientModel.Apply(new() { Kind = kind }, new()));
}
