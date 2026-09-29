using System.Collections.Immutable;
using System.IO.Compression;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Validation;
using PresentationSpace.Core;
using PresentationSpace.Formats;
using PresentationSpace.Rendering.Skia;
using Xunit;
using OfficeDocument = DocumentFormat.OpenXml.Packaging.PresentationDocument;

namespace PresentationSpace.Tests;

public sealed class TextFitAndInterchangeTests
{
    private static SlideShape Text(string text = "A large heading and a smaller annotation") => SlideFactory.Text(text, 40, 50, 250, 65, 38) with
    { TextRanges = [new(0, 1, new() { FontSize = 60, Bold = true, Color = "#285EA8" })] };
    private static PresentationDocument Deck(SlideShape shape) => new() { Slides = [new() { Shapes = [shape] }] };
    [Fact] public void ShrinkToFitPreservesTextRunRatiosAndObjectIdentity()
    {
        using var renderer = new SlideRenderer(); var before = Text(); var fit = renderer.FitTextToShape(before);
        Assert.True(fit.Fits); Assert.InRange(fit.Scale, .1f, .999f);
        Assert.Equal(before.Id, fit.Shape.Id); Assert.Equal(before.Text, fit.Shape.Text); Assert.Equal(before.Bounds, fit.Shape.Bounds);
        Assert.InRange(Math.Abs(fit.Shape.TextRanges[0].Style.FontSize / fit.Shape.TextStyle.FontSize - 60f/38), 0, .001);
        Assert.Equal(before.TextRanges[0].Style.Color, fit.Shape.TextRanges[0].Style.Color);
        Assert.True(renderer.MeasureRichTextHeight(fit.Shape, before.Bounds.Width, 3) <= before.Bounds.Height + .001);
    }
    [Fact] public void FittingTextDoesNotChangeAnAlreadyFittingShape()
    { using var renderer = new SlideRenderer(); var s = Text("a") with { Bounds = new(0, 0, 900, 300) }; var fit = renderer.FitTextToShape(s); Assert.True(fit.Fits); Assert.Same(s, fit.Shape); }
    [Fact] public void ImpossibleFitIsNonDestructive()
    { using var renderer = new SlideRenderer(); var s = Text() with { Bounds = new(0, 0, 10, 10) }; var fit = renderer.FitTextToShape(s); Assert.False(fit.Fits); Assert.Same(s, fit.Shape); }
    [Theory] [InlineData(0)] [InlineData(30)] [InlineData(90)] [InlineData(175)] [InlineData(-60)]
    public void FittingShapeHeightPinsItsRotatedTopEdge(float angle)
    {
        using var renderer = new SlideRenderer(); var shape = Text() with { Rotation = angle }; var next = renderer.FitShapeToText(shape);
        var a = Geometry.Rotate(new(shape.Bounds.Center.X, shape.Bounds.Y), shape.Bounds.Center, angle);
        var b = Geometry.Rotate(new(next.Bounds.Center.X, next.Bounds.Y), next.Bounds.Center, angle);
        Assert.InRange(Math.Abs(a.X - b.X), 0, .001); Assert.InRange(Math.Abs(a.Y - b.Y), 0, .001);
        Assert.Equal(shape.TextStyle, next.TextStyle); Assert.Equal(shape.TextRanges, next.TextRanges); Assert.Equal(shape.Bounds.Width, next.Bounds.Width);
        Assert.Equal(renderer.MeasureRichTextHeight(shape, shape.Bounds.Width, 3), next.Bounds.Height);
    }
    [Fact] public void TextFittingIsUndoableWithoutFlatteningMixedStyles()
    {
        var shape = Text(); var original = Deck(shape); var session = new EditorSession(original); session.Select(shape.Id);
        using var renderer = new SlideRenderer(); session.Apply("Fit", s => renderer.FitTextToShape(s).Shape);
        Assert.InRange(Math.Abs(session.PrimaryShape!.TextRanges[0].Style.FontSize/session.PrimaryShape.TextStyle.FontSize - 60f/38), 0, .001);
        session.Undo(); Assert.Same(original, session.Document); session.Redo(); Assert.True(session.PrimaryShape.TextStyle.FontSize < shape.TextStyle.FontSize);
    }
    [Fact] public void PresentationResizeScalesAllMixedRunsWithoutFlattening()
    {
        var shape = Text(); var source = Deck(shape); var result = DocumentLayout.Resize(source, 640, 360); var next = result.Slides[0].Shapes[0];
        Assert.Equal(19, next.TextStyle.FontSize); Assert.Equal(30, next.TextRanges[0].Style.FontSize);
        Assert.Equal(new RectF(20, 25, 125, 32.5f), next.Bounds); Assert.Equal(shape.Id, next.Id); Assert.Equal(shape.Text, next.Text);
        var session = new EditorSession(source); session.EditDocument("Resize", d => DocumentLayout.Resize(d, 640, 360));
        Assert.Equal(30, session.CurrentSlide.Shapes[0].TextRanges[0].Style.FontSize); session.Undo(); Assert.Same(source, session.Document);
    }
    [Fact] public void PresentationResizeScalesTableStyleMarginsAndBorders()
    {
        var table = TableModel.Create(2, 2) with { TextStyle = new() { FontSize = 30 } };
        table = table with { Cells = table.Cells.SetItem(0, table.Cells[0] with { Text = "ab", TextStyle = new() { FontSize = 40 }, TextRanges = [new(0, 1, new() { FontSize = 50 })] }) };
        var shape = TableModel.Apply(new SlideShape { Bounds = new(40, 60, 400, 200) }, table);
        var next = DocumentLayout.Resize(Deck(shape), 640, 360).Slides[0].Shapes[0].Table!;
        Assert.Equal(15, next.TextStyle.FontSize); Assert.Equal(20, next.Cells[0].TextStyle!.FontSize); Assert.Equal(25, next.Cells[0].TextRanges[0].Style.FontSize);
        Assert.Equal(table.Cells[0].MarginLeft/2, next.Cells[0].MarginLeft); Assert.Equal(table.Cells[0].Top.Width/2, next.Cells[0].Top.Width);
        Assert.Equal(table.Cells[0].Text, next.Cells[0].Text); Assert.Equal(table.RowHeights, next.RowHeights); Assert.Equal(table.ColumnWidths, next.ColumnWidths);
    }
    [Fact] public void FailedResizeDoesNotProduceAnInvalidPartiallyChangedDocument()
    {
        var original = Deck(Text()); var session = new EditorSession(original);
        Assert.Throws<ArgumentOutOfRangeException>(() => session.EditDocument("Resize", d => DocumentLayout.Resize(d, 1, 1)));
        Assert.Same(original, session.Document); Assert.Same(original, DocumentLayout.Resize(original, original.Width, original.Height));
    }
    [Theory] [InlineData("a\r\nb\rc\nd\u2029e", "a\nb\nc\nd\ne", 5, 0)]
    [InlineData("a\vb\u2028c\nd", "a\vb\vc\nd", 2, 2)]
    [InlineData("\r\n\r\n", "\n\n", 3, 0)]
    public void NativePptxKeepsSoftBreaksSeparateFromParagraphs(string text, string expected, int paragraphs, int breaks)
    {
        var shape = SlideFactory.Text(text, 10, 10, 600, 400, 24) with { TextStyle = new() { FontSize = 24, Bullets = true } };
        byte[] data = PptxCodec.Export(Deck(shape)).Data;
        using var stream = new MemoryStream(data); using (var office = OfficeDocument.Open(stream, false)) Assert.Empty(new OpenXmlValidator().Validate(office));
        using var zip = new ZipArchive(new MemoryStream(data)); using var part = zip.GetEntry("ppt/slides/slide1.xml")!.Open(); var xml = XElement.Load(part);
        XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
        Assert.Equal(paragraphs, xml.Descendants(a+"p").Count()); Assert.Equal(breaks, xml.Descendants(a+"br").Count());
        var imported = PptxCodec.Import(data).Document.Slides[0].Shapes[0]; Assert.Equal(expected, imported.Text);
    }
    [Theory] [InlineData(ShapeKind.Text, 3)] [InlineData(ShapeKind.Rectangle, 12)]
    public void NativeTextInsetsAndBulletIndentMatchRendererConventions(ShapeKind kind, int padding)
    {
        var shape = SlideFactory.Text("bulleted text", 10, 10, 600, 400, 24) with { Kind = kind, TextStyle = new() { FontSize = 24, Bullets = true } };
        using var zip = new ZipArchive(new MemoryStream(PptxCodec.Export(Deck(shape)).Data)); using var part = zip.GetEntry("ppt/slides/slide1.xml")!.Open(); var xml = XElement.Load(part);
        XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
        Assert.Equal(padding * 9525, (int)xml.Descendants(a+"bodyPr").First().Attribute("lIns")!);
        Assert.Equal(30 * 9525, (int)xml.Descendants(a+"pPr").First().Attribute("marL")!);
    }
    [Fact] public void TypographySampleRendersAndExportsAsEditableSlides()
    {
        var sample = TypographySample.Create(); DocumentSerializer.Validate(sample); Assert.Equal(2, sample.Slides.Length);
        using var renderer = new SlideRenderer(); Assert.All(sample.Slides, s => Assert.True(renderer.ExportPng(sample, s, 960).Length > 2000));
        Assert.True(renderer.ExportPdf(sample).Length > 1000);
        var data = PptxCodec.Export(sample).Data; using var stream = new MemoryStream(data); using var office = OfficeDocument.Open(stream, false);
        Assert.Empty(new OpenXmlValidator().Validate(office)); Assert.Equal(2, PptxCodec.Import(data).Document.Slides.Length);
    }
}
