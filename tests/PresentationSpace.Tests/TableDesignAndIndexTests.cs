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

public sealed class TableDesignAndIndexTests
{
    private static readonly TableBorder Red = new() { Color = "#CC0000", Width = 3, Dash = TableBorderDash.Dash };
    private static PresentationDocument Deck(TableSpec table)
    {
        var shape = TableModel.Apply(new() { Bounds = new(50, 50, 600, 300) }, table);
        return new() { Slides = [new() { Shapes = [shape] }] };
    }
    [Fact] public void IndexSharesOnlyTheExactImmutableSnapshot()
    {
        var table = TableModel.Create(10, 10); var first = TableGridIndex.For(table);
        Assert.Same(first, TableGridIndex.For(table));
        var changed = TableModel.SetText(table, 9, 9, "changed"); var second = TableGridIndex.For(changed);
        Assert.NotSame(first, second); Assert.Equal("", first.Owner(9, 9).Text); Assert.Equal("changed", second.Owner(9, 9).Text);
        Assert.Same(first, TableGridIndex.For(table));
    }
    [Fact] public void EveryMaximumGridCoordinateResolvesToItsOwner()
    {
        var table = TableModel.Create(100, 100); var index = TableGridIndex.For(table);
        for (int row = 0; row < 100; row++) for (int column = 0; column < 100; column++)
            Assert.Same(table.Cells[row * 100 + column], index.Owner(row, column));
    }
    [Fact] public void ReadingOrderSkipsMergedContinuationsAndClampsSafely()
    {
        var table = TableModel.Merge(TableModel.Create(3, 3), new(0, 0, 2, 2));
        table = table with { Cells = table.Cells.Reverse().ToImmutableArray() };
        var index = TableGridIndex.For(table); var owner = index.Owner(1, 1);
        Assert.Equal((0, 0), (owner.Row, owner.Column));
        Assert.Equal((0, 2), (index.Move(1, 1, 1).Row, index.Move(1, 1, 1).Column));
        Assert.Same(owner, index.Move(0, 0, int.MinValue)); Assert.Equal((2, 2), (index.Move(0, 0, int.MaxValue).Row, index.Move(0, 0, int.MaxValue).Column));
    }
    [Theory] [InlineData(-1, 0)] [InlineData(0, -1)] [InlineData(3, 0)] [InlineData(0, 3)] [InlineData(int.MaxValue, int.MaxValue)]
    public void IndexRejectsOutOfRangeWithoutAliasingAnotherRow(int row, int column)
    {
        var index = TableGridIndex.For(TableModel.Create(3, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => index.Owner(row, column));
        Assert.Throws<ArgumentOutOfRangeException>(() => index.Move(row, column, 1));
    }
    [Fact] public void InvalidSnapshotsNeverEnterCache()
    {
        var table = TableModel.Create(2, 2); var bad = table with { Cells = table.Cells.RemoveAt(0) };
        Assert.Throws<InvalidDataException>(() => TableGridIndex.For(bad));
        Assert.Throws<InvalidDataException>(() => TableGridIndex.For(bad));
    }
    [Fact] public void CachedLayoutOwnershipIsIndependentOfPublicEdgeArrays()
    {
        var table = TableModel.Create(2, 2); var first = new TableLayout(table, new(0, 0, 200, 100));
        first.X[1] = 0;
        var second = new TableLayout(table, new(10, 20, 400, 200));
        Assert.Equal(210, second.X[1]); Assert.Same(table.Cells[3], second.Owner(1, 1));
    }
    [Fact] public void HitTestUsesRightBottomTrackAtSharedBoundary()
    {
        var table = TableModel.Create(3, 3); var layout = new TableLayout(table, new(10, 20, 300, 300));
        Assert.Same(table.Cells[4], layout.HitTest(new(110, 120)));
        Assert.Same(table.Cells[8], layout.HitTest(new(310, 320)));
        Assert.Null(layout.HitTest(new(float.NaN, 100))); Assert.Null(layout.HitTest(new(100, float.PositiveInfinity)));
    }
    [Theory] [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(0)] [InlineData(-1)]
    public void LayoutRejectsInvalidDimensions(float width) => Assert.Throws<ArgumentOutOfRangeException>(() => new TableLayout(TableModel.Create(), new(0, 0, width, 100)));
    [Fact] public void WarmIndexedLookupAllocatesNoManagedObjects()
    {
        var table = TableModel.Create(32, 32); _ = TableModel.CellAt(table, 0, 0);
        long before = GC.GetAllocatedBytesForCurrentThread(); TableCell? sink = null;
        for (int i = 0; i < 8192; i++) sink = TableModel.CellAt(table, i % 32, i / 32 % 32);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(sink); Assert.Equal(0, bytes);
    }
    [Fact] public void ColumnFlagsUseSchemaFourWithoutForcingOlderTablesToUpgrade()
    {
        var table = TableModel.Create() with { FirstColumn = true, LastColumn = true, BandedColumns = true };
        string json = DocumentSerializer.Serialize(Deck(table)); Assert.Contains("\"SchemaVersion\": 4", json);
        var copy = DocumentSerializer.Deserialize(json).Slides[0].Shapes[0].Table!;
        Assert.True(copy.FirstColumn && copy.LastColumn && copy.BandedColumns);
        Assert.Contains("\"SchemaVersion\": 3", DocumentSerializer.Serialize(Deck(TableModel.Create())));
        foreach (int version in new[] { 1, 2, 3, 4 }) Assert.Equal(version, DocumentSerializer.Deserialize(DocumentSerializer.Serialize(new() { SchemaVersion = version })).SchemaVersion);
    }
    [Theory] [InlineData(TableStylePreset.Office)] [InlineData(TableStylePreset.Blue)] [InlineData(TableStylePreset.Green)] [InlineData(TableStylePreset.Slate)]
    public void PresetsDoNotLoseDirectOverridesOrGeometry(TableStylePreset preset)
    {
        var table = TableModel.Merge(TableModel.Create(3, 3), new(0, 0, 1, 2));
        var direct = table.Cells[0] with { Fill = "#FFFF00", Text = "Keep", TextStyle = new() { FontSize = 32 } };
        table = table with { Cells = table.Cells.SetItem(0, direct) };
        var result = TableModel.ApplyStyle(table, preset); TableModel.Validate(result);
        Assert.Equal(table.Cells, result.Cells); Assert.Equal(table.ColumnWidths, result.ColumnWidths); Assert.Equal(table.RowHeights, result.RowHeights);
        Assert.Equal("#FFFF00", TableModel.Fill(result, direct)); Assert.Equal(32, TableModel.Style(result, direct).FontSize);
    }
    [Fact] public void EmphasisOverridesBandingButNotExplicitCellStyle()
    {
        var table = TableModel.Create(4, 4) with { FirstColumn = true, LastColumn = true, BandedColumns = true, BandedRows = false, HeaderRow = false };
        Assert.Equal(table.Accent, TableModel.Fill(table, TableModel.CellAt(table, 1, 0)));
        Assert.Equal(table.Accent, TableModel.Fill(table, TableModel.CellAt(table, 1, 3)));
        Assert.Equal(table.BodyFill, TableModel.Fill(table, TableModel.CellAt(table, 1, 1)));
        Assert.Equal(table.BandFill, TableModel.Fill(table, TableModel.CellAt(table, 1, 2)));
        var direct = table.Cells[0] with { TextStyle = new() { Color = "#001122", Bold = false } };
        Assert.Equal("#001122", TableModel.Style(table, direct).Color); Assert.False(TableModel.Style(table, direct).Bold);
    }
    [Theory] [InlineData(TableBorderScope.All, 24)] [InlineData(TableBorderScope.Outside, 10)] [InlineData(TableBorderScope.Inside, 14)]
    [InlineData(TableBorderScope.InsideHorizontal, 6)] [InlineData(TableBorderScope.InsideVertical, 8)]
    [InlineData(TableBorderScope.Top, 3)] [InlineData(TableBorderScope.Bottom, 3)] [InlineData(TableBorderScope.Left, 2)] [InlineData(TableBorderScope.Right, 2)]
    public void BorderScopesOnlyChangeRequestedEdges(TableBorderScope scope, int expected)
    {
        var table = TableModel.Create(2, 3); var result = TableModel.SetBorders(table, new(0, 0, 2, 3), scope, Red);
        Assert.Equal(expected, result.Cells.Sum(c => new[] { c.Left, c.Right, c.Top, c.Bottom }.Count(b => b == Red)));
        Assert.All(table.Cells, c => Assert.NotEqual(Red, c.Left)); TableModel.Validate(result);
    }
    [Fact] public void SharedBorderRemovalCannotBeOverriddenByItsNeighbor()
    {
        var table = TableModel.SetBorders(TableModel.Create(2, 2), new(0, 0, 2, 2), TableBorderScope.All, Red);
        var result = TableModel.SetBorders(table, new(0, 0, 1, 1), TableBorderScope.Right, new() { Width = 0 });
        Assert.Equal(0, TableModel.CellAt(result, 0, 0).Right.Width); Assert.Equal(0, TableModel.CellAt(result, 0, 1).Left.Width);
        Assert.Equal(Red, TableModel.CellAt(result, 1, 0).Right);
    }
    [Fact] public void PartialNeighborMergeEdgeIsRejectedAtomically()
    {
        var table = TableModel.Merge(TableModel.Create(2, 2), new(0, 1, 2, 1));
        Assert.Throws<InvalidOperationException>(() => TableModel.SetBorders(table, new(0, 0, 1, 1), TableBorderScope.Right, Red));
        Assert.Equal(1, TableModel.CellAt(table, 0, 0).Right.Width);
        var full = TableModel.SetBorders(table, new(0, 0, 2, 1), TableBorderScope.Right, Red);
        Assert.Equal(Red, TableModel.CellAt(full, 0, 1).Left);
        Assert.Equal(Red, TableModel.CellAt(full, 1, 0).Right);
    }
    [Fact] public void InteriorOfSingleMergedCellHasNoBorderToChange()
    {
        var table = TableModel.Merge(TableModel.Create(2, 2), new(0, 0, 2, 2));
        Assert.Same(table, TableModel.SetBorders(table, new(1, 1, 1, 1), TableBorderScope.Inside, Red));
    }
    [Fact] public void NoBordersAndUndoRestoreExactSnapshots()
    {
        var document = Deck(TableModel.Create(3, 3)); var session = new EditorSession(document); session.Select(session.CurrentSlide.Shapes[0].Id);
        var before = session.PrimaryShape!;
        session.Apply("Borders", s => TableModel.Apply(s, TableModel.SetBorders(s.Table!, new(0, 0, 3, 3), TableBorderScope.None, Red)));
        Assert.All(session.PrimaryShape!.Table!.Cells, c => Assert.Equal(0, c.Right.Width));
        session.Undo(); Assert.Same(before, session.PrimaryShape); session.Redo(); Assert.Equal(0, session.PrimaryShape!.Table!.Cells[0].Left.Width);
    }
    [Theory] [InlineData(-1)] [InlineData(101)] [InlineData(float.NaN)]
    public void InvalidBorderIsRejected(float width) => Assert.Throws<InvalidDataException>(() => TableModel.SetBorders(TableModel.Create(), new(0, 0, 1, 1), TableBorderScope.All, Red with { Width = width }));
    [Fact] public void NativeDrawingMlPersistsAllColumnFlagsAndValidates()
    {
        var table = TableModel.ApplyStyle(TableModel.Create(3, 3), TableStylePreset.Blue) with { FirstColumn = true, LastColumn = true, BandedColumns = true };
        table = TableModel.SetBorders(table, new(0, 0, 3, 3), TableBorderScope.Outside, Red);
        byte[] bytes = PptxCodec.Export(Deck(table)).Data;
        using (var document = OpenXmlPresentation.Open(new MemoryStream(bytes), false))
            Assert.Empty(new OpenXmlValidator().Validate(document));
        using (var zip = new ZipArchive(new MemoryStream(bytes)))
        using (var stream = zip.GetEntry("ppt/slides/slide1.xml")!.Open())
        {
            XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
            var props = XElement.Load(stream).Descendants(a + "tblPr").Single();
            foreach (string name in new[] { "firstCol", "lastCol", "bandCol" }) Assert.Equal("1", (string?)props.Attribute(name));
        }
        var copy = PptxCodec.Import(bytes).Document.Slides[0].Shapes[0].Table!;
        Assert.True(copy.FirstColumn && copy.LastColumn && copy.BandedColumns);
        Assert.Equal(Red, TableModel.CellAt(copy, 0, 0).Top);
        Assert.Equal(TableModel.Fill(table, table.Cells[0]), TableModel.Fill(copy, copy.Cells[0]));
    }
    [Fact] public void ColumnEmphasisAndBandingRenderWithResolvedFills()
    {
        var table = TableModel.Create(2, 4) with { HeaderRow = false, BandedRows = false, BandedColumns = true, FirstColumn = true, LastColumn = true };
        using var renderer = new SlideRenderer(); using var surface = SKSurface.Create(new SKImageInfo(400, 200));
        renderer.RenderTable(surface.Canvas, table, new(0, 0, 400, 200));
        using var bitmap = SKBitmap.FromImage(surface.Snapshot());
        Assert.Equal(SlideRenderer.Color(table.Accent), bitmap.GetPixel(50, 50));
        Assert.Equal(SlideRenderer.Color(table.BodyFill), bitmap.GetPixel(150, 50));
        Assert.Equal(SlideRenderer.Color(table.BandFill), bitmap.GetPixel(250, 50));
        Assert.Equal(SlideRenderer.Color(table.Accent), bitmap.GetPixel(350, 50));
    }
}
