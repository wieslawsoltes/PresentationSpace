using System.IO.Compression;
using System.Text;
using DocumentFormat.OpenXml.Validation;
using PresentationSpace.Core;
using PresentationSpace.Formats;
using PresentationSpace.Rendering.Skia;
using Xunit;
using OpenXmlPresentation=DocumentFormat.OpenXml.Packaging.PresentationDocument;
namespace PresentationSpace.Tests;

public class FormatTests
{
    [Fact] public void PptxRoundTripPreservesTextAndNotes(){var d=SlideFactory.Welcome();var bytes=PptxCodec.Export(d).Data;var imported=PptxCodec.Import(bytes);Assert.Equal(d.Title,imported.Document.Title);Assert.Equal(4,imported.Document.Slides.Length);Assert.Contains(imported.Document.Slides[0].Shapes,s=>s.Text=="A clearer way\nto present.");Assert.Equal(d.Slides[0].Notes,imported.Document.Slides[0].Notes);}
    [Fact] public void ExportHasValidOpenXmlSchema(){using var stream=new MemoryStream(PptxCodec.Export(SlideFactory.Welcome()).Data);using var document=OpenXmlPresentation.Open(stream,false);var errors=new OpenXmlValidator().Validate(document).Take(20).Select(e=>$"{e.Part?.Uri}: {e.Path?.XPath}: {e.Description}").ToArray();Assert.True(errors.Length==0,string.Join('\n',errors));}
    [Fact] public void ExportDoesNotFlattenCharts(){var result=PptxCodec.Export(SlideFactory.Welcome());Assert.DoesNotContain(result.Warnings,x=>x.Contains("Charts are exported as editable bars"));}
    [Fact] public void InvalidArchiveFails(){Assert.Throws<InvalidDataException>(()=>PptxCodec.Import(Encoding.UTF8.GetBytes("not a zip")));}
    [Fact] public void DuplicateZipPartsFail(){using var buffer=new MemoryStream();using(var zip=new ZipArchive(buffer,ZipArchiveMode.Create,true)){zip.CreateEntry("a.xml");zip.CreateEntry("a.xml");}Assert.Throws<InvalidDataException>(()=>PptxCodec.Import(buffer.ToArray()));}
    [Fact] public void DtdIsRejected(){using var buffer=new MemoryStream();using(var zip=new ZipArchive(buffer,ZipArchiveMode.Create,true)){using var writer=new StreamWriter(zip.CreateEntry("ppt/presentation.xml").Open());writer.Write("<!DOCTYPE x [<!ENTITY e SYSTEM 'file:///etc/passwd'>]><x>&e;</x>");}Assert.ThrowsAny<Exception>(()=>PptxCodec.Import(buffer.ToArray()));}
    [Fact] public void PngExportHasSignature(){using var r=new SlideRenderer();var d=SlideFactory.Welcome();var png=r.ExportPng(d,d.Slides[0],640);Assert.Equal(new byte[]{137,80,78,71,13,10,26,10},png[..8]);Assert.True(png.Length>1000);}
    [Fact] public void PdfExportHasSignature(){using var r=new SlideRenderer();var pdf=r.ExportPdf(SlideFactory.Welcome());Assert.StartsWith("%PDF-",Encoding.ASCII.GetString(pdf[..8]));}
}
