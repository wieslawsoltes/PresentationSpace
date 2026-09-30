using System.Collections.Immutable;
using System.IO.Compression;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Validation;
using PresentationSpace.Core;
using PresentationSpace.Formats;
using PresentationSpace.Rendering.Skia;
using SkiaSharp;
using Xunit;
using OpenXmlPresentation = DocumentFormat.OpenXml.Packaging.PresentationDocument;

namespace PresentationSpace.Tests;

public sealed class TextBodyLayoutTests
{
    private static SlideShape Shape(string text = "One paragraph with several words to wrap into multiple lines.") => SlideFactory.Text(text, 20, 20, 220, 140, 24);
    private static PresentationDocument Deck(SlideShape shape) => new() { Slides = [new() { Shapes = [shape] }] };
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace P = "http://schemas.openxmlformats.org/presentationml/2006/main";
    private static byte[] Rewrite(byte[] data, Action<ZipArchive> action)
    {
        using var memory = new MemoryStream(); memory.Write(data);
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Update, true)) action(zip);
        return memory.ToArray();
    }
    private static void Xml(ZipArchive zip, string path, Action<XDocument> change)
    {
        var entry = zip.GetEntry(path)!; XDocument doc;
        using (var read = entry.Open()) doc = XDocument.Load(read);
        change(doc); entry.Delete(); using var write = zip.CreateEntry(path).Open(); doc.Save(write);
    }
    [Fact] public void LegacyDefaultsAndEmptyInsetBoundsAreExplicit()
    {
        Assert.Equal(3, TextBoxModel.Resolve(Shape()).MarginLeft);
        Assert.Equal(12, TextBoxModel.Resolve(new SlideShape()).MarginTop);
        var shape = Shape() with { TextBox = TextBoxSpec.Uniform(300) };
        var rect = TextBoxModel.ContentBounds(shape); Assert.Equal(0, rect.Width); Assert.Equal(0, rect.Height);
        using var renderer = new SlideRenderer(); Assert.False(renderer.FitTextToShape(shape).Fits);
        Assert.Throws<InvalidOperationException>(() => renderer.FitShapeToText(shape));
    }
    [Theory] [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(-1)] [InlineData(10001)]
    public void InvalidBodyMarginsAreRejected(float value)
    {
        var box = new TextBoxSpec { MarginLeft = value };
        Assert.Throws<InvalidDataException>(() => TextBoxModel.Validate(box));
        Assert.Throws<InvalidDataException>(() => DocumentSerializer.Validate(Deck(Shape() with { TextBox = box })));
    }
    [Theory] [InlineData(float.NaN)] [InlineData(float.NegativeInfinity)] [InlineData(-1)] [InlineData(10001)]
    public void InvalidParagraphDimensionsAreRejected(float value)
    {
        foreach (var style in new[] { new TextStyle { SpaceBefore = value }, new TextStyle { SpaceAfter = value },
            new TextStyle { ParagraphLeftMargin = value }, new TextStyle { ParagraphRightMargin = value }, new TextStyle { DefaultTabSize = value } })
            Assert.Throws<InvalidDataException>(() => TextFlow.ValidateStyle(style));
    }
    [Theory] [InlineData(float.NaN)] [InlineData(0)] [InlineData(-1)] [InlineData(10001)]
    public void InvalidAbsoluteLineSpacingFails(float value) => Assert.Throws<InvalidDataException>(() => TextFlow.ValidateStyle(new() { LineSpacingPoints = value }));
    [Fact] public void SchemaFiveGatesBodyAndParagraphPropertiesWithoutUpgradingLegacyText()
    {
        Assert.Equal(1, DocumentSerializer.Deserialize(DocumentSerializer.Serialize(Deck(Shape()))).SchemaVersion);
        var body = Shape() with { TextBox = new() { MarginLeft = 19, Wrap = false } };
        var copy = DocumentSerializer.Deserialize(DocumentSerializer.Serialize(Deck(body)));
        Assert.Equal(5, copy.SchemaVersion); Assert.Equal(body.TextBox, copy.Slides[0].Shapes[0].TextBox);
        var text = RichText.Format(Shape(), 0, 3, s => s with { SpaceAfter = 12 });
        Assert.Equal(5, DocumentSerializer.Deserialize(DocumentSerializer.Serialize(Deck(text))).SchemaVersion);
        var table = TableModel.Create(2, 2) with { TextStyle = new() { DefaultTabSize = 72 } };
        Assert.Equal(5, DocumentSerializer.Deserialize(DocumentSerializer.Serialize(Deck(TableModel.Apply(new(), table)))).SchemaVersion);
        Assert.Throws<InvalidDataException>(() => DocumentSerializer.Validate(Deck(body) with { SchemaVersion = int.MaxValue }));
    }
    [Fact] public void NoWrapStillHonorsHardAndSoftBreaksAndHasSeparateCacheEntry()
    {
        using var engine = new TextLayoutEngine(); var style = new TextStyle { FontSize = 25 };
        var wrapped = engine.Measure("abc def ghi", style, 75);
        var unwrapped = engine.Measure("abc def ghi", style, 75, [], false);
        Assert.True(wrapped.Lines.Length > 1); Assert.Single(unwrapped.Lines); Assert.True(unwrapped.Width > 75);
        Assert.Equal(3, engine.Measure("abc\vdef\nghi", style, 30, [], false).Lines.Length);
        Assert.NotSame(wrapped, unwrapped); Assert.Same(unwrapped, engine.Measure("abc def ghi", style, 75, [], false));
    }
    [Fact] public void ParagraphSpacingAppearsOnlyAtParagraphBoundaries()
    {
        var style = new TextStyle { FontSize = 20, SpaceBefore = 9, SpaceAfter = 13 };
        using var engine = new TextLayoutEngine(); var layout = engine.Measure("one\vtwo\nthree", style, 400);
        Assert.Equal(3, layout.Lines.Length); var lines = layout.Lines;
        Assert.Equal(9, lines[0].Top); Assert.Equal(lines[0].Top + lines[0].Height, lines[1].Top);
        Assert.Equal(lines[1].Top + lines[1].Height + 22, lines[2].Top);
        Assert.Equal(lines[2].Top + lines[2].Height + 13, layout.Height);
        Assert.False(lines[0].ParagraphEnd); Assert.True(lines[1].ParagraphEnd); Assert.True(lines[2].ParagraphEnd);
    }
    [Fact] public void AbsoluteSpacingRetainsFinalGlyphDescent()
    {
        using var engine = new TextLayoutEngine(); var style = new TextStyle { FontSize = 48, LineSpacingPoints = 12 };
        var layout = engine.Measure("gyp\ngyp", style, 400);
        Assert.Equal(12, layout.Lines[1].Top); Assert.Equal(12, layout.Lines[0].Height);
        Assert.True(layout.Height > layout.Lines[1].Baseline); Assert.True(layout.Height > 24);
    }
    [Fact] public void OrdinaryFirstLineIndentDoesNotAffectContinuationLines()
    {
        using var engine = new TextLayoutEngine();
        var style = new TextStyle { FontSize = 22, ParagraphLeftMargin = 32, ParagraphRightMargin = 11, ParagraphIndent = 19 };
        var layout = engine.Measure(Shape().Text, style, 180);
        Assert.True(layout.Lines.Length > 1); Assert.Equal(51, layout.Lines[0].Indent);
        Assert.All(layout.Lines.Skip(1), l => Assert.Equal(32, l.Indent));
        Assert.All(layout.Lines, l => Assert.True(l.Width <= 180.01));
    }
    [Fact] public void HangingBulletUsesItsTextMarginOnEveryLine()
    {
        using var engine = new TextLayoutEngine();
        var style = new TextStyle { Bullets = true, ParagraphLeftMargin = 45, ParagraphIndent = -22, FontSize = 20 };
        var layout = engine.Measure(Shape().Text + "\vcontinued", style, 180);
        Assert.All(layout.Lines, l => Assert.Equal(45, l.Indent)); Assert.Single(layout.Lines.Where(l => l.ParagraphStart));
    }
    [Fact] public void JustificationFillsAutomaticLinesButNotTheLastOrForcedLine()
    {
        using var engine = new TextLayoutEngine();
        var style = new TextStyle { FontSize = 20, Alignment = ParagraphAlignment.Justify };
        var layout = engine.Measure("one two three four five six seven eight nine ten", style, 185);
        Assert.True(layout.Lines.Length > 1); Assert.True(layout.Lines[0].Justified);
        Assert.Equal(185, layout.Lines[0].Width, 3); Assert.False(layout.Lines[^1].Justified);
        Assert.False(engine.Measure("one two\vthree", style, 185).Lines[0].Justified);
        Assert.DoesNotContain(engine.Measure("one\ttwo three four five", style, 200).Lines, l => l.Start == 0 && l.Justified);
    }
    [Theory] [InlineData(ParagraphAlignment.Right)] [InlineData(ParagraphAlignment.Center)]
    public void OverflowDoesNotShiftTheBeginningLeftOutOfView(ParagraphAlignment alignment)
    {
        using var engine = new TextLayoutEngine(); var style = new TextStyle { Alignment = alignment, FontSize = 100 };
        Assert.Equal(0, engine.Measure("W\u00a0W", style, 20).Lines[0].Left);
    }
    [Theory] [InlineData("\u2007")] [InlineData("\u2011")] [InlineData("\u202f")] [InlineData("\u00a0")]
    public void ExplicitNonBreakingGroupsRemainUnbroken(string join)
    {
        using var engine = new TextLayoutEngine(); string text = "abc" + join + "def";
        Assert.False(TextFlow.AllowsEmergencyBreak(text)); Assert.Single(engine.Measure(text, new(), 8).Lines);
    }
    [Fact] public void OversizedLeadingWhitespaceDoesNotAddAnEmptyLine()
    {
        using var engine = new TextLayoutEngine(); var layout = engine.Measure(new string(' ', 100) + "word", new(), 150);
        Assert.Single(layout.Lines); Assert.Equal(104, layout.Lines[0].Length);
    }
    [Fact] public void ExplicitTabIntervalUsesParagraphCoordinates()
    {
        using var engine = new TextLayoutEngine(); var style = new TextStyle { FontSize = 20, DefaultTabSize = 90 };
        var width = engine.Measure("b", style, 500).Width;
        Assert.Equal(90 + width, engine.Measure("a\tb", style, 500).Width, 3);
    }
    [Fact] public void BodyInsetsMeasureFitAndClipTheSameContentArea()
    {
        var shape = Shape(new string('W', 20)) with { Bounds = new(20, 20, 200, 120), TextBox = new() { MarginLeft = 27, MarginRight = 13, MarginTop = 19, MarginBottom = 17, Wrap = false } };
        using var renderer = new SlideRenderer();
        Assert.Single(renderer.LayoutRichText(shape, shape.Bounds.Width).Lines);
        var fit = renderer.FitTextToShape(shape, 1); Assert.True(fit.Fits); Assert.Same(shape.TextBox, fit.Shape.TextBox);
        Assert.True(renderer.MeasureRichTextHeight(fit.Shape, fit.Shape.Bounds.Width) <= 120.01);
        using var bmp = new SKBitmap(260, 180); using var canvas = new SKCanvas(bmp); canvas.Clear(SKColors.White);
        var content = TextBoxModel.ContentBounds(shape); renderer.DrawRichText(canvas, shape);
        Assert.Contains(bmp.Pixels, p => p != SKColors.White);
        for (int y = 0; y < bmp.Height; y++) for (int x = 0; x < bmp.Width; x++)
            if (x < content.X || x >= content.Right || y < content.Y || y >= content.Bottom) Assert.Equal(SKColors.White, bmp.GetPixel(x, y));
    }
    [Fact] public void ChangingOnlyInsetsInvalidatesTheRetainedPicture()
    {
        using var renderer = new SlideRenderer(); using var surface = SKSurface.Create(new SKImageInfo(1280, 720));
        var shape = Shape(); var document = Deck(shape); renderer.Render(surface.Canvas, document, document.Slides[0]); long misses = renderer.CacheStatistics.Misses;
        var changed = shape with { TextBox = TextBoxSpec.Uniform(24) }; document = Deck(changed);
        renderer.Render(surface.Canvas, document, document.Slides[0]); Assert.True(renderer.CacheStatistics.Misses > misses);
    }
    [Fact] public void FormattingOnePropertyPreservesUnrelatedParagraphAndCharacterStyles()
    {
        var shape = Shape() with { TextStyle = new() { SpaceBefore = 8 } };
        shape = RichText.Format(shape, 0, 3, s => s with { Bold = true, SpaceAfter = 16, ParagraphLeftMargin = 25 });
        var next = RichText.Reconcile(shape, shape with { TextStyle = shape.TextStyle with { SpaceBefore = 20 } });
        var style = RichText.StyleAt(next, 0); Assert.True(style.Bold); Assert.Equal(16, style.SpaceAfter); Assert.Equal(25, style.ParagraphLeftMargin); Assert.Equal(20, style.SpaceBefore);
    }
    [Fact] public void ExplicitWholeObjectLayoutResetsAllParagraphsButNotCharacterFormatting()
    {
        var shape = Shape("first\nsecond");
        shape = RichText.Format(shape, 6, 6, s => s with { SpaceAfter = 33, Alignment = ParagraphAlignment.Right, Bold = true, Color = "#224488" });
        var next = TextBoxModel.ApplyLayout(shape, TextBoxSpec.Uniform(3), shape.TextStyle);
        Assert.Equal(0, RichText.StyleAt(next, 6).SpaceAfter);
        Assert.Equal(ParagraphAlignment.Left, RichText.StyleAt(next, 6).Alignment);
        Assert.True(RichText.StyleAt(next, 6).Bold); Assert.Equal("#224488", RichText.StyleAt(next, 6).Color);
        Assert.Equal(shape.Text, next.Text); Assert.Equal(shape.Bounds, next.Bounds);
    }
    [Fact] public void ResizePreservesAndScalesTextBodyAndParagraphDimensions()
    {
        var shape = Shape() with { TextBox = new() { MarginLeft = 12, MarginTop = 20, Wrap = false }, TextStyle = new() { FontSize = 20, SpaceBefore = 9, ParagraphIndent = -10, LineSpacingPoints = 30 } };
        var session = new EditorSession(Deck(shape)); session.Select(shape.Id);
        session.EditDocument("Resize", d => DocumentLayout.Resize(d, 2560, 1440)); var resized = session.PrimaryShape!;
        Assert.Equal(24, resized.TextBox!.MarginLeft); Assert.Equal(40, resized.TextBox.MarginTop); Assert.False(resized.TextBox.Wrap);
        Assert.Equal(18, resized.TextStyle.SpaceBefore); Assert.Equal(-20, resized.TextStyle.ParagraphIndent); Assert.Equal(60, resized.TextStyle.LineSpacingPoints);
        session.Undo(); Assert.Same(shape, session.PrimaryShape); session.Redo(); Assert.Equal(24, session.PrimaryShape!.TextBox!.MarginLeft);
    }
    [Fact] public void NativeBodyAndParagraphPropertiesValidateAndRoundTrip()
    {
        var shape = Shape("Alpha beta\vsoft\nNext paragraph") with { TextBox = new() { MarginLeft = 17, MarginRight = 21, MarginTop = 9, MarginBottom = 13, Wrap = false },
            TextStyle = new() { FontSize = 24, SpaceBefore = 8, SpaceAfter = 12, ParagraphLeftMargin = 27, ParagraphRightMargin = 14, ParagraphIndent = -9, DefaultTabSize = 80, LineSpacingPoints = 34, Alignment = ParagraphAlignment.Justify } };
        var data = PptxCodec.Export(Deck(shape)).Data;
        using (var stream = new MemoryStream(data)) using (var doc = OpenXmlPresentation.Open(stream, false))
            Assert.Empty(new OpenXmlValidator().Validate(doc).Select(e => e.Description));
        var result = PptxCodec.Import(data).Document.Slides[0].Shapes[0];
        Assert.Equal(shape.Text, result.Text); Assert.Equal(shape.TextBox, result.TextBox);
        var style = RichText.StyleAt(result, 0); Assert.Equal(34, style.LineSpacingPoints); Assert.Equal(8, style.SpaceBefore); Assert.Equal(12, style.SpaceAfter);
        Assert.Equal(27, style.ParagraphLeftMargin); Assert.Equal(14, style.ParagraphRightMargin); Assert.Equal(-9, style.ParagraphIndent); Assert.Equal(80, style.DefaultTabSize);
        Assert.Equal(ParagraphAlignment.Justify, style.Alignment);
    }
    [Theory] [InlineData("lIns", "-1")] [InlineData("rIns", "NaN")] [InlineData("tIns", "999999999999")] [InlineData("wrap", "invalid")]
    public void MalformedNativeTextBodyIsRejected(string attribute, string value)
    {
        var data = Rewrite(PptxCodec.Export(Deck(Shape())).Data, z => Xml(z, "ppt/slides/slide1.xml", d => d.Descendants(A + "bodyPr").First().SetAttributeValue(attribute, value)));
        Assert.Throws<InvalidDataException>(() => PptxCodec.Import(data));
    }
    [Fact] public void EmptyParagraphKeepsItsSpacingOnTheDelimiter()
    {
        var shape = Shape("first\n\nthird"); shape = RichText.Format(shape, 6, 1, s => s with { SpaceAfter = 33, SpaceBefore = 11 });
        var copy = PptxCodec.Import(PptxCodec.Export(Deck(shape)).Data).Document.Slides[0].Shapes[0];
        Assert.Equal(33, RichText.StyleAt(copy, 6).SpaceAfter); Assert.Equal(11, RichText.StyleAt(copy, 6).SpaceBefore);
    }
    [Fact] public void NativeTableParagraphSpacingUsesSharedParserAndRenderer()
    {
        var table = TableModel.Create(1, 1) with { HeaderRow = false, TextStyle = new() { SpaceAfter = 17, LineSpacingPoints = 31, ParagraphLeftMargin = 16 } };
        table = TableModel.SetText(table, 0, 0, "wrapped table text");
        var shape = TableModel.Apply(new(), table); var copy = PptxCodec.Import(PptxCodec.Export(Deck(shape)).Data).Document.Slides[0].Shapes[0];
        var style = TableModel.Style(copy.Table!, copy.Table!.Cells[0]); Assert.Equal(17, style.SpaceAfter); Assert.Equal(31, style.LineSpacingPoints); Assert.Equal(16, style.ParagraphLeftMargin);
    }
    [Fact] public void LayoutSampleExportsThroughAllSharedPaths()
    {
        var doc = ParagraphLayoutSample.Create(); DocumentSerializer.Validate(doc);
        using var renderer = new SlideRenderer(); var png = renderer.ExportPng(doc, doc.Slides[0], 1280); var pdf = renderer.ExportPdf(doc);
        var pptx = PptxCodec.Export(doc).Data;
        using (var stream = new MemoryStream(pptx)) using (var package = OpenXmlPresentation.Open(stream, false))
            Assert.Empty(new OpenXmlValidator().Validate(package).Select(e => e.Description));
        if (Environment.GetEnvironmentVariable("RENDER_DIAGNOSTICS") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory); File.WriteAllBytes(Path.Combine(directory, "paragraph-layout.png"), png);
            File.WriteAllBytes(Path.Combine(directory, "paragraph-layout.pdf"), pdf); File.WriteAllBytes(Path.Combine(directory, "paragraph-layout.pptx"), pptx);
        }
    }
}
