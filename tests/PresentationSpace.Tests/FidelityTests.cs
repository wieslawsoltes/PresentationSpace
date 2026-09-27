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
using OpenXmlSpreadsheet = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument;

namespace PresentationSpace.Tests;

public class FidelityTests
{
    private static readonly XNamespace P = "http://schemas.openxmlformats.org/presentationml/2006/main";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace C = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private static PresentationDocument Deck(params SlideShape[] shapes) => new() { Slides = [new() { Shapes = shapes.ToImmutableArray() }] };
    private static SlideShape Text(string value = "Alpha Beta Gamma") => SlideFactory.Text(value, 50, 50, 800, 300, 32);
    private static SlideShape Chart() => new() { Kind = ShapeKind.Chart, Name = "Forecast", Bounds = new(50, 80, 640, 400), Values = [-12.5f, 0, 73.125f], Labels = ["=not a formula", "Two & <three>", "2026"], Fill = "#247BBF" };
    private static XElement Part(byte[] bytes, string path) { using var stream = new MemoryStream(bytes); using var zip = new ZipArchive(stream); using var part = zip.GetEntry(path)!.Open(); return XElement.Load(part); }
    private static byte[] ChangePart(byte[] bytes, string path, Action<XElement> change)
    {
        using var input = new MemoryStream(bytes); using var read = new ZipArchive(input); using var output = new MemoryStream();
        using (var write = new ZipArchive(output, ZipArchiveMode.Create, true)) foreach (var entry in read.Entries)
        {
            using var source = entry.Open(); using var target = write.CreateEntry(entry.FullName).Open();
            if (entry.FullName == path) { var xml = XElement.Load(source); change(xml); xml.Save(target); } else source.CopyTo(target);
        }
        return output.ToArray();
    }

