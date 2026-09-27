using PresentationSpace.Core;
using Xunit;

namespace PresentationSpace.Tests;

public sealed class ChartCommandTests
{
    private static SlideShape Shape() => ChartModel.Apply(new(), new()
    {
        Categories = ["A", "B"],
        Series = [new() { Color = "#D35230", Values = [2, null] }, new() { Color = "#4472C4", Values = [3, 5] }]
    });
    [Fact] public void ExistingShapeFillCommandUpdatesFirstSeriesWithoutLosingOtherData()
    {
        var shape = Shape(); var session = new EditorSession(new() { Slides = [new() { Shapes = [shape] }] }); session.Select(shape.Id);
        session.Apply("Shape fill", s => s with { Fill = "#112233" });
        Assert.Equal("#112233", session.PrimaryShape!.Chart!.Series[0].Color);
        Assert.Equal(shape.Chart!.Series[1], session.PrimaryShape.Chart.Series[1]); Assert.Null(session.PrimaryShape.Chart.Series[0].Values[1]);
        session.Undo(); Assert.Same(shape, session.PrimaryShape);
    }
    [Fact] public void ExplicitSeriesChangesRemainAuthoritative()
    {
        var shape = Shape(); var session = new EditorSession(new() { Slides = [new() { Shapes = [shape] }] }); session.Select(shape.Id);
        session.Apply("Series color", s => ChartModel.Apply(s, s.Chart! with { Series = s.Chart.Series.SetItem(1, s.Chart.Series[1] with { Color = "#112233" }) }));
        Assert.Equal("#D35230", session.PrimaryShape!.Chart!.Series[0].Color); Assert.Equal("#112233", session.PrimaryShape.Chart.Series[1].Color);
    }
    [Fact] public void ThemeAccentChangesPreserveCustomSeriesColorsAndMissingValues()
    {
        var original = Shape(); var changed = SlideFactory.ApplyTheme(new() { Slides = [new() { Shapes = [original] }] }, "Ocean").Slides[0].Shapes[0];
        Assert.Equal("#187EAB", changed.Chart!.Series[0].Color); Assert.Equal("#187EAB", changed.Fill);
        Assert.Same(original.Chart!.Series[1], changed.Chart.Series[1]); Assert.Null(changed.Chart.Series[0].Values[1]);
    }
    [Fact] public void LockedChartCommandsDoNotChangeData()
    {
        var shape = Shape() with { Locked = true }; var session = new EditorSession(new() { Slides = [new() { Shapes = [shape] }] }); session.Select(shape.Id);
        session.Apply("Shape fill", s => s with { Fill = "#112233" }); Assert.Same(shape, session.PrimaryShape);
    }
}
