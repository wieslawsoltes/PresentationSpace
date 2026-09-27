using System.Collections.Immutable;
using System.Globalization;
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

public sealed class ChartTests
{
    private static readonly XNamespace C = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static ChartSpec Data(ChartKind kind = ChartKind.Column, int series = 2) => new()
    {
        Kind = kind, Title = "A & B <forecast>", ShowLegend = true, ShowValues = true,
        Categories = ["=Literal label", "Two\tcolumns", "Three\nlines"],
        Series = Enumerable.Range(0, series).Select(i => new ChartSeries { Name = i == 0 ? "=Not a formula" : "Series \"two\"", Color = ChartModel.PointColor(i), Values = [12.125 + i, null, 32.75 + i] }).ToImmutableArray()
    };
    private static SlideShape Shape(ChartSpec chart) => ChartModel.Apply(new() { Name = "Chart", Bounds = new(0, 0, 640, 360) }, chart);
    private static PresentationDocument Deck(ChartSpec chart) => new() { Slides = [new() { Shapes = [Shape(chart)] }] };
    private static byte[] Export(ChartSpec chart) => PptxCodec.Export(Deck(chart)).Data;
    private static ChartSpec Import(byte[] bytes) => PptxCodec.Import(bytes).Document.Slides[0].Shapes.Single().Chart!;
    private static XElement Part(byte[] bytes, string path)
    {
        using var input = new MemoryStream(bytes); using var zip = new ZipArchive(input); using var part = zip.GetEntry(path)!.Open(); return XElement.Load(part);
    }
    private static byte[] Change(byte[] bytes, Action<XElement> action)
    {
        using var input = new MemoryStream(bytes); using var source = new ZipArchive(input); using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true)) foreach (var entry in source.Entries)
        {
            using var stream = entry.Open(); using var target = zip.CreateEntry(entry.FullName).Open();
            if (entry.FullName == "ppt/charts/chart1_2.xml") { var xml = XElement.Load(stream); action(xml); xml.Save(target); } else stream.CopyTo(target);
        }
        return output.ToArray();
    }
    public static IEnumerable<object[]> Types() => Enum.GetValues<ChartKind>().Select(t => new object[] { t });
    public static IEnumerable<object[]> Cartesian() => new[] { ChartKind.Column, ChartKind.Bar, ChartKind.Line, ChartKind.Area }.SelectMany(t => Enum.GetValues<ChartBlankMode>().Select(b => new object[] { t, b }));

    [Theory, MemberData(nameof(Types))] public void EveryChartTypePassesNativeAndWorkbookSchemas(ChartKind kind)
    {
        var data = Data(kind, ChartModel.IsCircular(kind) ? 1 : 2);
        using var input = new MemoryStream(Export(data)); using var deck = OpenXmlPresentation.Open(input, false);
        var validator = new OpenXmlValidator();
        var errors = validator.Validate(deck).Select(e => $"{e.Part?.Uri}: {e.Path?.XPath}: {e.Description}").ToArray();
        Assert.True(errors.Length == 0, string.Join('\n', errors));
        using var source = deck.PresentationPart!.SlideParts.Single().ChartParts.Single().EmbeddedPackagePart!.GetStream();
        using var copy = new MemoryStream(); source.CopyTo(copy); copy.Position = 0;
        using var workbook = OpenXmlSpreadsheet.Open(copy, false);
        Assert.Empty(validator.Validate(workbook));
        Assert.Empty(workbook.WorkbookPart!.WorksheetParts.Single().Worksheet.Descendants<DocumentFormat.OpenXml.Spreadsheet.CellFormula>());
    }
    [Theory, MemberData(nameof(Types))] public void TypeTitlesSeriesColorsAndMissingDataRoundTrip(ChartKind kind)
    {
        var original = Data(kind, ChartModel.IsCircular(kind) ? 1 : 3); var result = Import(Export(original));
        Assert.Equal(kind, result.Kind); Assert.Equal(original.Title, result.Title); Assert.True(result.ShowLegend); Assert.True(result.ShowValues);
        Assert.Equal(original.Categories.ToArray(), result.Categories.ToArray()); Assert.Equal(original.Series.Length, result.Series.Length);
        for (int i = 0; i < original.Series.Length; i++)
        {
            Assert.Equal(original.Series[i].Name, result.Series[i].Name);
            if (!ChartModel.IsCircular(kind)) Assert.Equal(original.Series[i].Color, result.Series[i].Color);
            Assert.Equal(original.Series[i].Values.ToArray(), result.Series[i].Values.ToArray());
        }
    }
    [Theory, MemberData(nameof(Cartesian))] public void BlankPoliciesSurviveRoundTrip(ChartKind kind, ChartBlankMode blanks)
    {
        var result = Import(Export(Data(kind) with { Blanks = blanks })); Assert.Equal(blanks, result.Blanks); Assert.Null(result.Series[1].Values[1]);
    }
    [Theory, InlineData(ChartKind.Column, ChartGrouping.Stacked), InlineData(ChartKind.Bar, ChartGrouping.Stacked), InlineData(ChartKind.Column, ChartGrouping.PercentStacked), InlineData(ChartKind.Bar, ChartGrouping.PercentStacked)]
    public void StackedModesRoundTripAndPassSchema(ChartKind kind, ChartGrouping grouping)
    {
        var bytes = Export(Data(kind) with { Grouping = grouping }); Assert.Equal(grouping, Import(bytes).Grouping);
        using var stream = new MemoryStream(bytes); using var deck = OpenXmlPresentation.Open(stream, false); Assert.Empty(new OpenXmlValidator().Validate(deck));
    }
    [Fact] public void NativeJsonUsesAFormatVersionOlderReadersReject()
    {
        string json = DocumentSerializer.Serialize(Deck(Data())); var restored = DocumentSerializer.Deserialize(json);
        Assert.Equal(2, restored.SchemaVersion); Assert.Equal(2, restored.Slides[0].Shapes[0].Chart!.Series.Length);
        Assert.Null(restored.Slides[0].Shapes[0].Chart!.Series[0].Values[1]);
        Assert.Equal(1, DocumentSerializer.Deserialize(DocumentSerializer.Serialize(new())).SchemaVersion);
    }
    [Fact] public void LegacyChartFieldsRemainReadable()
    {
        var legacy = new SlideShape { Kind = ShapeKind.Chart, Values = [-2, 0, 3], Labels = ["A", "B", "C"] };
        var data = ChartModel.Get(legacy); Assert.Equal(new double?[] { -2, 0, 3 }, data.Series[0].Values.ToArray()); Assert.Null(legacy.Chart);
    }
    [Fact] public void MissingPointsHaveNoWorkbookCellAndNoCachePoint()
    {
        var bytes = Export(Data()); var xml = Part(bytes, "ppt/charts/chart1_2.xml");
        Assert.All(xml.Descendants(C + "numCache"), cache => { Assert.Equal("3", (string?)cache.Element(C + "ptCount")!.Attribute("val")); Assert.DoesNotContain(cache.Elements(C + "pt"), p => (string?)p.Attribute("idx") == "1"); });
        using var stream = new MemoryStream(bytes); using var zip = new ZipArchive(stream); using var workbook = zip.GetEntry("ppt/embeddings/chart1_2.xlsx")!.Open(); using var copy = new MemoryStream(); workbook.CopyTo(copy);
        var cells = Part(copy.ToArray(), "xl/worksheets/sheet1.xml").Descendants(S + "c").Select(c => (string?)c.Attribute("r")).ToArray();
        Assert.DoesNotContain("B3", cells); Assert.DoesNotContain("C3", cells);
    }
    [Fact] public void AllMissingSeriesIsRetained()
    {
        var data = Data() with { Series = [new() { Values = [null, null, null] }] }; var result = Import(Export(data)); Assert.Equal(3, result.Series[0].Values.Length); Assert.All(result.Series[0].Values, v => Assert.Null(v));
    }
    [Fact] public void WorkbookAddressesWorkBeyondColumnZ()
    {
        var data = Data(series: 32); var bytes = Export(data); var xml = Part(bytes, "ppt/charts/chart1_2.xml");
        Assert.Contains(xml.Descendants(C + "f"), f => f.Value == "Data!$AG$2:$AG$4");
        using var input = new MemoryStream(bytes); using var deck = OpenXmlPresentation.Open(input, false); Assert.Empty(new OpenXmlValidator().Validate(deck));
        Assert.Equal(32, Import(bytes).Series.Length);
    }
    [Theory, InlineData("-1"), InlineData("1.5"), InlineData("NaN"), InlineData("1000000000")]
    public void MalformedCountsAreRejectedBeforeAllocation(string count)
    {
        var bytes = Change(Export(Data()), x => x.Descendants(C + "numCache").First().Element(C + "ptCount")!.SetAttributeValue("val", count)); Assert.Throws<InvalidDataException>(() => PptxCodec.Import(bytes));
    }
    [Fact] public void PointsOutsideDeclaredCountAreRejected()
    {
        var bytes = Change(Export(Data()), x => x.Descendants(C + "numCache").First().Element(C + "ptCount")!.SetAttributeValue("val", "1")); Assert.Throws<InvalidDataException>(() => PptxCodec.Import(bytes));
    }
    [Fact] public void ConflictingCategoriesDoNotSilentlyCollapseSeries()
    {
        var bytes = Change(Export(Data()), x => x.Descendants(C + "ser").Last().Element(C + "cat")!.Descendants(C + "v").First().Value = "Other"); var result = PptxCodec.Import(bytes);
        Assert.Empty(result.Document.Slides[0].Shapes); Assert.Contains(result.Warnings, w => w.Contains("conflicting"));
    }
    [Fact] public void CombinationChartsAreReportedRatherThanPartiallyImported()
    {
        var bytes = Change(Export(Data()), x => x.Descendants(C + "plotArea").Single().Add(new XElement(C + "pieChart"))); var result = PptxCodec.Import(bytes);
        Assert.Empty(result.Document.Slides[0].Shapes); Assert.Contains(result.Warnings, w => w.Contains("combination"));
    }
    [Fact] public void SeriesOrderIsRespectedEvenWhenXmlOrderDiffers()
    {
        var bytes = Change(Export(Data()), x => { var parent = x.Descendants(C + "barChart").Single(); var nodes = parent.Elements(C + "ser").ToArray(); nodes.Remove(); parent.AddFirst(nodes.Reverse()); });
        var result = Import(bytes); Assert.Equal(Data().Series[0].Name, result.Series[0].Name); Assert.Equal(Data().Series[1].Name, result.Series[1].Name);
    }
    [Fact] public void StacksAccumulatePositiveAndNegativeValuesSeparately()
    {
        var data = Data() with { Grouping = ChartGrouping.Stacked, Categories = ["A"], Series = [new() { Values = [5] }, new() { Values = [-2] }, new() { Values = [3] }, new() { Values = [-4] }] };
        var intervals = ChartModel.Intervals(data); Assert.Equal(new[] { (0d, 5d), (0d, -2d), (5d, 8d), (-2d, -6d) }, intervals.Select(i => (i.Start, i.End)).ToArray());
    }
    [Fact] public void PercentageStacksNormalizeWithoutNanOnEmptyCategories()
    {
        var data = Data() with { Grouping = ChartGrouping.PercentStacked, Categories = ["A", "B"], Series = [new() { Values = [1, 0] }, new() { Values = [3, 0] }] };
        var intervals = ChartModel.Intervals(data); Assert.Equal(.25, intervals[0].End); Assert.Equal(1, intervals[1].End); Assert.All(intervals, i => Assert.True(double.IsFinite(i.End)));
    }
    [Theory, InlineData(ChartKind.Pie), InlineData(ChartKind.Doughnut)] public void CircularConversionCannotSilentlyDiscardOtherSeries(ChartKind kind) => Assert.Throws<InvalidDataException>(() => ChartModel.Validate(Data(kind)));
    [Fact] public void NegativePercentageDataIsRejectedExplicitly()
    {
        var data = Data() with { Grouping = ChartGrouping.PercentStacked, Series = [new() { Values = [-1, 2, 3] }] }; Assert.Throws<InvalidDataException>(() => ChartModel.Validate(data));
    }
    [Theory, InlineData(double.NaN), InlineData(double.PositiveInfinity), InlineData(1e31)] public void InvalidValuesCannotEnterTheModel(double value)
    { Assert.Throws<InvalidDataException>(() => ChartModel.Validate(Data() with { Series = [new() { Values = [value, 1, 2] }] })); }
    [Fact] public void ChartEditsAreUndoableAndDoNotMutateOriginalData()
    {
        var data = Data(); var session = new EditorSession(Deck(data)); var id = session.CurrentSlide.Shapes[0].Id; session.Select(id);
        session.Apply("Change chart", shape => ChartModel.Apply(shape, data with { Kind = ChartKind.Line })); Assert.Equal(ChartKind.Line, session.PrimaryShape!.Chart!.Kind);
        session.Undo(); Assert.Same(data, session.PrimaryShape!.Chart); session.Redo(); Assert.Equal(ChartKind.Line, session.PrimaryShape!.Chart!.Kind);
    }
    [Fact] public void QuotedTabularDataRoundTripsLabelsNamesAndMissingCells()
    {
        var original = Data(); string text = ChartTabularData.Format(original); var result = ChartTabularData.Parse(text + "\r\n", original);
        Assert.Equal(original.Categories.ToArray(), result.Categories.ToArray()); Assert.Equal(original.Series[1].Name, result.Series[1].Name); Assert.Equal(original.Series[0].Values.ToArray(), result.Series[0].Values.ToArray());
    }
    [Theory, InlineData("Category\tA\nLabel\tNaN"), InlineData("Category\tA\nLabel\t=2+2"), InlineData("Category\tA\nLabel"), InlineData("Category\tA\n\"unclosed\t1"), InlineData("Category\tA\n\"Label\"x\t1")]
    public void InvalidTabularInputDoesNotPartiallyApply(string text) => Assert.Throws<InvalidDataException>(() => ChartTabularData.Parse(text, Data()));
    [Fact] public void TabularInputPreservesExistingColorsAndIsCultureIndependent()
    {
        var old = CultureInfo.CurrentCulture; try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pl-PL"); var result = ChartTabularData.Parse("Category\tA\tB\n=Literal\t1.5\t\nSecond\t2.75\t8", Data()); Assert.Equal(1.5, result.Series[0].Values[0]); Assert.Null(result.Series[1].Values[0]); Assert.Equal(Data().Series[1].Color, result.Series[1].Color); } finally { CultureInfo.CurrentCulture = old; }
    }
    [Fact] public void TotalValueBudgetIsEnforced()
    {
        var categories = Enumerable.Range(0, 10000).Select(i => i.ToString()).ToImmutableArray();
        var values = Enumerable.Repeat<double?>(1, 10000).ToImmutableArray();
        var data = Data() with { Categories = categories, Series = Enumerable.Range(0, 11).Select(i => new ChartSeries { Values = values }).ToImmutableArray() };
        Assert.Throws<InvalidDataException>(() => ChartModel.Validate(data));
    }
    [Theory, MemberData(nameof(Types))] public void EveryChartRendersVisiblePixelsAndDoesNotLeakTransforms(ChartKind kind)
    {
        using var surface = SKSurface.Create(new SKImageInfo(720, 440)); var canvas = surface.Canvas; canvas.Clear(SKColors.White); var matrix = canvas.TotalMatrix;
        using var renderer = new SlideRenderer(); renderer.RenderChart(canvas, Data(kind, ChartModel.IsCircular(kind) ? 1 : 2), new(40, 30, 640, 360), new());
        Assert.Equal(matrix, canvas.TotalMatrix); using var image = surface.Snapshot(); using var bitmap = SKBitmap.FromImage(image);
        int pixels = 0; for (int y = 30; y < 390; y += 3) for (int x = 40; x < 680; x += 3) if (bitmap.GetPixel(x, y) != SKColors.White) pixels++;
        Assert.True(pixels > 200, $"{kind} produced only {pixels} painted samples."); Assert.Equal(SKColors.White, bitmap.GetPixel(1, 1));
    }
    [Fact] public void DifferentChartTypesProduceDifferentPictures()
    {
        using var renderer = new SlideRenderer(); var outputs = Enum.GetValues<ChartKind>().Select(kind => Convert.ToBase64String(renderer.ExportPng(Deck(Data(kind, 1)), Deck(Data(kind, 1)).Slides[0], 640))).ToArray(); Assert.Equal(6, outputs.Distinct().Count());
    }
}