    [Fact] public void LayoutChangePreservesContentAndObjectIdentity()
    {
        var slide = SlideFactory.Create("Title slide");
        var title = slide.Shapes[0] with { Text = "Keep my title", TextStyle = new() { Bold = true, FontSize = 66 } };
        var subtitle = slide.Shapes[1] with { Text = "Keep my subtitle" };
        var custom = Text("Custom annotation");
        slide = slide with { Name = "My slide", Notes = "speaker notes", Shapes = [title, subtitle, custom], Comments = [new(Guid.NewGuid(), "Reviewer", "Keep this", DateTimeOffset.UtcNow)] };
        var result = SlideLayoutEngine.Apply(slide, "Two content", 1280, 720);
        Assert.Equal(slide.Name, result.Name); Assert.Equal(slide.Notes, result.Notes); Assert.Equal(slide.Comments.ToArray(), result.Comments.ToArray());
        Assert.Equal(title.Text, result.Shapes.Single(s => s.Id == title.Id).Text);
        Assert.Equal(title.TextStyle, result.Shapes.Single(s => s.Id == title.Id).TextStyle);
        Assert.Equal(PlaceholderKind.Body, result.Shapes.Single(s => s.Id == subtitle.Id).Placeholder);
        Assert.Contains(custom, result.Shapes); Assert.Equal(4, result.Shapes.Length);
    }
    [Fact] public void BlankAndReturnRetainsEveryContentObject()
    {
        var source = SlideFactory.Create("Two content");
        var blank = SlideLayoutEngine.Apply(source, "Blank", 1280, 720);
        Assert.Equal(source.Shapes.ToArray(), blank.Shapes.ToArray());
        var restored = SlideLayoutEngine.Apply(blank, "Two content", 1280, 720);
        Assert.Equal(source.Shapes.ToArray(), restored.Shapes.ToArray());
    }
    [Fact] public void FewerPlaceholdersDoesNotDeleteOverflowText()
    {
        var source = SlideFactory.Create("Two content"); var result = SlideLayoutEngine.Apply(source, "Title only", 1280, 720);
        Assert.Equal(source.Shapes.Select(s => s.Id), result.Shapes.Select(s => s.Id));
        Assert.Equal(source.Shapes.Select(s => s.Text), result.Shapes.Select(s => s.Text));
    }
    [Fact] public void CustomTextNeverBecomesAGuessedPlaceholder()
    {
        var text = Text("Click to add title"); var source = new Slide { Shapes = [text] };
        var result = SlideLayoutEngine.Apply(source, "Title slide", 1280, 720);
        Assert.Contains(text, result.Shapes); Assert.Equal(PlaceholderKind.None, result.Shapes.Single(s => s.Id == text.Id).Placeholder);
    }
    [Fact] public void LegacyFactorySlideMigratesWithoutDeletingText()
    {
        var source = SlideFactory.Create("Title slide"); source = source with { LayoutName = null, Shapes = source.Shapes.Select(s => s with { Placeholder = PlaceholderKind.None, Text = "Legacy content" }).ToImmutableArray() };
        var result = SlideLayoutEngine.Apply(source, "Title and content", 1280, 720);
        Assert.Equal(2, result.Shapes.Length); Assert.All(result.Shapes, s => Assert.Equal("Legacy content", s.Text));
    }
    [Fact] public void LayoutChangeIsOneUndoableCommand()
    {
        var source = new PresentationDocument { Slides = [SlideFactory.Create("Title slide")] }; var session = new EditorSession(source);
        session.ApplyLayout("Two content"); Assert.Equal("Two content", session.CurrentSlide.LayoutName);
        session.Undo(); Assert.Same(source, session.Document); session.Redo(); Assert.Equal("Two content", session.CurrentSlide.LayoutName);
    }
    [Fact] public void LayoutNamesAndRolesRoundTripThroughNativePptx()
    {
        var source = new PresentationDocument { Slides = SlideLayoutEngine.Layouts.Select(n => SlideFactory.Create(n)).ToImmutableArray() };
        var bytes = PptxCodec.Export(source).Data; var result = PptxCodec.Import(bytes).Document;
        Assert.Equal(source.Slides.Select(s => s.LayoutName), result.Slides.Select(s => s.LayoutName));
        for (int i = 0; i < source.Slides.Length; i++) Assert.Equal(source.Slides[i].Shapes.Select(s => (s.Placeholder, s.PlaceholderIndex)), result.Slides[i].Shapes.Select(s => (s.Placeholder, s.PlaceholderIndex)));
    }
    [Fact] public void LayoutGeometryIsInheritedWhenSlideTransformIsAbsent()
    {
        var deck = new PresentationDocument { Slides = [SlideFactory.Create("Title slide")] };
        var bytes = ChangePart(PptxCodec.Export(deck).Data, "ppt/slides/slide1.xml", root => root.Descendants(P + "spPr").Elements(A + "xfrm").Remove());
        var result = PptxCodec.Import(bytes).Document.Slides[0];
        Assert.InRange(Math.Abs(result.Shapes[0].Bounds.X - deck.Slides[0].Shapes[0].Bounds.X), 0, .01);
        Assert.InRange(Math.Abs(result.Shapes[1].Bounds.Height - deck.Slides[0].Shapes[1].Bounds.Height), 0, .01);
    }

