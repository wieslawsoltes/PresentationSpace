using PresentationSpace.Core;
using PresentationSpace.Rendering.Skia;
using Xunit;

namespace PresentationSpace.Tests;

public sealed class TextFlowValidationTests
{
    [Theory] [InlineData("one\vtwo")] [InlineData("one\r\ntwo")] [InlineData("one\u2028two")] [InlineData("one\u2029two")]
    public void ShapeNamesUseFirstVisualLineWithoutDroppingContent(string text)
    {
        var shape = SlideFactory.Text(text, 0, 0, 300, 200);
        Assert.Equal("one", shape.Name); Assert.Equal(text, shape.Text);
    }
    [Theory] [InlineData("\n")] [InlineData("\r")] [InlineData("\r\n")] [InlineData("\u2029")]
    public void ParagraphFormattingAgreesWithLayoutDelimiters(string delimiter)
    {
        var shape = SlideFactory.Text("One" + delimiter + "Two\vsoft\u2028line" + delimiter + "Three", 0, 0, 300, 200);
        int start = 3 + delimiter.Length;
        var changed = RichTextEditing.FormatParagraphs(shape, start + 2, 1, style => style with { Bullets = true });
        Assert.False(RichText.StyleAt(changed, 0).Bullets);
        Assert.True(RichText.StyleAt(changed, start).Bullets);
        Assert.True(RichText.StyleAt(changed, start + 9).Bullets);
        Assert.False(RichText.StyleAt(changed, changed.Text.Length - 1).Bullets);
        using var renderer = new SlideRenderer();
        Assert.Equal(3, renderer.LayoutRichText(changed, 1000).Lines.Count(l => l.ParagraphStart));
    }
    [Theory] [InlineData(0, 5)] [InlineData(4, 0)] [InlineData(3, 2)]
    public void CrlfSelectionDoesNotLeakFormattingIntoNextParagraph(int start, int length)
    {
        var shape = SlideFactory.Text("One\r\nTwo", 0, 0, 300, 200);
        var changed = RichTextEditing.FormatParagraphs(shape, start, length, style => style with { Bullets = true });
        Assert.True(RichText.StyleAt(changed, 0).Bullets);
        Assert.False(RichText.StyleAt(changed, 5).Bullets);
    }
    [Theory] [InlineData("")] [InlineData(" ")] [InlineData(null)]
    public void InvalidFontFamilyIsRejectedBeforeCommittingDocument(string? family)
    {
        var shape = SlideFactory.Text("test", 0, 0, 300, 200) with { TextStyle = new() { FontFamily = family! } };
        Assert.Throws<InvalidDataException>(() => DocumentSerializer.Validate(new() { Slides = [new() { Shapes = [shape] }] }));
        using var renderer = new SlideRenderer();
        Assert.Throws<InvalidDataException>(() => renderer.LayoutRichText(shape, 300));
    }
    [Fact] public void InvalidRangeStyleIsRejectedBeforeRenderingAndMutation()
    {
        var shape = SlideFactory.Text("test", 0, 0, 300, 200) with { TextRanges = [new(0, 1, new() { Alignment = (ParagraphAlignment)999 })] };
        Assert.Throws<InvalidDataException>(() => DocumentSerializer.Validate(new() { Slides = [new() { Shapes = [shape] }] }));
    }
    [Fact] public void TextLimitIsSharedByModelAndLayout()
    {
        var shape = SlideFactory.Text(new string('x', TextFlow.MaximumTextLength + 1), 0, 0, 300, 200);
        Assert.Throws<InvalidDataException>(() => DocumentSerializer.Validate(new() { Slides = [new() { Shapes = [shape] }] }));
    }
}
