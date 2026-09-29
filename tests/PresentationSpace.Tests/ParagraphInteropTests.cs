using System.IO.Compression;
using System.Xml.Linq;
using PresentationSpace.Core;
using PresentationSpace.Formats;
using Xunit;

namespace PresentationSpace.Tests;

public sealed class ParagraphInteropTests
{
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace P = "http://schemas.openxmlformats.org/presentationml/2006/main";
    private static PresentationDocument Deck(SlideShape shape) => new() { Slides = [new() { LayoutName = "Title only", Shapes = [shape] }] };
    private static byte[] Rewrite(byte[] data, Action<ZipArchive> change)
    {
        using var memory = new MemoryStream(); memory.Write(data);
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Update, true)) change(zip);
        return memory.ToArray();
    }
    private static void Xml(ZipArchive zip, string name, Action<XDocument> change)
    {
        var entry = zip.GetEntry(name)!; XDocument doc;
        using (var read = entry.Open()) doc = XDocument.Load(read);
        change(doc); entry.Delete(); using var write = zip.CreateEntry(name).Open(); doc.Save(write);
    }
    [Fact] public void FirstParagraphIndentDoesNotBecomeTheDefaultOfItsSiblings()
    {
        var shape = SlideFactory.Text("first\nsecond", 20, 20, 400, 200, 24);
        shape = RichText.Format(shape, 0, 5, s => s with { ParagraphLeftMargin = 48, ParagraphRightMargin = 20, ParagraphIndent = -15, DefaultTabSize = 100 });
        var copy = PptxCodec.Import(PptxCodec.Export(Deck(shape)).Data).Document.Slides[0].Shapes[0];
        Assert.Equal(48, RichText.StyleAt(copy, 0).ParagraphLeftMargin);
        var second = RichText.StyleAt(copy, 6);
        Assert.Null(second.ParagraphLeftMargin); Assert.Null(second.ParagraphIndent);
        Assert.Equal(0, second.ParagraphRightMargin); Assert.Equal(0, second.DefaultTabSize);
    }
    [Fact] public void TableParagraphsHaveIndependentDefaults()
    {
        var table = TableModel.SetText(TableModel.Create(1, 1) with { HeaderRow = false }, 0, 0, "first\nsecond");
        var cell = table.Cells[0];
        var shape = new SlideShape { Text = cell.Text, TextStyle = TableModel.Style(table, cell), TextRanges = cell.TextRanges };
        shape = RichText.Format(shape, 0, 5, s => s with { ParagraphLeftMargin = 38, DefaultTabSize = 90 });
        table = table with { Cells = [cell with { TextRanges = shape.TextRanges }] };
        var copy = PptxCodec.Import(PptxCodec.Export(Deck(TableModel.Apply(new(), table))).Data).Document.Slides[0].Shapes[0].Table!;
        var first = copy.Cells[0]; var content = new SlideShape { Text = first.Text, TextStyle = TableModel.Style(copy, first), TextRanges = first.TextRanges };
        Assert.Equal(38, RichText.StyleAt(content, 0).ParagraphLeftMargin);
        Assert.Null(RichText.StyleAt(content, 6).ParagraphLeftMargin); Assert.Equal(0, RichText.StyleAt(content, 6).DefaultTabSize);
    }
    [Fact] public void BodyAttributesInheritIndividuallyFromTheMatchingLayout()
    {
        var shape = SlideFactory.Text("Placeholder", 20, 20, 400, 120, 24) with { Placeholder = PlaceholderKind.Title, TextBox = new() { MarginRight = 13 } };
        var data = Rewrite(PptxCodec.Export(Deck(shape)).Data, z =>
        {
            foreach (var name in z.Entries.Where(e => e.FullName.StartsWith("ppt/slideLayouts/slideLayout") && e.FullName.EndsWith(".xml")).Select(e => e.FullName).ToArray())
                Xml(z, name, d => { foreach (var body in d.Descendants(A + "bodyPr")) { body.SetAttributeValue("lIns", 27 * 9525); body.SetAttributeValue("rIns", 41 * 9525); body.SetAttributeValue("tIns", 19 * 9525); body.SetAttributeValue("wrap", "none"); } });
            Xml(z, "ppt/slides/slide1.xml", d => { var body = d.Descendants(A + "bodyPr").First(); body.Attribute("lIns")?.Remove(); body.Attribute("tIns")?.Remove(); body.Attribute("wrap")?.Remove(); });
        });
        var box = PptxCodec.Import(data).Document.Slides[0].Shapes[0].TextBox!;
        Assert.Equal(27, box.MarginLeft); Assert.Equal(19, box.MarginTop); Assert.Equal(13, box.MarginRight); Assert.False(box.Wrap);
    }
    [Theory] [InlineData("marL", "-1")] [InlineData("marR", "NaN")] [InlineData("indent", "Infinity")] [InlineData("defTabSz", "999999999999")]
    public void MalformedParagraphCoordinatesFailBeforeLayout(string key, string value)
    {
        var shape = SlideFactory.Text("Text", 0, 0, 400, 200);
        var data = Rewrite(PptxCodec.Export(Deck(shape)).Data, z => Xml(z, "ppt/slides/slide1.xml", d => d.Descendants(A + "pPr").First().SetAttributeValue(key, value)));
        Assert.Throws<InvalidDataException>(() => PptxCodec.Import(data));
    }
    [Theory] [InlineData("spcBef")] [InlineData("spcAft")] [InlineData("lnSpc")]
    public void NegativeParagraphSpacingIsRejected(string element)
    {
        var data = Rewrite(PptxCodec.Export(Deck(SlideFactory.Text("Text", 0, 0, 400, 200))).Data,
            z => Xml(z, "ppt/slides/slide1.xml", d => d.Descendants(A + element).First().ReplaceNodes(new XElement(A + "spcPts", new XAttribute("val", "-50")))));
        Assert.Throws<InvalidDataException>(() => PptxCodec.Import(data));
    }
    [Fact] public void UnsupportedBodyModesProduceWarnings()
    {
        var data = Rewrite(PptxCodec.Export(Deck(SlideFactory.Text("Text", 0, 0, 400, 200))).Data, z => Xml(z, "ppt/slides/slide1.xml", d =>
        {
            var body = d.Descendants(A + "bodyPr").First(); body.SetAttributeValue("vert", "vert"); body.SetAttributeValue("numCol", "2"); body.Add(new XElement(A + "normAutofit"));
        }));
        var result = PptxCodec.Import(data);
        Assert.Contains(result.Warnings, w => w.Contains("Vertical")); Assert.Contains(result.Warnings, w => w.Contains("column")); Assert.Contains(result.Warnings, w => w.Contains("autofit"));
    }
}