    [Fact] public void PartialFormattingSplitsRangesWithoutChangingText()
    {
        var source = Text(); var result = RichText.Format(source, 6, 4, style => style with { Bold = true, Color = "#F00000" });
        Assert.Equal(source.Text, result.Text); Assert.False(RichText.StyleAt(result, 0).Bold); Assert.True(RichText.StyleAt(result, 7).Bold); Assert.False(RichText.StyleAt(result, 12).Bold);
        Assert.Single(result.TextRanges);
    }
    [Fact] public void OverlappingFormattingPreservesOtherProperties()
    {
        var result = RichText.Format(Text(), 0, 10, style => style with { Bold = true });
        result = RichText.Format(result, 6, 10, style => style with { Italic = true });
        Assert.True(RichText.StyleAt(result, 7).Bold); Assert.True(RichText.StyleAt(result, 7).Italic);
        Assert.False(RichText.StyleAt(result, 12).Bold); Assert.True(RichText.StyleAt(result, 12).Italic);
        DocumentSerializer.Validate(Deck(result));
    }
    [Fact] public void TextReplacementShiftsFollowingRanges()
    {
        var source = RichText.Format(Text(), 6, 4, style => style with { Bold = true }); var result = RichText.Replace(source, 0, 5, "A");
        Assert.Equal("A Beta Gamma", result.Text); Assert.True(RichText.StyleAt(result, 2).Bold); Assert.False(RichText.StyleAt(result, 7).Bold);
    }
    [Fact] public void PlainTextEditorChangesReconcileFormatting()
    {
        var shape = RichText.Format(Text(), 6, 4, style => style with { Bold = true }); var session = new EditorSession(Deck(shape)); session.Select(shape.Id);
        session.Apply("Type", s => s with { Text = "A Beta Gamma" }); Assert.True(RichText.StyleAt(session.PrimaryShape!, 2).Bold);
        session.Undo(); Assert.Equal(shape, session.PrimaryShape);
    }
    [Fact] public void BaseStyleChangeRetainsUnchangedMixedProperties()
    {
        var shape = RichText.Format(Text(), 6, 4, style => style with { Bold = true }); var session = new EditorSession(Deck(shape)); session.Select(shape.Id);
        session.Apply("Color", s => s with { TextStyle = s.TextStyle with { Color = "#00BB00" } });
        Assert.True(RichText.StyleAt(session.PrimaryShape!, 7).Bold); Assert.Equal("#00BB00", RichText.StyleAt(session.PrimaryShape!, 7).Color);
    }
    [Fact] public void Utf16RangesNeverSplitSurrogatePairs()
    {
        var shape = Text("A😀B"); Assert.Throws<ArgumentOutOfRangeException>(() => RichText.Format(shape, 1, 1, s => s));
        var result = RichText.Format(shape, 1, 2, s => s with { Bold = true }); result = RichText.Replace(result, 0, 1, "XX");
        Assert.Equal("XX😀B", result.Text); Assert.True(RichText.StyleAt(result, 2).Bold); DocumentSerializer.Validate(Deck(result));
    }
    [Fact] public void MalformedRichTextRangesAreRejected()
    {
        var shape = Text() with { TextRanges = [new(6, 8, new()), new(7, 2, new())] };
        Assert.Throws<InvalidDataException>(() => DocumentSerializer.Validate(Deck(shape)));
    }
    [Fact] public void RichTextNativeAndPptxRoundTripsKeepCharacterStyles()
    {
        var shape = RichText.Format(Text("Alpha\nBeta Gamma"), 6, 4, s => s with { Bold = true, Italic = true, Underline = true, FontSize = 51, Color = "#FF0000", FontFamily = "Georgia" });
        var native = DocumentSerializer.Deserialize(DocumentSerializer.Serialize(Deck(shape))); Assert.Equal(shape.TextRanges.ToArray(), native.Slides[0].Shapes[0].TextRanges.ToArray());
        var imported = PptxCodec.Import(PptxCodec.Export(Deck(shape)).Data).Document.Slides[0].Shapes[0];
        var style = RichText.StyleAt(imported, 7); Assert.Equal(shape.Text, imported.Text); Assert.True(style.Bold && style.Italic && style.Underline); Assert.Equal(51, style.FontSize); Assert.Equal("Georgia", style.FontFamily); Assert.Equal("#FF0000", style.Color);
        Assert.False(RichText.StyleAt(imported, 0).Bold);
    }
    [Fact] public void RichTextRendersDifferentRunColors()
    {
        var shape = Text("Alpha Beta Gamma") with { TextStyle = new() { FontSize = 48, Color = "#0000FF" } };
        shape = RichText.Format(shape, 6, 4, s => s with { Color = "#FF0000", Bold = true });
        using var renderer = new SlideRenderer(); using var bitmap = SKBitmap.Decode(renderer.ExportPng(Deck(shape), Deck(shape).Slides[0], 1280));
        Assert.Contains(bitmap.Pixels, c => c.Red > 150 && c.Blue < 80 && c.Green < 80); Assert.Contains(bitmap.Pixels, c => c.Blue > 150 && c.Red < 80 && c.Green < 80);
    }

