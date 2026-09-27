using System.Collections.Immutable;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Validation;
using PresentationSpace.Core;
using PresentationSpace.Formats;
using PresentationSpace.Rendering.Skia;
using SkiaSharp;
using Xunit;
using OpenXmlPresentation = DocumentFormat.OpenXml.Packaging.PresentationDocument;

namespace PresentationSpace.Tests;

public sealed class TableTests
{
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static TableSpec Table(int rows = 3, int columns = 4) => TableModel.Create(rows, columns) is { } t ? t with { Cells = t.Cells.Select(c => c with { Text = $"R{c.Row + 1}C{c.Column + 1}" }).ToImmutableArray() } : throw new Exception();
    private static SlideShape Shape(TableSpec table) => TableModel.Apply(new() { Bounds = new(100, 100, 600, 300) }, table);
    private static PresentationDocument Deck(SlideShape shape) => new() { Slides = [new() { Shapes = [shape] }] };
    private static XElement Part(byte[] bytes)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes)); using var stream = zip.GetEntry("ppt/slides/slide1.xml")!.Open(); return XElement.Load(stream);
    }
    private static byte[] ChangePart(byte[] bytes, Action<XElement> edit)
    {
        using var stream = new MemoryStream(); stream.Write(bytes); stream.Position = 0;
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Update, true))
        {
            var entry = zip.GetEntry("ppt/slides/slide1.xml")!; XElement root; using (var input = entry.Open()) root = XElement.Load(input); entry.Delete(); edit(root);
            using var output = zip.CreateEntry("ppt/slides/slide1.xml").Open(); root.Save(output);
        }
        return stream.ToArray();
    }
    [Fact] public void LegacyTablesUpgradeWithoutLosingValues()
    {
        var shape = new SlideShape { Kind = ShapeKind.Table, TableColumns = 2, Cells = ["A", "B", "C", "D"] };
        var table = TableModel.Get(shape); Assert.Equal(2, table.RowCount); Assert.Equal(2, table.ColumnCount); Assert.Equal(shape.Cells.ToArray(), table.Cells.Select(c => c.Text));
        Assert.Null(shape.Table); TableModel.Validate(table);
    }
    [Fact] public void LegacyNonRectangularTailIsPadded()
    {
        var table = TableModel.Get(new() { Kind = ShapeKind.Table, TableColumns = 2, Cells = ["A", "B", "C"] }); Assert.Equal("", TableModel.CellAt(table, 1, 1).Text);
    }
    [Fact] public void NewTablesSerializeAsVersionThreeAndReadOldVersions()
    {
        var document = Deck(Shape(Table())); string json = DocumentSerializer.Serialize(document);
        Assert.Contains("\"SchemaVersion\": 3", json); var copy = DocumentSerializer.Deserialize(json).Slides[0].Shapes[0].Table!;
        Assert.Equal(Table().Cells.Select(c => c.Text), copy.Cells.Select(c => c.Text));
        Assert.Equal(1, DocumentSerializer.Deserialize(DocumentSerializer.Serialize(new())).SchemaVersion);
        Assert.Equal(2, DocumentSerializer.Deserialize(DocumentSerializer.Serialize(new() { SchemaVersion = 2 })).SchemaVersion);
    }
    public static IEnumerable<object[]> MergeCases()
    {
        for (int r = 0; r < 3; r++) for (int c = 0; c < 4; c++) for (int height = 1; height <= 3 - r; height++) for (int width = 1; width <= 4 - c; width++)
            yield return [r, c, height, width];
    }
    [Theory, MemberData(nameof(MergeCases))] public void EveryRectangularMergePartitionsGridAndRetainsContent(int row, int column, int height, int width)
    {
        var source = Table(); var range = new TableRange(row, column, height, width); var result = TableModel.Merge(source, range); TableModel.Validate(result);
        Assert.Equal(source.Cells.Length - height * width + 1, result.Cells.Length);
        var cell = TableModel.CellAt(result, row, column); Assert.Equal(height, cell.RowSpan); Assert.Equal(width, cell.ColumnSpan);
        Assert.Equal(string.Join('\n', source.Cells.Where(range.Contains).Select(c => c.Text)), cell.Text);
        for (int r = row; r < row + height; r++) for (int c = column; c < column + width; c++) Assert.Same(cell, TableModel.CellAt(result, r, c));
        TableModel.Validate(TableModel.Split(result, row, column)); Assert.Equal(12, source.Cells.Length);
    }
    [Fact] public void MergePreservesIndependentCharacterStyles()
    {
        var source = Table(1, 2); source = source with { Cells = [source.Cells[0] with { Text = "Red", TextStyle = new() { Color = "#FF0000" } }, source.Cells[1] with { Text = "Blue", TextStyle = new() { Color = "#0000FF", Bold = true } }] };
        var merged = TableModel.Merge(source, new(0, 0, 1, 2)); var shape = TableModel.TextShape(merged, merged.Cells[0]);
        Assert.Equal("Red\nBlue", shape.Text); Assert.Equal("#FF0000", RichText.StyleAt(shape, 0).Color); Assert.Equal("#0000FF", RichText.StyleAt(shape, 4).Color); Assert.True(RichText.StyleAt(shape, 5).Bold);
    }
    [Fact] public void SplitKeepsCombinedTextAtOriginAndCreatesBlankCells()
    {
        var merged = TableModel.Merge(Table(2, 2), new(0, 0, 2, 2)); var split = TableModel.Split(merged, 1, 1); Assert.Equal(4, split.Cells.Length);
        Assert.Equal(merged.Cells[0].Text, split.Cells[0].Text); Assert.All(split.Cells.Skip(1), c => Assert.Equal("", c.Text));
    }
    [Fact] public void PartialMergeSelectionIsRejectedAndCanBeExpanded()
    {
        var source = TableModel.Merge(Table(), new(0, 0, 2, 2)); Assert.Throws<InvalidOperationException>(() => TableModel.Merge(source, new(1, 1, 2, 2)));
        var range = TableModel.ExpandRange(source, new(1, 1, 2, 2)); Assert.Equal(new TableRange(0, 0, 3, 3), range); TableModel.Validate(TableModel.Merge(source, range));
    }
    [Theory, InlineData(true, 0), InlineData(true, 1), InlineData(true, 2), InlineData(true, 3), InlineData(false, 0), InlineData(false, 1), InlineData(false, 2), InlineData(false, 3)]
    public void InsertThenDeleteTrackRestoresMergedGeometry(bool row, int index)
    {
        var source = TableModel.Merge(Table(3, 3), new(0, 0, 2, 2)); var inserted = TableModel.InsertTrack(source, index, row); var deleted = TableModel.DeleteTrack(inserted, index, row);
        TableModel.Validate(inserted); TableModel.Validate(deleted); Assert.Equal(source.Cells.ToArray(), deleted.Cells.ToArray());
        Assert.Equal(source.ColumnWidths.ToArray(), deleted.ColumnWidths.ToArray()); Assert.Equal(source.RowHeights.ToArray(), deleted.RowHeights.ToArray());
    }
    [Theory, InlineData(true), InlineData(false)] public void DeletingMergeOriginTrackPreservesTextInSurvivingMerge(bool row)
    {
        var merged = TableModel.Merge(Table(3, 3), new(0, 0, 2, 2)); string text = TableModel.CellAt(merged, 0, 0).Text;
        var deleted = TableModel.DeleteTrack(merged, 0, row); TableModel.Validate(deleted); Assert.Equal(text, TableModel.CellAt(deleted, 0, 0).Text);
    }
    [Fact] public void LastTrackCannotBeDeleted() { Assert.Throws<InvalidOperationException>(() => TableModel.DeleteTrack(Table(1, 1), 0, true)); }
    [Fact] public void InsertionBeyondLimitIsRejected() { Assert.Throws<InvalidDataException>(() => TableModel.InsertTrack(TableModel.Create(100, 1), 20, true)); }
    [Fact] public void InvalidLayoutsAreRejectedBeforeCoverageAllocation()
    {
        var t = Table();
        Assert.Throws<InvalidDataException>(() => TableModel.Validate(t with { RowHeights = [] }));
        Assert.Throws<InvalidDataException>(() => TableModel.Validate(t with { RowHeights = default }));
        Assert.Throws<InvalidDataException>(() => TableModel.Validate(t with { ColumnWidths = [float.NaN] }));
        Assert.Throws<InvalidDataException>(() => TableModel.Validate(t with { Cells = t.Cells.RemoveAt(0) }));
        Assert.Throws<InvalidDataException>(() => TableModel.Validate(t with { Cells = t.Cells.Add(t.Cells[0]) }));
        Assert.Throws<InvalidDataException>(() => TableModel.Validate(t with { Cells = t.Cells.SetItem(0, t.Cells[0] with { RowSpan = int.MaxValue }) }));
        Assert.Throws<InvalidDataException>(() => TableModel.Validate(t with { Cells = t.Cells.SetItem(0, t.Cells[0] with { MarginLeft = float.PositiveInfinity }) }));
        Assert.Throws<InvalidDataException>(() => TableModel.Validate(t with { Cells = t.Cells.SetItem(0, t.Cells[0] with { Left = new() { Width = -1 } }) }));
        Assert.Throws<InvalidDataException>(() => TableModel.Validate(t with { Cells = t.Cells.SetItem(0, t.Cells[0] with { Fill = "bad" }) }));
        Assert.Throws<InvalidDataException>(() => TableModel.Validate(t with { Cells = t.Cells.SetItem(0, t.Cells[0] with { Text = "😀", TextRanges = [new(0, 1, new())] }) }));
    }
    [Fact] public void CellTextEditingPreservesUnaffectedRuns()
    {
        var table = Table(1, 1); table = table with { Cells = [table.Cells[0] with { Text = "Alpha Beta", TextStyle = new(), TextRanges = [new(6, 4, new() { Bold = true })] }] };
        var edited = TableModel.SetText(table, 0, 0, "A Beta"); Assert.True(RichText.StyleAt(TableModel.TextShape(edited, edited.Cells[0]), 2).Bold);
    }
    [Fact] public void TableLayoutUsesTrackWeightsAndMapsCoveredCells()
    {
        var table = TableModel.Merge(Table(2, 2) with { ColumnWidths = [1, 3], RowHeights = [1, 2] }, new(0, 0, 1, 2)); var layout = new TableLayout(table, new(10, 20, 400, 300));
        Assert.Equal(110, layout.X[1]); Assert.Equal(120, layout.Y[1]); Assert.Equal(400, layout.Bounds(layout.Owner(0, 1)).Width);
        Assert.Same(layout.Owner(0, 0), layout.HitTest(new(300, 60))); Assert.Null(layout.HitTest(new(9, 20))); Assert.Same(layout.Owner(1, 1), layout.HitTest(new(410, 320)));
    }
    [Fact] public void TableSessionChangesUndoWithoutLosingPriorSnapshot()
    {
        var shape = Shape(Table()); var session = new EditorSession(Deck(shape)); session.Select(shape.Id);
        session.Apply("Merge", s => TableModel.Apply(s, TableModel.Merge(TableModel.Get(s), new(0, 0, 2, 2)))); Assert.Equal(9, session.PrimaryShape!.Table!.Cells.Length);
        session.Undo(); Assert.Same(shape.Table, session.PrimaryShape!.Table); session.Redo(); Assert.Equal(9, session.PrimaryShape!.Table!.Cells.Length);
    }
    [Fact] public void ExistingFillCommandChangesTableAccent()
    {
        var shape = Shape(Table()); var session = new EditorSession(Deck(shape)); session.Select(shape.Id); session.Apply("Fill", s => s with { Fill = "#0000FF" });
        Assert.Equal("#0000FF", session.PrimaryShape!.Table!.Accent); Assert.Equal("#0000FF", TableModel.Fill(session.PrimaryShape.Table!, session.PrimaryShape.Table!.Cells[0]));
    }
    [Fact] public void LockedTableCannotBeChangedBySelectionCommands()
    {
        var shape = Shape(Table()) with { Locked = true }; var session = new EditorSession(Deck(shape)); session.Select(shape.Id); session.Apply("Fill", s => s with { Fill = "#0000FF" }); Assert.Same(shape, session.CurrentSlide.Shapes[0]);
    }
    [Theory, InlineData(1, 3), InlineData(3, 1), InlineData(2, 3)] public void NativeMergedTablesPassSchemaAndRoundTrip(int rows, int columns)
    {
        var table = TableModel.Merge(Table(3, 4), new(0, 0, rows, columns)); var data = PptxCodec.Export(Deck(Shape(table))).Data;
        using var stream = new MemoryStream(data); using var document = OpenXmlPresentation.Open(stream, false);
        var errors = new OpenXmlValidator().Validate(document).Select(e => $"{e.Path?.XPath}: {e.Description}").ToArray(); Assert.True(errors.Length == 0, string.Join('\n', errors));
        var copy = PptxCodec.Import(data).Document.Slides[0].Shapes.Single().Table!; TableModel.Validate(copy);
        Assert.Equal(table.Cells.Select(c => (c.Row, c.Column, c.RowSpan, c.ColumnSpan, c.Text)), copy.Cells.Select(c => (c.Row, c.Column, c.RowSpan, c.ColumnSpan, c.Text)));
        Assert.Equal(12, Part(data).Descendants(A + "tc").Count());
    }
    [Fact] public void TwoDimensionalContinuationsCarryBothMergeFlags()
    {
        var xml = Part(PptxCodec.Export(Deck(Shape(TableModel.Merge(Table(2, 2), new(0, 0, 2, 2))))).Data);
        var rows = xml.Descendants(A + "tr").ToArray(); var first = rows[0].Elements(A + "tc").ToArray(); var last = rows[1].Elements(A + "tc").ToArray();
        Assert.Equal("2", (string?)first[0].Attribute("gridSpan")); Assert.Equal("2", (string?)first[1].Attribute("rowSpan"));
        Assert.Equal("1", (string?)last[1].Attribute("hMerge")); Assert.Equal("1", (string?)last[1].Attribute("vMerge")); Assert.Empty(last[1].Descendants(A + "t"));
    }
    [Fact] public void CellFillsBordersMarginsAndEmptyTextStylesRoundTrip()
    {
        var table = Table(2, 2); var border = new TableBorder { Color = "#009900", Width = 3, Dash = TableBorderDash.Dash };
        table = table with { Cells = table.Cells.SetItem(0, table.Cells[0] with { Text = "", Fill = "#FFFF00", MarginLeft = 17, MarginTop = 13, Left = border, TextStyle = new() { FontFamily = "Georgia", FontSize = 33, Bold = true, VerticalAlignment = VerticalAlignment.Bottom } }) };
        var copy = PptxCodec.Import(PptxCodec.Export(Deck(Shape(table))).Data).Document.Slides[0].Shapes[0].Table!; var cell = TableModel.CellAt(copy, 0, 0);
        Assert.Equal("#FFFF00", cell.Fill); Assert.Equal(border, cell.Left); Assert.Equal(17, cell.MarginLeft); Assert.Equal(13, cell.MarginTop); Assert.Equal(33, cell.TextStyle!.FontSize); Assert.Equal(VerticalAlignment.Bottom, cell.TextStyle.VerticalAlignment);
    }
    [Fact] public void MergedMixedTextColorsSurvivePptx()
    {
        var table = Table(1, 2); table = table with { Cells = [table.Cells[0] with { Text = "First", TextStyle = new() { Color = "#FF0000" } }, table.Cells[1] with { Text = "Second", TextStyle = new() { Color = "#0000FF" } }] };
        table = TableModel.Merge(table, new(0, 0, 1, 2)); var copy = PptxCodec.Import(PptxCodec.Export(Deck(Shape(table))).Data).Document.Slides[0].Shapes[0].Table!;
        var text = TableModel.TextShape(copy, copy.Cells[0]); Assert.Equal("#FF0000", RichText.StyleAt(text, 0).Color); Assert.Equal("#0000FF", RichText.StyleAt(text, 6).Color);
    }
    [Theory, InlineData("rowSpan", "2147483647"), InlineData("gridSpan", "0"), InlineData("rowSpan", "1.5"), InlineData("gridSpan", "-1"), InlineData("hMerge", "1"), InlineData("vMerge", "yes")]
    public void MalformedImportedMergesAreRejected(string name, string value)
    {
        var bytes = ChangePart(PptxCodec.Export(Deck(Shape(Table()))).Data, xml => xml.Descendants(A + "tc").First().SetAttributeValue(name, value)); Assert.Throws<InvalidDataException>(() => PptxCodec.Import(bytes));
    }
    [Fact] public void MissingTableGridPositionIsRejected()
    {
        var bytes = ChangePart(PptxCodec.Export(Deck(Shape(Table()))).Data, xml => xml.Descendants(A + "tc").Last().Remove()); Assert.Throws<InvalidDataException>(() => PptxCodec.Import(bytes));
    }
    [Fact] public void UnflaggedOverlappingOriginIsRejected()
    {
        var bytes = ChangePart(PptxCodec.Export(Deck(Shape(Table()))).Data, xml => xml.Descendants(A + "tc").First().SetAttributeValue("gridSpan", 2)); Assert.Throws<InvalidDataException>(() => PptxCodec.Import(bytes));
    }
    [Fact] public void MergedCellsRenderWithoutInternalGridLine()
    {
        var table = TableModel.Merge(Table(1, 2), new(0, 0, 1, 2)); table = table with { Cells = [table.Cells[0] with { Text = "", Fill = "#FF0000", TextRanges = [] }] };
        var shape = Shape(table); using var renderer = new SlideRenderer(); using var bitmap = SKBitmap.Decode(renderer.ExportPng(Deck(shape), Deck(shape).Slides[0], 1280));
        Assert.Equal(SKColors.Red, bitmap.GetPixel(400, 220)); Assert.Equal(SKColors.Red, bitmap.GetPixel(101, 220));
    }
    [Fact] public void RendererUsesPerCellFillsAndRestoresCallerState()
    {
        var table = Table(1, 2); table = table with { Cells = [table.Cells[0] with { Text = "", Fill = "#FF0000" }, table.Cells[1] with { Text = "", Fill = "#0000FF" }] };
        var shape = Shape(table); using var renderer = new SlideRenderer(); using var surface = SKSurface.Create(new SKImageInfo(1280, 720)); var c = surface.Canvas; c.Translate(3, 5); var matrix = c.TotalMatrix; int count = c.SaveCount;
        renderer.Render(c, Deck(shape), Deck(shape).Slides[0]); Assert.Equal(matrix, c.TotalMatrix); Assert.Equal(count, c.SaveCount);
        using var bitmap = SKBitmap.FromImage(surface.Snapshot()); Assert.Equal(SKColors.Red, bitmap.GetPixel(203, 205)); Assert.Equal(SKColors.Blue, bitmap.GetPixel(603, 205));
    }
    [Fact] public void RandomTrackAndMergeOperationsAlwaysPartitionGrid()
    {
        var random = new Random(71); var table = Table(5, 5);
        for (int i = 0; i < 150; i++)
        {
            int row = random.Next(table.RowCount), column = random.Next(table.ColumnCount);
            table = (i % 5) switch
            {
                0 => TableModel.Merge(table, TableModel.ExpandRange(table, new(row, column, 1, table.ColumnCount - column))),
                1 => TableModel.Split(table, row, column),
                2 when table.RowCount < 12 => TableModel.InsertTrack(table, row, true),
                3 when table.RowCount > 1 => TableModel.DeleteTrack(table, row, true),
                _ => table
            };
            TableModel.Validate(table);
        }
    }
}
