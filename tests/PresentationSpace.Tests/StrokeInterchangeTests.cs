using System.Collections.Immutable;
using System.IO.Compression;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Validation;
using PresentationSpace.Core;
using PresentationSpace.Formats;
using Xunit;

namespace PresentationSpace.Tests;

public sealed class StrokeInterchangeTests
{
    private static readonly XNamespace A="http://schemas.openxmlformats.org/drawingml/2006/main",P="http://schemas.openxmlformats.org/presentationml/2006/main";
    private static PresentationDocument Deck(SlideShape shape)=>new() {Slides=[new() {LayoutName="Title only",Shapes=[shape]}]};
    private static SlideShape Example()=>StrokeModel.Line(new(200,40),new(20,40),true) with {Stroke="#4488CC",StrokeWidth=8,Outline=new() {Cap=StrokeCap.Square,Join=StrokeJoin.Miter,MiterLimit=7,Dash=StrokeDash.LongDashDot,Begin=new() {Kind=LineEndKind.Diamond,Width=LineEndSize.Small},End=new() {Kind=LineEndKind.Stealth,Length=LineEndSize.Large}}};
    private static byte[] Rewrite(byte[] bytes,Action<ZipArchive> change)
    {using var output=new MemoryStream();output.Write(bytes);using(var z=new ZipArchive(output,ZipArchiveMode.Update,true))change(z);return output.ToArray();}
    private static XElement Read(ZipArchive z,string name){using var stream=z.GetEntry(name)!.Open();return XElement.Load(stream);}
    private static void Set(ZipArchive z,string name,XElement root){z.GetEntry(name)?.Delete();using var stream=z.CreateEntry(name).Open();root.Save(stream);}
    private static void Xml(ZipArchive z,string name,Action<XElement> edit){var x=Read(z,name);edit(x);Set(z,name,x);}
    private static byte[] Change(Action<XElement> edit)=>Rewrite(PptxCodec.Export(Deck(Example())).Data,z=>Xml(z,"ppt/slides/slide1.xml",edit));
    private static XElement Outline(XElement x)=>x.Descendants(P+"spPr").First().Element(A+"ln")!;
    private static SlideShape Import(byte[] b)=>PptxCodec.Import(b).Document.Slides[0].Shapes[0];
    [Fact] public void NativeSampleValidatesWithoutRasterizing()
    {
        byte[] bytes=PptxCodec.Export(StrokeSample.Create()).Data;
        using(var stream=new MemoryStream(bytes))using(var p=DocumentFormat.OpenXml.Packaging.PresentationDocument.Open(stream,false)) Assert.Empty(new OpenXmlValidator().Validate(p));
        using var z=new ZipArchive(new MemoryStream(bytes));Assert.Empty(z.Entries.Where(e=>e.FullName.StartsWith("ppt/media/")));
        var slide=Read(z,"ppt/slides/slide1.xml");Assert.NotEmpty(slide.Descendants(A+"custDash"));Assert.NotEmpty(slide.Descendants(A+"ln").Where(x=>x.Element(A+"gradFill") is not null));
    }
    [Theory] [InlineData(StrokeDash.Solid)] [InlineData(StrokeDash.Dot)] [InlineData(StrokeDash.Dash)] [InlineData(StrokeDash.LongDash)] [InlineData(StrokeDash.DashDot)] [InlineData(StrokeDash.LongDashDot)] [InlineData(StrokeDash.LongDashDotDot)] [InlineData(StrokeDash.SystemDash)] [InlineData(StrokeDash.SystemDot)] [InlineData(StrokeDash.SystemDashDot)] [InlineData(StrokeDash.SystemDashDotDot)]
    public void EveryPresetDashRoundTrips(StrokeDash dash)
    {
        var original=Example() with {Outline=Example().Outline! with {Dash=dash}};var next=Import(PptxCodec.Export(Deck(original)).Data);
        Assert.Equal(original.Bounds,next.Bounds);Assert.Equal(original.LineDirection,next.LineDirection);Assert.Equal(8,next.StrokeWidth);Assert.True(StrokeModel.Equivalent(original.Outline!,next.Outline!));
    }
    [Theory] [InlineData(LineEndKind.None)] [InlineData(LineEndKind.Triangle)] [InlineData(LineEndKind.Stealth)] [InlineData(LineEndKind.Diamond)] [InlineData(LineEndKind.Oval)] [InlineData(LineEndKind.OpenArrow)]
    public void EveryEndRoundTripsIncludingExplicitNone(LineEndKind kind)
    {
        var shape=Example() with {Outline=new() {Begin=new() {Kind=kind,Width=LineEndSize.Large,Length=LineEndSize.Small},End=new() {Kind=kind,Width=LineEndSize.Small,Length=LineEndSize.Large}}};
        var next=Import(PptxCodec.Export(Deck(shape)).Data);Assert.Equal(shape.Outline!.Begin,next.Outline!.Begin);Assert.Equal(shape.Outline.End,next.Outline.End);
    }
    [Theory] [InlineData(StrokeCap.Flat,StrokeJoin.Round)] [InlineData(StrokeCap.Round,StrokeJoin.Bevel)] [InlineData(StrokeCap.Square,StrokeJoin.Miter)]
    public void CapsJoinsAndGradientAlphaRoundTrip(StrokeCap cap,StrokeJoin join)
    {
        var shape=Example() with {Opacity=.5f,Outline=new() {Cap=cap,Join=join,Gradient=new() {Stops=[new(0,"#FF0000",.5f),new(1,"#0000FF")]},CustomDashes=[new(4.5f,2),new(0,1)]}};
        var next=Import(PptxCodec.Export(Deck(shape)).Data);Assert.Equal(cap,next.Outline!.Cap);Assert.Equal(join,next.Outline.Join);Assert.Equal(shape.Outline!.CustomDashes.ToArray(),next.Outline.CustomDashes.ToArray());
        Assert.Equal(.25f,next.Outline.Gradient!.Stops[0].Opacity);Assert.Equal(.5f,next.Outline.Gradient.Stops[1].Opacity);
    }
    [Fact] public void TailEndOnAClosedShapeDoesNotChangeItsGeometry()
    {
        var original=new SlideShape {Kind=ShapeKind.Ellipse,Outline=new() {End=new() {Kind=LineEndKind.Triangle}}};
        Assert.Equal(ShapeKind.Ellipse,Import(PptxCodec.Export(Deck(original)).Data).Kind);
        var none=Change(x=>Outline(x).Element(A+"tailEnd")!.SetAttributeValue("type","none"));
        var both=Rewrite(none,z=>Xml(z,"ppt/slides/slide1.xml",x=>Outline(x).Element(A+"headEnd")!.SetAttributeValue("type","none")));
        Assert.Equal(ShapeKind.Line,Import(both).Kind);
    }
    [Fact] public void AxisAlignedAndCollapsedLinesKeepZeroExtents()
    {
        foreach(var s in new[]{StrokeModel.Line(new(30,10),new(30,200)),StrokeModel.Line(new(20,30),new(200,30)),StrokeModel.Line(new(40,40),new(40,40))})
        {var next=Import(PptxCodec.Export(Deck(s)).Data);Assert.Equal(s.Bounds,next.Bounds);}
    }
    [Fact] public void ExplicitNoFillBlocksInheritedOutlineFillButNotWidth()
    {
        var shape=Example() with {Placeholder=PlaceholderKind.Title};
        var data=Rewrite(PptxCodec.Export(Deck(shape)).Data,z=>
        {
            foreach(var name in z.Entries.Select(e=>e.FullName).Where(n=>n.StartsWith("ppt/slideLayouts/")&&n.EndsWith(".xml")).ToArray())Xml(z,name,x=>
            {foreach(var pr in x.Descendants(P+"spPr"))pr.Element(A+"ln")!.ReplaceWith(new XElement(A+"ln",new XAttribute("w",76200),new XElement(A+"solidFill",new XElement(A+"srgbClr",new XAttribute("val","FF0000"))),new XElement(A+"prstDash",new XAttribute("val","dot"))));});
            Xml(z,"ppt/slides/slide1.xml",x=>Outline(x).ReplaceWith(new XElement(A+"ln",new XElement(A+"noFill"),new XElement(A+"bevel"))));
        });
        var next=Import(data);Assert.Equal(8,next.StrokeWidth);Assert.Equal("#00000000",next.Stroke);Assert.Equal(StrokeDash.Dot,next.Outline!.Dash);Assert.Equal(StrokeJoin.Bevel,next.Outline.Join);
    }
    [Fact] public void ThemeLineReferenceResolvesPlaceholderColorAndDirectOverrides()
    {
        var data=Rewrite(PptxCodec.Export(Deck(Example())).Data,z=>
        {
            Xml(z,"ppt/theme/theme1.xml",x=>x.Descendants(A+"lnStyleLst").First().Elements().First().ReplaceWith(new XElement(A+"ln",new XAttribute("w",57150),new XAttribute("cap","flat"),new XElement(A+"solidFill",new XElement(A+"schemeClr",new XAttribute("val","phClr"),new XElement(A+"shade",new XAttribute("val",50000)))),new XElement(A+"prstDash",new XAttribute("val","lgDash")))));
            Xml(z,"ppt/slides/slide1.xml",x=>
            {Outline(x).ReplaceWith(new XElement(A+"ln",new XAttribute("w",76200)));x.Descendants(P+"sp").First().Add(new XElement(P+"style",new XElement(A+"lnRef",new XAttribute("idx",1),new XElement(A+"srgbClr",new XAttribute("val","FF0000")))));});
        });
        var next=Import(data);Assert.Equal(8,next.StrokeWidth);Assert.Equal("#800000",next.Stroke);Assert.Equal(StrokeCap.Flat,next.Outline!.Cap);Assert.Equal(StrokeDash.LongDash,next.Outline.Dash);
    }
    [Fact] public void ConnectorsInsideGroupsAreNotSilentlyDropped()
    {
        var data=Change(x=>
        {
            var shape=x.Descendants(P+"sp").First();shape.Name=P+"cxnSp";
            var pr=shape.Element(P+"nvSpPr")!;pr.Name=P+"nvCxnSpPr";pr.Element(P+"cNvSpPr")!.Name=P+"cNvCxnSpPr";
            var group=new XElement(P+"grpSp",new XElement(P+"grpSpPr",new XElement(A+"xfrm",new XElement(A+"off",new XAttribute("x",0),new XAttribute("y",0)),new XElement(A+"ext",new XAttribute("cx",100),new XAttribute("cy",100)),new XElement(A+"chOff",new XAttribute("x",0),new XAttribute("y",0)),new XElement(A+"chExt",new XAttribute("cx",100),new XAttribute("cy",100)))));
            shape.ReplaceWith(group);group.Add(shape);
        });
        Assert.Equal(ShapeKind.Arrow,Import(data).Kind);
    }
    [Theory] [InlineData("w","-1")] [InlineData("w","NaN")] [InlineData("w","9525001")] [InlineData("cap","banana")]
    public void BadOutlineAttributesFailAtomically(string name,string value)=>Assert.Throws<InvalidDataException>(()=>Import(Change(x=>Outline(x).SetAttributeValue(name,value))));
    [Theory] [InlineData("cx","-1")] [InlineData("cy","Infinity")] [InlineData("cx","1.1")]
    public void BadLineExtentsAreNotClampedIntoPlausibleGeometry(string key,string value)=>Assert.Throws<InvalidDataException>(()=>Import(Change(x=>x.Descendants(A+"ext").Last().SetAttributeValue(key,value))));
    [Fact] public void EmptyZeroAndOversizedCustomPatternsAreRejected()
    {
        Assert.Throws<InvalidDataException>(()=>Import(Change(x=>Outline(x).Element(A+"prstDash")!.ReplaceWith(new XElement(A+"custDash")))));
        Assert.Throws<InvalidDataException>(()=>Import(Change(x=>Outline(x).Element(A+"prstDash")!.ReplaceWith(new XElement(A+"custDash",new XElement(A+"ds",new XAttribute("d",0),new XAttribute("sp",0)))))));
        Assert.Throws<InvalidDataException>(()=>Import(Change(x=>Outline(x).Element(A+"prstDash")!.ReplaceWith(new XElement(A+"custDash",Enumerable.Range(0,33).Select(_=>new XElement(A+"ds",new XAttribute("d",100000),new XAttribute("sp",100000))))))));
    }
    [Fact] public void UnsupportedCompoundAndInsetModesWarn()
    {
        var result=PptxCodec.Import(Change(x=>{Outline(x).SetAttributeValue("cmpd","dbl");Outline(x).SetAttributeValue("algn","in");}));
        Assert.Contains(result.Warnings,w=>w.Contains("Compound outlines"));Assert.Contains(result.Warnings,w=>w.Contains("Inset outline"));
    }
}