    [Fact] public void TablesExportAsNativeGraphicFrames()
    {
        var shape = new SlideShape { Kind = ShapeKind.Table, TableColumns = 2, Cells = ["A", "B", "one", "two"] };
        var bytes = PptxCodec.Export(Deck(shape)).Data; var slide = Part(bytes, "ppt/slides/slide1.xml");
        Assert.Single(slide.Descendants(A + "tbl")); Assert.Single(slide.Descendants(P + "graphicFrame")); Assert.Empty(slide.Descendants(P + "sp"));
        var imported = PptxCodec.Import(bytes).Document.Slides[0].Shapes.Single(); Assert.Equal(ShapeKind.Table, imported.Kind); Assert.Equal(shape.Cells.ToArray(), imported.Cells.ToArray());
    }
    [Fact] public void NativeChartsRetainNumericAndLabelData()
    {
        var shape = Chart(); var result = PptxCodec.Export(Deck(shape));
        Assert.DoesNotContain(result.Warnings, warning => warning.Contains("flatten", StringComparison.OrdinalIgnoreCase));
        var imported = PptxCodec.Import(result.Data).Document.Slides[0].Shapes.Single();
        Assert.Equal(ShapeKind.Chart, imported.Kind); Assert.Equal(shape.Values.ToArray(), imported.Values.ToArray()); Assert.Equal(shape.Labels.ToArray(), imported.Labels.ToArray()); Assert.Equal(shape.Fill.ToUpperInvariant(), imported.Fill);
    }
    [Fact] public void NativeChartAndEmbeddedWorkbookPassIndependentSchemaValidation()
    {
        var bytes = PptxCodec.Export(Deck(Chart())).Data; using var stream = new MemoryStream(bytes); using var presentation = OpenXmlPresentation.Open(stream, false);
        var validator = new OpenXmlValidator(); var errors = validator.Validate(presentation).Select(e => $"{e.Part?.Uri}: {e.Path?.XPath}: {e.Description}").ToArray(); Assert.True(errors.Length == 0, string.Join('\n', errors));
        var chart = presentation.PresentationPart!.SlideParts.Single().ChartParts.Single(); using var packageStream = chart.EmbeddedPackagePart!.GetStream();
        using var workbookStream = new MemoryStream(); packageStream.CopyTo(workbookStream); workbookStream.Position = 0;
        using var workbook = OpenXmlSpreadsheet.Open(workbookStream, false); var workbookErrors = validator.Validate(workbook).Select(e => e.Description).ToArray(); Assert.True(workbookErrors.Length == 0, string.Join('\n', workbookErrors));
        Assert.DoesNotContain(workbook.WorkbookPart!.WorksheetParts.Single().Worksheet.Descendants<DocumentFormat.OpenXml.Spreadsheet.CellFormula>(), _ => true);
    }
    [Fact] public void NativeTableAndMixedTextPassIndependentSchemaValidation()
    {
        var text = RichText.Format(Text(), 6, 4, s => s with { Bold = true });
        var table = new SlideShape { Kind = ShapeKind.Table, Cells = ["A", "B", "C", "1", "2", "3"] };
        using var stream = new MemoryStream(PptxCodec.Export(Deck(table, text)).Data); using var document = OpenXmlPresentation.Open(stream, false);
        var errors = new OpenXmlValidator().Validate(document).Select(e => $"{e.Part?.Uri}: {e.Path?.XPath}: {e.Description}").ToArray(); Assert.True(errors.Length == 0, string.Join('\n', errors));
    }
    [Fact] public void OversizedChartCachesAreRejectedBeforeAllocation()
    {
        var bytes = ChangePart(PptxCodec.Export(Deck(Chart())).Data, "ppt/charts/chart1_2.xml", root => root.Descendants(C + "numCache").Elements(C + "ptCount").Single().SetAttributeValue("val", 1000000000));
        Assert.Throws<InvalidDataException>(() => PptxCodec.Import(bytes));
    }
    [Fact] public void DuplicateChartPointIndexesAreRejected()
    {
        var bytes = ChangePart(PptxCodec.Export(Deck(Chart())).Data, "ppt/charts/chart1_2.xml", root => root.Descendants(C + "numCache").Elements(C + "pt").Last().SetAttributeValue("idx", 0));
        Assert.Throws<InvalidDataException>(() => PptxCodec.Import(bytes));
    }
    [Fact] public void UnsupportedMultiSeriesChartIsNotSilentlyReducedToOneSeries()
    {
        var bytes = ChangePart(PptxCodec.Export(Deck(Chart())).Data, "ppt/charts/chart1_2.xml", root => { var bars = root.Descendants(C + "barChart").Single(); bars.Add(new XElement(bars.Element(C + "ser")!)); });
        var result = PptxCodec.Import(bytes); Assert.Empty(result.Document.Slides[0].Shapes); Assert.Contains(result.Warnings, w => w.Contains("unsupported chart"));
    }
    [Fact] public void AlternativeTextRoundTrips()
    {
        var shape = Text() with { AlternativeText = "An accessible object description" }; var result = PptxCodec.Import(PptxCodec.Export(Deck(shape)).Data);
        Assert.Equal(shape.AlternativeText, result.Document.Slides[0].Shapes[0].AlternativeText);
    }

