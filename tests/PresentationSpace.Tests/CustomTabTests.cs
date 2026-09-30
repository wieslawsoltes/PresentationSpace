using System.Collections.Immutable;
using System.Globalization;
using PresentationSpace.Core;
using Xunit;

namespace PresentationSpace.Tests;

public sealed class CustomTabTests
{
    private static PresentationDocument Deck(SlideShape shape) => new() { Slides = [new() { Shapes = [shape] }] };
    [Fact] public void DefinitionsParseAndFormatWithoutCultureDependence()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new("pl-PL");
            var expected = ImmutableArray.Create(new TextTabStop(72.5f), new TextTabStop(144, TextTabAlignment.Right), new TextTabStop(288, TextTabAlignment.Decimal));
            var parsed = TextTabStops.Parse("72.5 left\r\n144\tRIGHT\n\n288 Decimal");
            TabAssert.Equal(expected, parsed); TabAssert.Equal(expected, TextTabStops.Parse(TextTabStops.Format(parsed)));
        }
        finally { CultureInfo.CurrentCulture = before; }
    }
    [Theory]
    [InlineData("1 Bogus")] [InlineData("NaN Left")] [InlineData("Infinity Right")]
    [InlineData("-1 Left")] [InlineData("10001 Left")] [InlineData("20 Right\n10 Left")]
    [InlineData("10 Left\n10 Right")] [InlineData("10 Left extra")] [InlineData("10 1")]
    public void InvalidDefinitionsFailAtomically(string value) => Assert.Throws<InvalidDataException>(() => TextTabStops.Parse(value));
    [Fact] public void EmptyDefinitionsUseRegularStops()
    {
        Assert.Empty(TextTabStops.Parse("\n\t "));
        var tab = TextTabStops.Place([], 48, 24); Assert.Equal(72, tab.Stop); Assert.Equal(72, tab.Start); Assert.False(tab.Custom);
    }
    [Fact] public void CountInputAndDefaultArrayLimitsAreEnforced()
    {
        Assert.Throws<InvalidDataException>(() => TextTabStops.Parse(new string(' ', 4097)));
        Assert.Throws<InvalidDataException>(() => TextTabStops.Parse(string.Join('\n', Enumerable.Range(0, 33).Select(i => i + " Left"))));
        Assert.Throws<InvalidDataException>(() => TextTabStops.Validate(default));
        Assert.Throws<InvalidDataException>(() => TextFlow.ValidateStyle(new() { TabStops = [new(10, (TextTabAlignment)99)] }));
    }
    [Theory] [InlineData(TextTabAlignment.Left, 100)] [InlineData(TextTabAlignment.Center, 80)]
    [InlineData(TextTabAlignment.Right, 60)] [InlineData(TextTabAlignment.Decimal, 88)]
    public void AlignmentUsesMeasuredFieldAdvance(TextTabAlignment align, float expected)
    {
        var tab = TextTabStops.Place([new(100, align)], 20, 24, 40, 12);
        Assert.True(tab.Custom); Assert.False(tab.Clamped); Assert.Equal(expected, tab.Start);
    }
    [Fact] public void DecimalWithoutPeriodFallsBackToRightAlignment()
    {
        Assert.Equal(60, TextTabStops.Place([new(100, TextTabAlignment.Decimal)], 20, 24, 40).Start);
    }
    [Fact] public void OverlapClampsAtCaretInsteadOfMovingBackward()
    {
        var tab = TextTabStops.Place([new(100, TextTabAlignment.Right)], 80, 24, 50);
        Assert.Equal(80, tab.Start); Assert.True(tab.Clamped);
    }
    [Fact] public void CustomStopsAfterCaretArePreferredOverRegularIntervals()
    {
        Assert.Equal(300, TextTabStops.Place([new(30), new(300)], 40, 20).Stop);
        Assert.Equal(320, TextTabStops.Place([new(30), new(300)], 300, 20).Stop);
    }
    [Fact] public void TinyRegularIntervalsAlwaysAdvance()
    {
        var tab = TextTabStops.Place([], 10000, .00001f);
        Assert.True(tab.Stop > 10000); Assert.True(float.IsFinite(tab.Stop));
    }
    [Theory] [InlineData(float.NaN)] [InlineData(float.NegativeInfinity)] [InlineData(0)] [InlineData(-1)]
    public void InvalidIntervalAndScaleAreRejected(float value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TextTabStops.Place([], 20, value));
        Assert.Throws<ArgumentOutOfRangeException>(() => TextTabStops.Scale([], value));
    }
    [Fact] public void SchemaSixIsRequiredForAnyCustomStopAndCannotBeDowngraded()
    {
        var shape = SlideFactory.Text("one\ttwo", 10, 10, 400, 100) with { TextStyle = new() { TabStops = [new(150)] } };
        var document = new PresentationDocument { Slides = [new() { Shapes = [shape, new() { TextBox = new() }] }] };
        var copy = DocumentSerializer.Deserialize(DocumentSerializer.Serialize(document));
        Assert.Equal(6, copy.SchemaVersion); TabAssert.Equal(shape.TextStyle.TabStops, copy.Slides[0].Shapes[0].TextStyle.TabStops);
        var table = TableModel.Create(1, 1) with { TextStyle = shape.TextStyle };
        Assert.Equal(6, DocumentSerializer.Deserialize(DocumentSerializer.Serialize(Deck(TableModel.Apply(new(), table)))).SchemaVersion);
    }
    [Fact] public void CharacterStylesCarryIndependentParagraphStopsThroughEditing()
    {
        var shape = SlideFactory.Text("one\ttwo\nthree\tfour", 0, 0, 400, 200);
        shape = RichTextEditing.FormatParagraphs(shape, 1, 0, s => s with { TabStops = [new(180, TextTabAlignment.Right)] });
        shape = RichText.Format(shape, 4, 3, s => s with { Bold = true });
        var next = RichText.Replace(shape, 0, 3, "first");
        Assert.Single(RichText.StyleAt(next, 6).TabStops); Assert.True(RichText.StyleAt(next, 6).Bold);
        Assert.Empty(RichText.StyleAt(next, 10).TabStops);
    }
    [Fact] public void BaseStyleReconciliationDoesNotWipeParagraphCustomStops()
    {
        var shape = SlideFactory.Text("one\ttwo", 0, 0, 400, 100);
        shape = RichText.Format(shape, 0, 3, s => s with { TabStops = [new(120)], Bold = true });
        var result = RichText.Reconcile(shape, shape with { TextStyle = shape.TextStyle with { Color = "#FF0000" } });
        Assert.Single(RichText.StyleAt(result, 0).TabStops); Assert.True(RichText.StyleAt(result, 0).Bold);
        Assert.Equal("#FF0000", RichText.StyleAt(result, 0).Color);
    }
    [Fact] public void WholeObjectLayoutAppliesStopsWithoutClearingCharacterFormatting()
    {
        var shape = RichText.Format(SlideFactory.Text("one\ttwo", 0, 0, 400, 100), 4, 3, s => s with { Italic = true });
        var updated = TextBoxModel.ApplyLayout(shape, new(), shape.TextStyle with { TabStops = [new(140)] });
        Assert.True(RichText.StyleAt(updated, 4).Italic); Assert.Single(RichText.StyleAt(updated, 4).TabStops);
    }
    [Fact] public void ResizeAndTextFitScaleAbsoluteStopsAndPreserveContent()
    {
        var shape = SlideFactory.Text("one\ttwo", 100, 100, 600, 100) with { TextStyle = new() { FontSize = 24, TabStops = [new(100), new(300, TextTabAlignment.Decimal)] } };
        var document = Deck(shape); var copy = DocumentLayout.Resize(document, 640, 360).Slides[0].Shapes[0];
        Assert.Equal(50, copy.TextStyle.TabStops[0].Position); Assert.Equal(150, copy.TextStyle.TabStops[1].Position);
        Assert.Equal(shape.Text, copy.Text); Assert.Equal(shape.Id, copy.Id);
        Assert.Throws<InvalidDataException>(() => TextTabStops.Scale([new(9000)], 2));
    }
    [Fact] public void StopChangesAreTransactionalAndUndoable()
    {
        var shape = SlideFactory.Text("one\ttwo", 0, 0, 400, 100);
        var session = new EditorSession(Deck(shape)); session.Select(shape.Id);
        session.Apply("Tabs", s => TextBoxModel.ApplyLayout(s, new(), s.TextStyle with { TabStops = [new(200)] }));
        Assert.Single(session.PrimaryShape!.TextStyle.TabStops);
        session.Undo(); Assert.Empty(session.PrimaryShape!.TextStyle.TabStops);
        session.Redo(); Assert.Single(session.PrimaryShape!.TextStyle.TabStops);
    }
}
