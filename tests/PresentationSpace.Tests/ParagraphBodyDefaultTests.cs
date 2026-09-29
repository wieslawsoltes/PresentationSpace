using System.IO.Compression;
using System.Xml.Linq;
using PresentationSpace.Core;
using PresentationSpace.Formats;
using Xunit;

namespace PresentationSpace.Tests;

public sealed class ParagraphBodyDefaultTests
{
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace P = "http://schemas.openxmlformats.org/presentationml/2006/main";

    private static SlideShape Import(Action<XElement> change)
    {
        var document = new PresentationDocument { Slides = [new() { Shapes = [SlideFactory.Text("first\nsecond", 10, 20, 600, 300, 24)] }] };
        using var stream = new MemoryStream();
        stream.Write(PptxCodec.Export(document).Data);
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Update, true))
        {
            var entry = zip.GetEntry("ppt/slides/slide1.xml")!;
            XDocument xml;
            using (var input = entry.Open()) xml = XDocument.Load(input);
            var body = xml.Descendants(P + "txBody").Single();
            // Test real sparse DrawingML, not only our fully explicit export.
            foreach (var paragraph in body.Elements(A + "p"))
            {
                paragraph.Element(A + "pPr")?.Remove();
                paragraph.Element(A + "endParaRPr")?.Remove();
                foreach (var run in paragraph.Elements(A + "r")) run.Element(A + "rPr")?.Remove();
            }
            change(body);
            entry.Delete();
            using var output = zip.CreateEntry("ppt/slides/slide1.xml").Open(); xml.Save(output);
        }
        return PptxCodec.Import(stream.ToArray()).Document.Slides[0].Shapes.Single();
    }

    [Theory]
    [InlineData("r", ParagraphAlignment.Right)]
    [InlineData("ctr", ParagraphAlignment.Center)]
    public void FirstParagraphAlignmentAndBulletDoNotLeak(string alignment, ParagraphAlignment expected)
    {
        var shape = Import(body => body.Element(A + "p")!.AddFirst(new XElement(A + "pPr",
            new XAttribute("algn", alignment), new XElement(A + "buChar", new XAttribute("char", "•")))));
        var first = RichText.StyleAt(shape, 0); var second = RichText.StyleAt(shape, 6);
        Assert.Equal(expected, first.Alignment); Assert.True(first.Bullets);
        Assert.Equal(ParagraphAlignment.Left, second.Alignment); Assert.False(second.Bullets);
    }

    [Fact]
    public void ParagraphRunDefaultsDoNotBecomeBodyDefaults()
    {
        var shape = Import(body => body.Element(A + "p")!.AddFirst(new XElement(A + "pPr",
            new XElement(A + "defRPr", new XAttribute("sz", "3900"), new XAttribute("b", "1"),
                new XElement(A + "solidFill", new XElement(A + "srgbClr", new XAttribute("val", "CC2020")))))));
        var first = RichText.StyleAt(shape, 0); var second = RichText.StyleAt(shape, 6);
        Assert.Equal(52, first.FontSize); Assert.True(first.Bold); Assert.Equal("#CC2020", first.Color);
        Assert.Equal(28, second.FontSize); Assert.False(second.Bold); Assert.Equal("#243247", second.Color);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BodyLevelDefaultsApplyIndependentlyToSiblings(bool overrideFirst)
    {
        var shape = Import(body =>
        {
            body.Element(A + "lstStyle")!.Add(new XElement(A + "lvl1pPr", new XAttribute("algn", "r"),
                new XAttribute("marL", 22 * 9525), new XElement(A + "spcBef", new XElement(A + "spcPts", new XAttribute("val", 9 * 75))),
                new XElement(A + "buChar", new XAttribute("char", "•")),
                new XElement(A + "defRPr", new XAttribute("sz", 36 * 75), new XAttribute("b", "1"))));
            if (overrideFirst) body.Element(A + "p")!.AddFirst(new XElement(A + "pPr", new XAttribute("algn", "l"),
                new XElement(A + "buNone"), new XElement(A + "defRPr", new XAttribute("sz", 48 * 75), new XAttribute("b", "0"))));
        });
        var first = RichText.StyleAt(shape, 0); var second = RichText.StyleAt(shape, 6);
        Assert.Equal(ParagraphAlignment.Right, second.Alignment); Assert.True(second.Bullets);
        Assert.Equal(22, second.ParagraphLeftMargin); Assert.Equal(9, second.SpaceBefore);
        Assert.Equal(36, second.FontSize); Assert.True(second.Bold);
        Assert.Equal(overrideFirst ? ParagraphAlignment.Left : ParagraphAlignment.Right, first.Alignment);
        Assert.Equal(!overrideFirst, first.Bullets); Assert.Equal(overrideFirst ? 48 : 36, first.FontSize);
        Assert.Equal(!overrideFirst, first.Bold);
    }
}
