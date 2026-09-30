using System.Collections.Immutable;
using System.IO.Compression;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Validation;
using PresentationSpace.Core;
using PresentationSpace.Formats;
using PresentationSpace.Rendering.Skia;
using Xunit;
using OpenXmlPresentation = DocumentFormat.OpenXml.Packaging.PresentationDocument;

namespace PresentationSpace.Tests;

public sealed class CustomTabInterchangeTests
{
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace P = "http://schemas.openxmlformats.org/presentationml/2006/main";
    private static readonly ImmutableArray<TextTabStop> Stops = [new(100), new(200, TextTabAlignment.Center), new(320, TextTabAlignment.Right), new(480, TextTabAlignment.Decimal)];
    private static PresentationDocument Deck(SlideShape shape) => new() { Slides = [new() { Shapes = [shape] }] };
    private static byte[] Rewrite(byte[] data, Action<XDocument> change)
    {
        using var memory = new MemoryStream(); memory.Write(data);
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Update, true))
        {
            var entry = zip.GetEntry("ppt/slides/slide1.xml")!; XDocument xml;
            using (var stream = entry.Open()) xml = XDocument.Load(stream);
            change(xml); entry.Delete(); using var output = zip.CreateEntry("ppt/slides/slide1.xml").Open(); xml.Save(output);
        }
        return memory.ToArray();
    }
    private static byte[] Basic() => PptxCodec.Export(Deck(SlideFactory.Text("a\tb\tc\td\te", 20, 20, 700, 140) with { TextStyle = new() { TabStops = Stops } })).Data;
    private static void Validate(byte[] data)
    {
        using var stream = new MemoryStream(data); using var pptx = OpenXmlPresentation.Open(stream, false);
        var errors = new OpenXmlValidator().Validate(pptx).Take(10).ToArray();
        Assert.True(errors.Length == 0, string.Join("\n", errors.Select(e => e.Description + " " + e.Path?.XPath)));
    }
    [Fact] public void FourTabTypesAreNativeAndSchemaValid()
    {
        var data = Basic(); Validate(data);
        using var zip = new ZipArchive(new MemoryStream(data)); using var stream = zip.GetEntry("ppt/slides/slide1.xml")!.Open(); var xml = XDocument.Load(stream);
        var values = xml.Descendants(A + "tab").ToArray();
        Assert.Equal(new[] { "l", "ctr", "r", "dec" }, values.Select(n => (string?)n.Attribute("algn")));
        TabAssert.Equal(Stops, PptxCodec.Import(data).Document.Slides[0].Shapes[0].TextStyle.TabStops);
    }
    [Fact] public void ParagraphsCanClearCustomStopsIndependently()
    {
        var shape = SlideFactory.Text("one\ttwo\nthree\tfour", 0, 0, 600, 300);
        shape = RichTextEditing.FormatParagraphs(shape, 1, 0, s => s with { TabStops = Stops });
        var data = PptxCodec.Export(Deck(shape)).Data; Validate(data);
        var next = PptxCodec.Import(data).Document.Slides[0].Shapes[0];
        TabAssert.Equal(Stops, RichText.StyleAt(next, 0).TabStops); Assert.Empty(RichText.StyleAt(next, 8).TabStops);
    }
    [Fact] public void TableCellsPreserveNativeStopsAndCharacterStyles()
    {
        var table = TableModel.Create(1, 1) with { HeaderRow = false, TextStyle = new() { TabStops = Stops, FontSize = 18 } };
        table = TableModel.SetText(table, 0, 0, "a\tb\tc");
        var cell = table.Cells[0]; table = table with { Cells = [cell with { TextRanges = [new(2, 1, table.TextStyle with { Bold = true })] }] };
        var data = PptxCodec.Export(Deck(TableModel.Apply(new SlideShape { Bounds = new(0, 0, 600, 200) }, table))).Data; Validate(data);
        var next = PptxCodec.Import(data).Document.Slides[0].Shapes[0].Table!;
        TabAssert.Equal(Stops, TableModel.Style(next, next.Cells[0]).TabStops);
        Assert.Contains(next.Cells[0].TextRanges, r => r.Style.Bold);
    }
    [Fact] public void BodyDefaultStopsApplyWithoutFirstParagraphLeakingToSiblings()
    {
        var data = PptxCodec.Export(Deck(SlideFactory.Text("one\ttwo\nthree\tfour", 0, 0, 600, 200))).Data;
        data = Rewrite(data, xml =>
        {
            var body = xml.Descendants(P + "txBody").First();
            body.Element(A + "lstStyle")!.Add(new XElement(A + "defPPr", new XElement(A + "tabLst", new XElement(A + "tab", new XAttribute("pos", 300 * 9525), new XAttribute("algn", "r")))));
            body.Elements(A + "p").Last().Element(A + "pPr")!.Element(A + "tabLst")!.Remove();
        });
        var next = PptxCodec.Import(data).Document.Slides[0].Shapes[0];
        Assert.Empty(RichText.StyleAt(next, 0).TabStops);
        var stop = Assert.Single(RichText.StyleAt(next, 8).TabStops); Assert.Equal(300, stop.Position); Assert.Equal(TextTabAlignment.Right, stop.Alignment);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void LevelOneDefaultsOverrideBodyDefaultsIncludingExplicitClears(bool clear)
    {
        var data = Rewrite(Basic(), xml =>
        {
            var body = xml.Descendants(P + "txBody").First();
            body.Element(A + "lstStyle")!.Add(
                new XElement(A + "defPPr", new XElement(A + "tabLst", new XElement(A + "tab", new XAttribute("pos", 300 * 9525), new XAttribute("algn", "r")))),
                new XElement(A + "lvl1pPr", new XElement(A + "tabLst", clear ? null : new XElement(A + "tab", new XAttribute("pos", 150 * 9525), new XAttribute("algn", "ctr")))));
            body.Elements(A + "p").First().Element(A + "pPr")!.Element(A + "tabLst")!.Remove();
        });
        Validate(data);
        var tabs = PptxCodec.Import(data).Document.Slides[0].Shapes[0].TextStyle.TabStops;
        if (clear) Assert.Empty(tabs);
        else { var stop = Assert.Single(tabs); Assert.Equal(150, stop.Position); Assert.Equal(TextTabAlignment.Center, stop.Alignment); }
    }
    [Fact] public void TableCellReadsBodyDefaultStops()
    {
        var table = TableModel.SetText(TableModel.Create(1, 1) with { HeaderRow = false }, 0, 0, "a\tb");
        var data = PptxCodec.Export(Deck(TableModel.Apply(new(), table))).Data;
        data = Rewrite(data, xml =>
        {
            var body = xml.Descendants(A + "tc").First().Element(A + "txBody")!;
            body.Element(A + "lstStyle")!.Add(new XElement(A + "defPPr", new XElement(A + "tabLst",
                new XElement(A + "tab", new XAttribute("pos", 200 * 9525), new XAttribute("algn", "dec")))));
            body.Element(A + "p")!.Element(A + "pPr")!.Element(A + "tabLst")!.Remove();
        });
        Validate(data);
        var imported = PptxCodec.Import(data).Document.Slides[0].Shapes[0].Table!;
        var stop = Assert.Single(TableModel.Style(imported, imported.Cells[0]).TabStops);
        Assert.Equal(200, stop.Position); Assert.Equal(TextTabAlignment.Decimal, stop.Alignment);
    }
    [Theory]
    [InlineData("pos", "NaN")] [InlineData("pos", "-1")] [InlineData("pos", "100000000000")]
    [InlineData("algn", "bogus")]
    public void MalformedNativeTabsAreRejected(string attribute, string value)
    {
        var data = Rewrite(Basic(), xml => xml.Descendants(A + "tab").First().SetAttributeValue(attribute, value));
        Assert.Throws<InvalidDataException>(() => PptxCodec.Import(data));
    }
    [Fact] public void UnorderedDuplicateOversizedAndMissingPositionsAreRejected()
    {
        foreach (Action<XDocument> change in new Action<XDocument>[] {
            xml => xml.Descendants(A + "tab").First().Attribute("pos")!.Remove(),
            xml => xml.Descendants(A + "tab").First().SetAttributeValue("pos", 250 * 9525),
            xml => xml.Descendants(A + "tab").Skip(1).First().SetAttributeValue("pos", 100 * 9525),
            xml => xml.Descendants(A + "tabLst").First().ReplaceNodes(Enumerable.Range(0, 33).Select(i => new XElement(A + "tab", new XAttribute("pos", i * 9525)))) })
            Assert.Throws<InvalidDataException>(() => PptxCodec.Import(Rewrite(Basic(), change)));
    }
    [Fact] public void SampleRendersAndExportsWithRealTextRatherThanFlattening()
    {
        var document = TabLayoutSample.Create(); DocumentSerializer.Validate(document);
        var pptx = PptxCodec.Export(document).Data; Validate(pptx);
        var imported = PptxCodec.Import(pptx).Document;
        Assert.Contains(imported.Slides[0].Shapes, s => s.Text.Contains('\t') && s.TextStyle.TabStops.Length == 3);
        using var renderer = new SlideRenderer();
        byte[] png = renderer.ExportPng(document, document.Slides[0], 1600);
        byte[] pdf = renderer.ExportPdf(document);
        Assert.True(png.Length > 5000); Assert.True(pdf.Length > 1000);
        if (Environment.GetEnvironmentVariable("RENDER_DIAGNOSTICS") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, "custom-tabs.png"), png);
            File.WriteAllBytes(Path.Combine(directory, "custom-tabs.pptx"), pptx);
            File.WriteAllBytes(Path.Combine(directory, "custom-tabs.pdf"), pdf);
        }
    }
}
