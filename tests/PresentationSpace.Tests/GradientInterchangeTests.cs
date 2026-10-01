using System.Collections.Immutable;
using System.IO.Compression;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Validation;
using PresentationSpace.Core;
using PresentationSpace.Formats;
using Xunit;

namespace PresentationSpace.Tests;

public sealed class GradientInterchangeTests
{
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main", P = "http://schemas.openxmlformats.org/presentationml/2006/main", R = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static GradientFill Example => new() { Angle = 32.25f, Scaled = false, RotateWithShape = false, Stops = [new(0, "#204060", .3f), new(.4f, "#A04060", .9f), new(1, "#F0E0D0")] };
    private static PresentationDocument Deck() => new() { Slides = [new() { LayoutName = "Title only", BackgroundGradient = Example,
        Shapes = [new() { Name = "Gradient", FillGradient = Example, Placeholder = PlaceholderKind.Title, PlaceholderIndex = 0 }] }] };
    private static byte[] Rewrite(byte[] data, Action<ZipArchive> change)
    {
        using var stream = new MemoryStream(); stream.Write(data);
        using (var z = new ZipArchive(stream, ZipArchiveMode.Update, true)) change(z);
        return stream.ToArray();
    }
    private static XElement Read(ZipArchive z, string path) { using var s = z.GetEntry(path)!.Open(); return XElement.Load(s); }
    private static void Set(ZipArchive z, string path, XElement xml) { z.GetEntry(path)?.Delete(); using var s = z.CreateEntry(path).Open(); xml.Save(s); }
    private static void Xml(ZipArchive z, string path, Action<XElement> change) { var xml = Read(z, path); change(xml); Set(z, path, xml); }
    private static byte[] Change(Action<XElement> change) => Rewrite(PptxCodec.Export(Deck()).Data, z => Xml(z, "ppt/slides/slide1.xml", change));
    private static XElement ShapeGradient(XElement x) => x.Descendants(P + "spPr").First().Element(A + "gradFill")!;
    private static SlideShape Imported(byte[] b) => PptxCodec.Import(b).Document.Slides[0].Shapes[0];
    private static void Same(GradientFill a, GradientFill b)
    {
        Assert.Equal(a.Angle, b.Angle); Assert.Equal(a.Scaled, b.Scaled); Assert.Equal(a.RotateWithShape, b.RotateWithShape);
        Assert.Equal(a.Stops.Length, b.Stops.Length);
        foreach (var (x, y) in a.Stops.Zip(b.Stops)) { Assert.Equal(x.Color, y.Color); Assert.InRange(Math.Abs(x.Offset - y.Offset), 0, .000011); Assert.InRange(Math.Abs(x.Opacity - y.Opacity), 0, .000011); }
    }
    [Fact] public void NativeGradientShapesBackgroundsAndCellsValidateAndRoundTrip()
    {
        var d = GradientSample.Create(); var bytes = PptxCodec.Export(d).Data;
        using (var s = new MemoryStream(bytes)) using (var p = DocumentFormat.OpenXml.Packaging.PresentationDocument.Open(s, false))
            Assert.Empty(new OpenXmlValidator().Validate(p));
        var copy = PptxCodec.Import(bytes).Document;
        Same(d.Slides[0].BackgroundGradient!, copy.Slides[0].BackgroundGradient!);
        foreach (var (a, b) in d.Slides[0].Shapes.Zip(copy.Slides[0].Shapes))
        {
            if (a.FillGradient is not null) Same(a.FillGradient, b.FillGradient!);
            if (a.Table is not null) foreach (var (c, e) in a.Table.Cells.Zip(b.Table!.Cells)) Same(c.FillGradient!, e.FillGradient!);
        }
        using var zip = new ZipArchive(new MemoryStream(bytes)); Assert.Empty(zip.Entries.Where(e => e.FullName.StartsWith("ppt/media/")));
    }
    [Fact] public void ExplicitAngleScalingRotationAndTransparentStopsSurvive()
    { var original = Deck(); var copy = PptxCodec.Import(PptxCodec.Export(original).Data).Document; Same(Example, copy.Slides[0].Shapes[0].FillGradient!); }
    [Fact] public void ShapeOpacityIsAppliedToStopsExactlyOnce()
    {
        var d = Deck(); d = d with { Slides = [d.Slides[0] with { Shapes = [d.Slides[0].Shapes[0] with { Opacity = .5f }] }] };
        var copy = Imported(PptxCodec.Export(d).Data); Assert.Equal(1, copy.Opacity);
        Assert.InRange(Math.Abs(copy.FillGradient!.Stops[0].Opacity - .15f), 0, .00001);
    }
    [Theory] [InlineData("-1")] [InlineData("100001")] [InlineData("NaN")] [InlineData("Infinity")]
    public void InvalidPositionsAreRejected(string value) => Assert.Throws<InvalidDataException>(() => Imported(Change(x => ShapeGradient(x).Descendants(A + "gs").First().SetAttributeValue("pos", value))));
    [Theory] [InlineData("-1")] [InlineData("21600000")] [InlineData("4.2")] [InlineData("Infinity")]
    public void InvalidAnglesAreRejected(string value) => Assert.Throws<InvalidDataException>(() => Imported(Change(x => ShapeGradient(x).Element(A + "lin")!.SetAttributeValue("ang", value))));
    [Theory] [InlineData("scaled")] [InlineData("rotWithShape")]
    public void InvalidFlagsAreRejected(string name) => Assert.Throws<InvalidDataException>(() => Imported(Change(x =>
        (name == "scaled" ? ShapeGradient(x).Element(A + "lin")! : ShapeGradient(x)).SetAttributeValue(name, "maybe"))));
    [Fact] public void DescendingAndMissingStopsFailWithoutPartialImports()
    {
        Assert.Throws<InvalidDataException>(() => Imported(Change(x => ShapeGradient(x).Descendants(A + "gs").First().SetAttributeValue("pos", "50000"))));
        Assert.Throws<InvalidDataException>(() => Imported(Change(x => ShapeGradient(x).Element(A + "gsLst")!.Remove())));
        Assert.Throws<InvalidDataException>(() => Imported(Change(x => ShapeGradient(x).Descendants(A + "gs").First().RemoveNodes())));
        Assert.Throws<InvalidDataException>(() => Imported(Change(x => ShapeGradient(x).Element(A + "gsLst")!.Add(Enumerable.Range(0, 65).Select(_ => new XElement(A + "gs", new XAttribute("pos", "100000"), new XElement(A + "srgbClr", new XAttribute("val", "FFFFFF"))))))));
    }
    [Fact] public void DuplicateInteriorStopsAndPercentLexicalValuesArePreserved()
    {
        var data = Change(x =>
        {
            var stops = ShapeGradient(x).Element(A + "gsLst")!; stops.Elements().First().SetAttributeValue("pos", "0%");
            var mid = stops.Elements().ElementAt(1); mid.AddAfterSelf(new XElement(mid));
        });
        Assert.Equal(4, Imported(data).FillGradient!.Stops.Length);
    }
    [Theory] [InlineData("path")] [InlineData("tileRect")]
    public void UnsupportedGradientGeometryIsDiagnosed(string element)
    {
        var result = PptxCodec.Import(Change(x => ShapeGradient(x).Add(new XElement(A + element, new XAttribute(element == "path" ? "path" : "r", element == "path" ? "circle" : "50000")))));
        Assert.Null(result.Document.Slides[0].Shapes[0].FillGradient); Assert.Contains(result.Warnings, w => w.Contains("Path/radial"));
    }
    [Fact] public void ShapeAndBackgroundFillsInheritFromMatchingLayoutButExplicitNoneWins()
    {
        var source = PptxCodec.Export(Deck()).Data;
        var inherited = Rewrite(source, z =>
        {
            var slide = Read(z, "ppt/slides/slide1.xml"); var fill = new XElement(ShapeGradient(slide));
            foreach (var path in z.Entries.Select(e => e.FullName).Where(p => p.StartsWith("ppt/slideLayouts/slideLayout") && p.EndsWith(".xml")).ToArray())
                Xml(z, path, x =>
                {
                    foreach (var props in x.Descendants(P + "spPr")) { props.Elements().Where(e => e.Name == A + "noFill" || e.Name == A + "solidFill").Remove(); props.Add(new XElement(fill)); }
                    x.Element(P + "cSld")!.AddFirst(new XElement(P + "bg", new XElement(P + "bgPr", new XElement(fill))));
                });
            slide.Element(P + "cSld")!.Element(P + "bg")!.Remove(); ShapeGradient(slide).Remove(); Set(z, "ppt/slides/slide1.xml", slide);
        });
        var d = PptxCodec.Import(inherited).Document; Same(Example, d.Slides[0].Shapes[0].FillGradient!); Same(Example, d.Slides[0].BackgroundGradient!);
        var cleared = Rewrite(inherited, z => Xml(z, "ppt/slides/slide1.xml", x =>
        {
            x.Descendants(P + "spPr").First().Add(new XElement(A + "noFill"));
            x.Element(P + "cSld")!.AddFirst(new XElement(P + "bg", new XElement(P + "bgPr", new XElement(A + "noFill"))));
        }));
        var next = PptxCodec.Import(cleared).Document; Assert.Null(next.Slides[0].Shapes[0].FillGradient); Assert.Null(next.Slides[0].BackgroundGradient); Assert.Equal("#00000000", next.Slides[0].Background);
    }
    [Fact] public void ThemeFillReferencesResolvePlaceholderColorAndOrderedTransforms()
    {
        var data = Rewrite(PptxCodec.Export(Deck()).Data, z =>
        {
            var slide = Read(z, "ppt/slides/slide1.xml"); var fill = new XElement(ShapeGradient(slide));
            fill.Element(A + "gsLst")!.Elements().First().ReplaceNodes(new XElement(A + "schemeClr", new XAttribute("val", "phClr"),
                new XElement(A + "lumMod", new XAttribute("val", 50000)), new XElement(A + "lumOff", new XAttribute("val", 50000)), new XElement(A + "alpha", new XAttribute("val", 25000))));
            Xml(z, "ppt/theme/theme1.xml", x => x.Descendants(A + "fillStyleLst").First().Elements().First().ReplaceWith(fill));
            ShapeGradient(slide).Remove(); var shape = slide.Descendants(P + "sp").First();
            shape.Add(new XElement(P + "style", new XElement(A + "fillRef", new XAttribute("idx", 1), new XElement(A + "srgbClr", new XAttribute("val", "FF0000")))));
            Set(z, "ppt/slides/slide1.xml", slide);
        });
        var stop = Imported(data).FillGradient!.Stops[0]; Assert.Equal("#FF8080", stop.Color); Assert.Equal(.25f, stop.Opacity);
    }
    [Fact] public void PerMasterThemeRelationshipsOverrideArchiveOrder()
    {
        var a = Deck().Slides[0] with { LayoutName = "Blank", BackgroundGradient = null };
        var b = a with { Id = Guid.NewGuid(), LayoutName = "Title only", Shapes = [a.Shapes[0] with { Id = Guid.NewGuid() }] };
        var data = Rewrite(PptxCodec.Export(new() { Slides = [a, b] }).Data, z =>
        {
            foreach (int i in new[] { 1, 2 }) Xml(z, $"ppt/slides/slide{i}.xml", x => ShapeGradient(x).Descendants(A + "gs").First().ReplaceNodes(new XElement(A + "schemeClr", new XAttribute("val", "accent1"))));
            Xml(z, "ppt/theme/theme1.xml", x => x.Descendants(A + "clrScheme").First().Element(A + "accent1")!.ReplaceNodes(new XElement(A + "srgbClr", new XAttribute("val", "FF0000"))));
            var theme2 = Read(z, "ppt/theme/theme1.xml"); theme2.Descendants(A + "clrScheme").First().Element(A + "accent1")!.ReplaceNodes(new XElement(A + "srgbClr", new XAttribute("val", "0000FF"))); Set(z, "ppt/theme/theme2.xml", theme2);
            Set(z, "ppt/slideMasters/slideMaster2.xml", Read(z, "ppt/slideMasters/slideMaster1.xml"));
            var rels = Read(z, "ppt/slideMasters/_rels/slideMaster1.xml.rels");
            rels.Elements().First(e => ((string?)e.Attribute("Type"))!.EndsWith("/theme")).SetAttributeValue("Target", "../theme/theme2.xml"); Set(z, "ppt/slideMasters/_rels/slideMaster2.xml.rels", rels);
            var slideRels = Read(z, "ppt/slides/_rels/slide2.xml.rels"); string layout = Path.GetFileName((string)slideRels.Elements().First(e => ((string?)e.Attribute("Type"))!.EndsWith("/slideLayout")).Attribute("Target")!);
            Xml(z, "ppt/slideLayouts/_rels/" + layout + ".rels", x => x.Elements().First(e => ((string?)e.Attribute("Type"))!.EndsWith("/slideMaster")).SetAttributeValue("Target", "../slideMasters/slideMaster2.xml"));
        });
        var copy = PptxCodec.Import(data).Document; Assert.Equal("#FF0000", copy.Slides[0].Shapes[0].FillGradient!.Stops[0].Color); Assert.Equal("#0000FF", copy.Slides[1].Shapes[0].FillGradient!.Stops[0].Color);
    }
    [Fact] public void SystemColorsUseLastClrAndSolidColorsUseTheSameTransforms()
    {
        var data = Change(x => ShapeGradient(x).ReplaceWith(new XElement(A + "solidFill", new XElement(A + "sysClr", new XAttribute("val", "windowText"), new XAttribute("lastClr", "0000FF"), new XElement(A + "tint", new XAttribute("val", 50000))))));
        Assert.Equal("#8080FF", Imported(data).Fill);
    }
    [Theory] [InlineData("alpha", "NaN")] [InlineData("alpha", "100001")] [InlineData("lumMod", "Infinity")]
    public void MalformedColorTransformsAreRejected(string name, string value) => Assert.Throws<InvalidDataException>(() => Imported(Change(x =>
        ShapeGradient(x).Descendants(A + "srgbClr").First().Add(new XElement(A + name, new XAttribute("val", value))))));
}
