using PresentationSpace.Core;
using Xunit;

namespace PresentationSpace.Tests;

public class RichTextEditingTests
{
    private static SlideShape Text(string text) => SlideFactory.Text(text, 0, 0, 500, 300);

    [Fact] public void ReplaceAllRetainsInterveningAndReplacementStyles()
    {
        var shape = RichText.Format(Text("x RED x BLUE x"), 2, 3, style => style with { Color = "#FF0000" });
        shape = RichText.Format(shape, 8, 4, style => style with { Color = "#0000FF", Bold = true });
        shape = RichText.Format(shape, 13, 1, style => style with { Italic = true });
        var result = RichTextEditing.ReplaceAll(shape, "x", "long");
        Assert.Equal("long RED long BLUE long", result.Text);
        Assert.Equal("#FF0000", RichText.StyleAt(result, 5).Color);
        Assert.Equal("#0000FF", RichText.StyleAt(result, 14).Color);
        Assert.True(RichText.StyleAt(result, 19).Italic);
        DocumentSerializer.Validate(new() { Slides = [new() { Shapes = [result] }] });
    }
    [Fact] public void ReplaceAllNoMatchPreservesIdentity()
    {
        var shape = Text("unchanged"); Assert.Same(shape, RichTextEditing.ReplaceAll(shape, "missing", "new"));
    }
    [Fact] public void ReplaceAllDoesNotCreateBrokenSurrogates()
    {
        var shape = Text("A😀B😀C"); Assert.Same(shape, RichTextEditing.ReplaceAll(shape, "\uD83D", "broken"));
        Assert.Equal("AxBxC", RichTextEditing.ReplaceAll(shape, "😀", "x").Text);
    }
    [Fact] public void ReplaceAllHandlesManyMatchesWithoutRepeatedWholeStringCopies()
    {
        var shape = Text(string.Concat(Enumerable.Repeat("A ", 50000)) + "Z");
        shape = RichText.Format(shape, shape.Text.Length - 1, 1, style => style with { Bold = true });
        var result = RichTextEditing.ReplaceAll(shape, "A ", "B");
        Assert.Equal(50001, result.Text.Length); Assert.True(RichText.StyleAt(result, 50000).Bold);
    }
    [Fact] public void ReplaceAllIsOneSessionUndoCommand()
    {
        var shape = RichText.Format(Text("x RED x"), 2, 3, style => style with { Bold = true });
        var source = new PresentationDocument { Slides = [new() { Shapes = [shape] }] };
        var session = new EditorSession(source); session.Select(shape.Id); session.ReplaceText("x", "long");
        Assert.True(RichText.StyleAt(session.PrimaryShape!, 6).Bold);
        session.Undo(); Assert.Same(source, session.Document);
    }
    [Fact] public void ParagraphFormattingExpandsToTheCurrentParagraphOnly()
    {
        var shape = RichText.Format(Text("One\nTwo\nThree"), 5, 1, style => style with { Bold = true });
        var result = RichTextEditing.FormatParagraphs(shape, 5, 1, style => style with { Alignment = ParagraphAlignment.Center });
        Assert.Equal(ParagraphAlignment.Left, RichText.StyleAt(result, 0).Alignment);
        Assert.Equal(ParagraphAlignment.Center, RichText.StyleAt(result, 4).Alignment);
        Assert.True(RichText.StyleAt(result, 5).Bold);
        Assert.Equal(ParagraphAlignment.Left, RichText.StyleAt(result, 8).Alignment);
    }
    [Fact] public void SelectionEndingAfterNewlineDoesNotFormatTheNextParagraph()
    {
        var result = RichTextEditing.FormatParagraphs(Text("One\nTwo"), 0, 4, style => style with { Bullets = true });
        Assert.True(RichText.StyleAt(result, 0).Bullets); Assert.False(RichText.StyleAt(result, 4).Bullets);
    }
    [Fact] public void CaretFormatsCurrentParagraph()
    {
        var result = RichTextEditing.FormatParagraphs(Text("One\nTwo"), 5, 0, style => style with { Bullets = true });
        Assert.False(RichText.StyleAt(result, 0).Bullets); Assert.True(RichText.StyleAt(result, 4).Bullets);
    }
    [Fact] public void IncrementalDraftChangesPreserveAnUntouchedMiddleRun()
    {
        var source = RichText.Format(Text("x RED x"), 2, 3, style => style with { Color = "#FF0000" });
        var first = RichText.Reconcile(source, source with { Text = "long RED x" });
        var second = RichText.Reconcile(first, first with { Text = "long RED long" });
        Assert.Equal("#FF0000", RichText.StyleAt(second, 5).Color);
    }
}
