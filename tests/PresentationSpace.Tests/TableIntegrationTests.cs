using System.Collections.Immutable;
using System.IO.Compression;
using System.Xml.Linq;
using PresentationSpace.Core;
using PresentationSpace.Formats;
using PresentationSpace.Rendering.Skia;
using SkiaSharp;
using Xunit;

namespace PresentationSpace.Tests;

public sealed class TableIntegrationTests
{
    private static SlideShape Shape(TableSpec table) => TableModel.Apply(new() { Kind = ShapeKind.Table, Bounds = new(0, 0, 600, 300) }, table);
    private static PresentationDocument Deck(SlideShape shape) => new() { Slides = [new() { Shapes = [shape] }] };

    [Fact] public void ShapeFormatChangesOnlyTheRequestedCellStyleProperties()
    {
        var table = TableModel.Create(2, 2) with { TextStyle = new() { FontSize = 15 }, Cells = TableModel.Create(2, 2).Cells.Select((c, i) => i == 0 ? c with
        { Text = "Red blue", TextStyle = new() { FontSize = 10, Color = "#FF0000" }, TextRanges = [new(4, 4, new() { FontSize = 8, Italic = true, Color = "#0000FF" })] } : c).ToImmutableArray() };
        var shape = Shape(table); var session = new EditorSession(Deck(shape)); session.Select(shape.Id);
        session.Apply("Bold", s => s with { TextStyle = s.TextStyle with { Bold = true } });
        var result = session.PrimaryShape!.Table!; var cell = result.Cells[0];
        Assert.Equal(15, result.TextStyle.FontSize); Assert.True(result.TextStyle.Bold);
        Assert.Equal(10, cell.TextStyle!.FontSize); Assert.Equal("#FF0000", cell.TextStyle.Color); Assert.True(cell.TextStyle.Bold);
        Assert.Equal(8, cell.TextRanges[0].Style.FontSize); Assert.True(cell.TextRanges[0].Style.Italic); Assert.True(cell.TextRanges[0].Style.Bold); Assert.Equal("#0000FF", cell.TextRanges[0].Style.Color);
        Assert.Null(result.Cells[1].TextStyle); // Header semantics remain inherited, not flattened.
        session.Undo(); Assert.Same(table, session.PrimaryShape!.Table);
    }
    [Fact] public void ThemeChangeUpdatesTableAccentWithoutOverwritingExplicitCellFills()
    {
        var table = TableModel.Create(2, 2); table = table with { Cells = table.Cells.SetItem(0, table.Cells[0] with { Fill = "#012345" }) };
        var doc = SlideFactory.ApplyTheme(Deck(Shape(table)), "Ocean"); var result = doc.Slides[0].Shapes[0].Table!;
        Assert.Equal("#187EAB", result.Accent); Assert.Equal("#012345", TableModel.Fill(result, result.Cells[0])); Assert.Equal("#187EAB", TableModel.Fill(result, result.Cells[1]));
    }
    [Fact] public void WholeDocumentEditsAlsoReconcileTableStyleChanges()
    {
        var shape = Shape(TableModel.Create()); var session = new EditorSession(Deck(shape));
        session.EditDocument("Accent", d => d with { Slides = [d.Slides[0] with { Shapes = [shape with { Fill = "#123456" }] }] });
        Assert.Equal("#123456", session.CurrentSlide.Shapes[0].Table!.Accent);
    }
    [Fact] public void ReplaceAllPreservesMergedCellRangesAndIsUndoable()
    {
        var table = TableModel.Create(1, 2); table = table with { Cells = [table.Cells[0] with { Text = "Hello red", TextStyle = new() { Color = "#FF0000" } }, table.Cells[1] with { Text = "Hello blue", TextStyle = new() { Color = "#0000FF" } }] };
        table = TableModel.Merge(table, new(0, 0, 1, 2)); var shape = Shape(table); var session = new EditorSession(Deck(shape));
        session.ReplaceText("Hello", "Hi"); var result = session.CurrentSlide.Shapes[0].Table!;
        Assert.Single(result.Cells); Assert.Equal(2, result.Cells[0].ColumnSpan); Assert.Equal("Hi red\nHi blue", result.Cells[0].Text);
        Assert.Equal("#0000FF", RichText.StyleAt(TableModel.TextShape(result, result.Cells[0]), 7).Color);
        session.Undo(); Assert.Same(table, session.CurrentSlide.Shapes[0].Table); session.Redo(); Assert.Equal("Hi red\nHi blue", session.CurrentSlide.Shapes[0].Table!.Cells[0].Text);
    }
    [Fact] public void ReplaceAllUpgradesLegacyCellsOnlyWhenMatching()
    {
        var shape = new SlideShape { Kind = ShapeKind.Table, TableColumns = 2, Cells = ["a", "b", "A"] };
        Assert.Same(shape, TableModel.ReplaceAll(shape, "missing", "new"));
        var result = TableModel.ReplaceAll(shape, "a", "new"); Assert.NotNull(result.Table); Assert.Equal(new[] { "new", "b", "new", "" }, result.Cells);
    }
    [Fact] public void TextExpansionBeyondTableLimitDoesNotChangeTheSession()
    {
        var shape = Shape(TableModel.Create(1, 1) with { Cells = [new() { Text = "a" }] }); var session = new EditorSession(Deck(shape));
        Assert.Throws<InvalidDataException>(() => session.ReplaceText("a", new string('x', TableModel.MaxTextLength + 1)));
        Assert.Same(shape, session.CurrentSlide.Shapes[0]); Assert.False(session.CanUndo);
    }
    [Theory, InlineData(false, "#00FF00"), InlineData(true, "#F1F4F8")]
    public void StandaloneTableRenderingUsesTheSamePipelineAndRestoresState(bool bandedRows, string expectedColor)
    {
        var table = TableModel.Create(1, 2) with { HeaderRow = false, BandedRows = bandedRows, BodyFill = "#00FF00" };
        using var surface = SKSurface.Create(new SKImageInfo(600, 300)); using var renderer = new SlideRenderer(); surface.Canvas.Translate(10, 10); var matrix = surface.Canvas.TotalMatrix; var count = surface.Canvas.SaveCount;
        renderer.RenderTable(surface.Canvas, table, new(0, 0, 500, 200)); Assert.Equal(matrix, surface.Canvas.TotalMatrix); Assert.Equal(count, surface.Canvas.SaveCount);
        using var image = surface.Snapshot(); using var bitmap = SKBitmap.FromImage(image); Assert.Equal(SKColor.Parse(expectedColor), bitmap.GetPixel(100, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => renderer.RenderTable(surface.Canvas, table, new(0, 0, float.NaN, 200)));
    }
    [Fact] public void EmptyCellParagraphDefaultsRoundTrip()
    {
        var table = TableModel.Create(1, 1) with { Cells = [new() { TextStyle = new() { Alignment = ParagraphAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, FontSize = 33, Underline = true, LineSpacing = 1.5f } }] };
        var copy = PptxCodec.Import(PptxCodec.Export(Deck(Shape(table))).Data).Document.Slides[0].Shapes[0].Table!.Cells[0].TextStyle!;
        Assert.Equal(ParagraphAlignment.Right, copy.Alignment); Assert.Equal(VerticalAlignment.Bottom, copy.VerticalAlignment); Assert.Equal(33, copy.FontSize); Assert.True(copy.Underline); Assert.Equal(1.5f, copy.LineSpacing);
    }
    [Theory, InlineData("NaN"), InlineData("-1"), InlineData("bad"), InlineData("Infinity"), InlineData("9999999999")]
    public void InvalidBorderWidthsAreRejected(string value)
    {
        var bytes = PptxCodec.Export(Deck(Shape(TableModel.Create()))).Data;
        using var input = new ZipArchive(new MemoryStream(bytes)); using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        foreach (var entry in input.Entries)
        {
            using var source = entry.Open(); using var target = zip.CreateEntry(entry.FullName).Open();
            if (entry.FullName == "ppt/slides/slide1.xml") { var doc = XDocument.Load(source); XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main"; doc.Descendants(a + "lnL").First().SetAttributeValue("w", value); doc.Save(target); }
            else source.CopyTo(target);
        }
        Assert.Throws<InvalidDataException>(() => PptxCodec.Import(output.ToArray()));
    }
}
