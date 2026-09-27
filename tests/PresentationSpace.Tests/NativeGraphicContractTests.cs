using System.Collections.Immutable;
using System.IO.Compression;
using System.Xml.Linq;
using PresentationSpace.Core;
using PresentationSpace.Formats;
using Xunit;

namespace PresentationSpace.Tests;

public class NativeGraphicContractTests
{
    // a:graphicData/@uri is an application dispatch identifier, not the a:tbl XML namespace.
    // Structural schema validation alone does not enforce that identifier-to-content contract.
    [Theory]
    [InlineData(ShapeKind.Table, "http://schemas.openxmlformats.org/drawingml/2006/table", "tbl")]
    [InlineData(ShapeKind.Chart, "http://schemas.openxmlformats.org/drawingml/2006/chart", "chart")]
    public void ExportDeclaresTheNativeConsumerGraphicType(ShapeKind kind, string uri, string child)
    {
        var shape = new SlideShape { Kind = kind, Cells = ["A", "B", "C"], Values = [1, 2, 3], Labels = ["A", "B", "C"] };
        var document = new PresentationDocument { Slides = [new() { Shapes = [shape] }] };
        using var bytes = new MemoryStream(PptxCodec.Export(document).Data);
        using var zip = new ZipArchive(bytes);
        using var stream = zip.GetEntry("ppt/slides/slide1.xml")!.Open();
        var slide = XElement.Load(stream);
        XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
        var data = Assert.Single(slide.Descendants(a + "graphicData"));
        Assert.Equal(uri, (string?)data.Attribute("uri"));
        Assert.Equal(child, Assert.Single(data.Elements()).Name.LocalName);
    }
}
