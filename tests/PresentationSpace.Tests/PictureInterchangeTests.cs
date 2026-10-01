using System.Collections.Immutable;
using System.IO.Compression;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using PresentationSpace.Core;
using PresentationSpace.Formats;
using PresentationSpace.Rendering.Skia;
using Xunit;

namespace PresentationSpace.Tests;

public sealed class PictureInterchangeTests
{
    private static readonly XNamespace A="http://schemas.openxmlformats.org/drawingml/2006/main",P="http://schemas.openxmlformats.org/presentationml/2006/main";
    private static byte[] Change(byte[] data,Action<XElement> change)
    {
        using var memory=new MemoryStream();memory.Write(data);
        using(var zip=new ZipArchive(memory,ZipArchiveMode.Update,true))
        {
            var entry=zip.GetEntry("ppt/slides/slide1.xml")!;XElement xml;
            using(var stream=entry.Open())xml=XElement.Load(stream); change(xml);entry.Delete();
            using var output=zip.CreateEntry("ppt/slides/slide1.xml").Open();xml.Save(output);
        }
        return memory.ToArray();
    }
    private static SlideShape Imported(byte[] data)=>PptxCodec.Import(data).Document.Slides[0].Shapes[0];
    [Fact] public void NativePicturesHaveValidSchemaAndEditableSourceDestinationFlags()
    {
        var picture=new PictureSpec{Source=new(.2f,.1f,.15f,.05f),Destination=new(.1f,.2f,.1f,.2f),Mask=PictureMask.Ellipse,FlipHorizontal=true,FlipVertical=true,Opacity=.6f};
        var data=PptxCodec.Export(PictureRenderingTests.Deck(picture)).Data;
        using(var stream=new MemoryStream(data))using(var doc=DocumentFormat.OpenXml.Packaging.PresentationDocument.Open(stream,false))
            Assert.Empty(new OpenXmlValidator().Validate(doc));
        var copy=Imported(data).Picture!;Assert.Equal(picture,copy);
        using var archive=new ZipArchive(new MemoryStream(data));using var xml=archive.GetEntry("ppt/slides/slide1.xml")!.Open();var root=XElement.Load(xml);
        Assert.Single(root.Descendants(P+"pic"));Assert.Single(root.Descendants(A+"srcRect"));Assert.Single(root.Descendants(A+"fillRect"));
    }
    [Theory] [InlineData(PictureFit.Contain)] [InlineData(PictureFit.Cover)] [InlineData(PictureFit.Stretch)]
    public void FitPoliciesRoundTripToEquivalentNativePictureFraming(PictureFit fit)
    {
        var original=PictureRenderingTests.Deck(new(){Fit=fit});
        var copy=PptxCodec.Import(PptxCodec.Export(original).Data).Document;
        Assert.Equal(PictureFit.Stretch,copy.Slides[0].Shapes[0].Picture!.Fit);
        using var renderer=new SlideRenderer();using var before=PictureRenderingTests.Render(renderer,original);using var after=PictureRenderingTests.Render(renderer,copy);
        Assert.Equal(before.Pixels,after.Pixels);
    }
    [Fact] public void LegacyContainIsExportedWithoutChangingItsVisibleSize()
    {
        var copy=Imported(PptxCodec.Export(PictureRenderingTests.Deck(null)).Data);
        Assert.Equal(new PictureInsets(0,.25f,0,.25f),copy.Picture!.Destination);
    }
    [Fact] public void NegativeSourceAndDestinationOffsetsRoundTrip()
    {
        var picture=new PictureSpec{Source=new(-.25f,-.1f,.1f,0),Destination=new(-.2f,0,0,-.15f)};
        Assert.Equal(picture,Imported(PptxCodec.Export(PictureRenderingTests.Deck(picture)).Data).Picture);
    }
    [Fact] public void PictureAndShapeTransparencyCombineWithoutFadingTheBorderTwice()
    {
        var d=PictureRenderingTests.Deck(new(){Opacity=.8f});d=d with{Slides=[d.Slides[0] with{Shapes=[d.Slides[0].Shapes[0] with{Opacity=.5f,Stroke="#FF000000",StrokeWidth=4}]}]};
        var copy=Imported(PptxCodec.Export(d).Data);Assert.Equal(1,copy.Opacity);Assert.InRange(Math.Abs(copy.Picture!.Opacity-.4f),0,.00001f);
        Assert.Equal("#7F000000",copy.Stroke);
    }
    [Fact] public void ReusedAssetsAndByteIdenticalAliasesEmitOneMediaPartAcrossSlides()
    {
        var asset=PictureRenderingTests.Asset();var alias=asset with{Id="alias",MimeType="image/jpeg"};
        var d=PictureRenderingTests.Deck(new(),asset);var s=d.Slides[0].Shapes[0];
        d=d with{Assets=d.Assets.Add(alias.Id,alias),Slides=[d.Slides[0],new(){Shapes=[s with{Id=Guid.NewGuid(),AssetId=alias.Id},s with{Id=Guid.NewGuid()}]}]};
        var export=PptxCodec.Export(d);using var zip=new ZipArchive(new MemoryStream(export.Data));Assert.Single(zip.Entries.Where(e=>e.FullName.StartsWith("ppt/media/")));
        Assert.Contains(export.Warnings,w=>w.Contains("content types"));
        var copy=PptxCodec.Import(export.Data).Document;Assert.Single(copy.Assets);Assert.Equal(3,copy.Slides.Sum(s=>s.Shapes.Length));
    }
    [Fact] public void NativePictureMetadataAndBinaryPayloadSurvive()
    {
        var d=PictureRenderingTests.Deck(new(){FlipHorizontal=true});var original=d.Slides[0].Shapes[0] with{Rotation=37,AlternativeText="Test image",Name="Example",Hidden=true};
        d=d with{Slides=[d.Slides[0] with{Shapes=[original]}]};var copy=PptxCodec.Import(PptxCodec.Export(d).Data).Document;
        var shape=copy.Slides[0].Shapes[0];Assert.Equal(original.Bounds,shape.Bounds);Assert.Equal(37,shape.Rotation);Assert.True(shape.Hidden);Assert.Equal("Test image",shape.AlternativeText);Assert.Equal("Example",shape.Name);
        Assert.Equal(d.Assets.Values.Single().Base64,copy.Assets.Values.Single().Base64);
    }
    [Theory] [InlineData("NaN")] [InlineData("Infinity")] [InlineData("bad")] [InlineData("100000")][InlineData("2000000")]
    public void InvalidSourcePercentagesFailBeforeRendering(string value)
    {
        var data=PptxCodec.Export(PictureRenderingTests.Deck(new())).Data;
        data=Change(data,x=>x.Descendants(A+"srcRect").Single().SetAttributeValue("l",value));
        Assert.Throws<InvalidDataException>(()=>PptxCodec.Import(data));
    }
    [Fact] public void PercentStringCoordinatesAreAccepted()
    {
        var data=PptxCodec.Export(PictureRenderingTests.Deck(new())).Data;
        var copy=Imported(Change(data,x=>x.Descendants(A+"srcRect").Single().SetAttributeValue("l","25%")));
        Assert.Equal(.25f,copy.Picture!.Source.Left);
    }
    [Theory] [InlineData("flipH")] [InlineData("flipV")]
    public void MalformedFlipFlagsAreRejected(string flag)
    {
        var data=PptxCodec.Export(PictureRenderingTests.Deck(new())).Data;
        Assert.Throws<InvalidDataException>(()=>Imported(Change(data,x=>x.Descendants(A+"xfrm").Last().SetAttributeValue(flag,"maybe"))));
    }
    [Fact] public void UnsupportedPictureModesAreDiagnosed()
    {
        var data=PptxCodec.Export(PictureRenderingTests.Deck(new())).Data;
        var result=PptxCodec.Import(Change(data,x=>
        {
            var fill=x.Descendants(P+"blipFill").Single();fill.SetAttributeValue("rotWithShape","0");fill.Element(A+"stretch")!.ReplaceWith(new XElement(A+"tile"));
            fill.Element(A+"blip")!.Add(new XElement(A+"grayscl"));x.Descendants(A+"prstGeom").Single().SetAttributeValue("prst","roundRect");
        }));
        Assert.Contains(result.Warnings,w=>w.Contains("Tiled"));Assert.Contains(result.Warnings,w=>w.Contains("recoloring"));Assert.Contains(result.Warnings,w=>w.Contains("masks"));Assert.Contains(result.Warnings,w=>w.Contains("rotate"));
    }
    [Fact] public void ExternalImagesAreNeverFetched()
    {
        var data=PptxCodec.Export(PictureRenderingTests.Deck(new())).Data;
        using var memory=new MemoryStream();memory.Write(data);
        using(var zip=new ZipArchive(memory,ZipArchiveMode.Update,true))
        {
            var e=zip.GetEntry("ppt/slides/_rels/slide1.xml.rels")!;XElement xml;using(var read=e.Open())xml=XElement.Load(read);
            var rel=xml.Elements().Single(e=>((string?)e.Attribute("Type"))?.EndsWith("/image")==true);rel.SetAttributeValue("TargetMode","External");rel.SetAttributeValue("Target","https://example.invalid/picture.png");e.Delete();using var write=zip.CreateEntry("ppt/slides/_rels/slide1.xml.rels").Open();xml.Save(write);
        }
        var result=PptxCodec.Import(memory.ToArray());Assert.Empty(result.Document.Assets);Assert.Empty(result.Document.Slides[0].Shapes);Assert.Contains(result.Warnings,w=>w.Contains("External"));
    }
}