    public static IEnumerable<object[]> ResizeCases() => from angle in new[] { 0f, 37f, 90f, 217f } from handle in Enumerable.Range(0, 8) from aspect in new[] { false, true } select new object[] { angle, handle, aspect };
    [Theory, MemberData(nameof(ResizeCases))] public void RotatedResizeKeepsOppositeHandleFixed(float angle, int handle, bool aspect)
    {
        var before = new RectF(100, 150, 300, 120); var after = Geometry.ResizeRotated(before, angle, handle, new(24, 35), aspect);
        int opposite = (handle + 4) % 8; var fixedBefore = Geometry.Rotate(Geometry.Handles(before)[opposite], before.Center, angle); var fixedAfter = Geometry.Rotate(Geometry.Handles(after)[opposite], after.Center, angle);
        Assert.InRange(Math.Abs(fixedBefore.X - fixedAfter.X), 0, .001); Assert.InRange(Math.Abs(fixedBefore.Y - fixedAfter.Y), 0, .001);
        if (aspect) Assert.InRange(Math.Abs(before.Width / before.Height - after.Width / after.Height), 0, .001);
    }
    [Theory, InlineData(1), InlineData(5)] public void AspectResizeWithVerticalSideHandleChangesSize(int handle)
    {
        var before = new RectF(100, 100, 300, 150); var after = Geometry.ResizeRotated(before, 30, handle, new(0, 35), true); Assert.NotEqual(before.Height, after.Height);
    }
    [Fact] public void TriangleHitTestRejectsItsEmptyCorners()
    {
        var triangle = new SlideShape { Kind = ShapeKind.Triangle, Bounds = new(0, 0, 100, 100) };
        Assert.False(Geometry.HitTest(triangle, new(5, 5))); Assert.True(Geometry.HitTest(triangle, new(50, 30)));
    }
    [Fact] public void MarqueeSelectionUsesRotatedVisualExtent()
    {
        var shape = new SlideShape { Bounds = new(100, 100, 200, 20), Rotation = 90 }; var session = new EditorSession(Deck(shape));
        session.SelectRect(new(185, 15, 30, 20)); Assert.Contains(shape.Id, session.Selection);
    }
}
