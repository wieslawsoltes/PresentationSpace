using System.Collections.Immutable;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using PresentationSpace.Core;

namespace PresentationSpace.Formats;

public sealed record ImportResult(PresentationDocument Document,IReadOnlyList<string> Warnings);
public sealed record ExportResult(byte[] Data,IReadOnlyList<string> Warnings);

/// <summary>Interoperability for the documented PresentationML subset. Never executes macros, follows external links, or downloads assets.</summary>
public static partial class PptxCodec
{
    private static readonly XNamespace P="http://schemas.openxmlformats.org/presentationml/2006/main",A="http://schemas.openxmlformats.org/drawingml/2006/main",R="http://schemas.openxmlformats.org/officeDocument/2006/relationships",Rel="http://schemas.openxmlformats.org/package/2006/relationships",CT="http://schemas.openxmlformats.org/package/2006/content-types",C="http://schemas.openxmlformats.org/drawingml/2006/chart";
    private const string RelationshipBase="http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
    private const string PresentationContent="application/vnd.openxmlformats-officedocument.presentationml.";
    private const float Emu=9525;
    private static XAttribute V(string name,object value)=>new(name,value);
    private static long E(float value)=>(long)Math.Round(value*Emu);
    private static XElement Root(XName name,params object[] children)=>new(name,new XAttribute(XNamespace.Xmlns+"a",A),new XAttribute(XNamespace.Xmlns+"r",R),new XAttribute(XNamespace.Xmlns+"p",P),children);
    private static XElement Relation(string id,string type,string target)=>new(Rel+"Relationship",V("Id",id),V("Type",RelationshipBase+type),V("Target",target));
    private static XElement Relations(IEnumerable<XElement> elements)=>new(Rel+"Relationships",elements);
    private static XElement GroupTree(params object[] shapes)=>new(P+"spTree",new XElement(P+"nvGrpSpPr",new XElement(P+"cNvPr",V("id",1),V("name","")),new XElement(P+"cNvGrpSpPr"),new XElement(P+"nvPr")),new XElement(P+"grpSpPr",new XElement(A+"xfrm",new XElement(A+"off",V("x",0),V("y",0)),new XElement(A+"ext",V("cx",0),V("cy",0)),new XElement(A+"chOff",V("x",0),V("y",0)),new XElement(A+"chExt",V("cx",0),V("cy",0)))),shapes);
    private static XElement ColorMap()=>new(P+"clrMap",V("bg1","lt1"),V("tx1","dk1"),V("bg2","lt2"),V("tx2","dk2"),Enumerable.Range(1,6).Select(i=>V("accent"+i,"accent"+i)),V("hlink","hlink"),V("folHlink","folHlink"));
    private static XElement ColorMapOverride()=>new(P+"clrMapOvr",new XElement(A+"masterClrMapping"));
    private static XElement Fill(string color,float opacity=1)
    {
        color=color.TrimStart('#');int alpha=255;if(color.Length==8){alpha=int.Parse(color[..2],NumberStyles.HexNumber,CultureInfo.InvariantCulture);color=color[2..];}
        if(color.Length!=6||!int.TryParse(color,NumberStyles.HexNumber,CultureInfo.InvariantCulture,out _))color="000000";
        if(alpha==0||opacity<=0)return new(A+"noFill");
        return new(A+"solidFill",new XElement(A+"srgbClr",V("val",color.ToUpperInvariant()),new XElement(A+"alpha",V("val",Math.Clamp((int)Math.Round(alpha/255f*opacity*100000),0,100000)))));
    }
    private static XElement Transform(SlideShape shape)
    {
        var b=shape.Bounds;return new(A+"xfrm",V("rot",(int)Math.Round(shape.Rotation*60000)),new XElement(A+"off",V("x",E(b.X)),V("y",E(b.Y))),new XElement(A+"ext",V("cx",E(b.Width)),V("cy",E(b.Height))));
    }
    private static XElement TextBody(string text,TextStyle style) => RichTextBody(new SlideShape { Text = text, TextStyle = style });
    private static XElement Shape(SlideShape s,int id,string? imageRelationship=null)
    {
        var nonvisual=new XElement(P+"cNvPr",V("id",id),V("name",s.Name),V("descr",s.AlternativeText),V("hidden",s.Hidden?1:0));
        if(s.Kind==ShapeKind.Image&&imageRelationship is not null)
            return new(P+"pic",new XElement(P+"nvPicPr",nonvisual,new XElement(P+"cNvPicPr",new XElement(A+"picLocks",V("noChangeAspect",1))),new XElement(P+"nvPr")),new XElement(P+"blipFill",new XElement(A+"blip",new XAttribute(R+"embed",imageRelationship)),new XElement(A+"stretch",new XElement(A+"fillRect"))),new XElement(P+"spPr",Transform(s),new XElement(A+"prstGeom",V("prst","rect"),new XElement(A+"avLst"))));
        string geometry=s.Kind switch{ShapeKind.Ellipse=>"ellipse",ShapeKind.RoundRectangle=>"roundRect",ShapeKind.Triangle=>"triangle",ShapeKind.Diamond=>"diamond",ShapeKind.Line or ShapeKind.Arrow=>"line",_=>"rect"};
        var outline=new XElement(A+"ln",V("w",E(s.StrokeWidth)),Fill(s.Stroke,s.Opacity));if(s.Kind==ShapeKind.Arrow)outline.Add(new XElement(A+"tailEnd",V("type","triangle")));
        return new(P+"sp",new XElement(P+"nvSpPr",nonvisual,new XElement(P+"cNvSpPr",V("txBox",s.Kind==ShapeKind.Text?1:0)),new XElement(P+"nvPr",s.Placeholder == PlaceholderKind.None ? null : Placeholder(s))),new XElement(P+"spPr",Transform(s),new XElement(A+"prstGeom",V("prst",geometry),new XElement(A+"avLst")),Fill(s.Fill,s.Opacity),outline),RichTextBody(s));
    }
    public static ExportResult Export(PresentationDocument document)
    {
        DocumentSerializer.Validate(document);var warnings=new HashSet<string>();using var output=new MemoryStream();
        using(var zip=new ZipArchive(output,ZipArchiveMode.Create,true))
        {
            var types=new XElement(CT+"Types",new XElement(CT+"Default",V("Extension","rels"),V("ContentType","application/vnd.openxmlformats-package.relationships+xml")),new XElement(CT+"Default",V("Extension","xml"),V("ContentType","application/xml")));
            void Xml(string path,XElement root,string? contentType=null){WriteXml(zip,path,root);if(contentType is not null)types.Add(new XElement(CT+"Override",V("PartName","/"+path),V("ContentType",contentType)));}
            Xml("_rels/.rels",Relations([Relation("rId1","officeDocument","ppt/presentation.xml"),new XElement(Rel+"Relationship",V("Id","rId2"),V("Type","http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties"),V("Target","docProps/core.xml"))]));
            XNamespace cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties",dc="http://purl.org/dc/elements/1.1/";
            Xml("docProps/core.xml",new XElement(cp+"coreProperties",new XAttribute(XNamespace.Xmlns+"dc",dc),new XElement(dc+"title",document.Title),new XElement(dc+"creator","PresentationSpace")),"application/vnd.openxmlformats-package.core-properties+xml");
            var presentationRels=new List<XElement>{Relation("rId1","slideMaster","slideMasters/slideMaster1.xml"),Relation("rId2","notesMaster","notesMasters/notesMaster1.xml"),Relation("rId3","presProps","presProps.xml")};
            var slideIds=new XElement(P+"sldIdLst");
            for(int index=0;index<document.Slides.Length;index++)
            {
                var slide=document.Slides[index];int number=index+1;string rid="rId"+(number+3);slideIds.Add(new XElement(P+"sldId",V("id",256+index),new XAttribute(R+"id",rid)));presentationRels.Add(Relation(rid,"slide",$"slides/slide{number}.xml"));
                var rels=new List<XElement>{Relation("rId1","slideLayout",$"../slideLayouts/slideLayout{Math.Max(0,SlideLayoutEngine.Layouts.IndexOf(slide.LayoutName??"Blank"))+1}.xml"),Relation("rId2","notesSlide",$"../notesSlides/notesSlide{number}.xml")};
                var shapes=new List<XElement>();int objectId=2;
                foreach(var original in slide.Shapes)
                {
                    if(original.Animation!=AnimationKind.None)warnings.Add("Object animations are retained in native files but are not exported to PPTX.");
                    if(original.Locked||original.GroupId is not null)warnings.Add("Grouping and editor locks are flattened on PPTX export.");
                    foreach(var shape in new[]{original})
                    {
                        if(shape.Kind==ShapeKind.Table){shapes.Add(NativeTable(shape,objectId++));continue;}
                        if(shape.Kind==ShapeKind.Chart){shapes.Add(NativeChart(zip,types,shape,objectId++,number,rels));continue;}
                        string? imageRid=null;
                        if(shape.Kind==ShapeKind.Image&&shape.AssetId is {} assetId&&document.Assets.TryGetValue(assetId,out var asset))
                        {
                            imageRid="rIdImage"+objectId;string extension=asset.MimeType switch{"image/jpeg"=>"jpg","image/webp"=>"webp","image/gif"=>"gif",_=>"png"};string path=$"ppt/media/slide{number}image{objectId}.{extension}";
                            using(var stream=zip.CreateEntry(path).Open()){var data=Convert.FromBase64String(asset.Base64);stream.Write(data);}types.Add(new XElement(CT+"Override",V("PartName","/"+path),V("ContentType",asset.MimeType)));rels.Add(Relation(imageRid,"image",$"../media/slide{number}image{objectId}.{extension}"));
                        }
                        shapes.Add(Shape(shape,objectId++,imageRid));
                    }
                }
                var root=Root(P+"sld",V("show",slide.Hidden?0:1),new XElement(P+"cSld",V("name",slide.Name),new XElement(P+"bg",new XElement(P+"bgPr",Fill(slide.Background),new XElement(A+"effectLst"))),GroupTree(shapes.ToArray())),ColorMapOverride());
                if(slide.Transition!=TransitionKind.None)root.Add(new XElement(P+"transition",V("spd","med"),new XElement(P+slide.Transition.ToString().ToLowerInvariant(),slide.Transition==TransitionKind.Fade?null:V("dir","l"))));
                Xml($"ppt/slides/slide{number}.xml",root,PresentationContent+"slide+xml");Xml($"ppt/slides/_rels/slide{number}.xml.rels",Relations(rels));
                var notesShape=Shape(SlideFactory.Text(slide.Notes,40,120,640,750,20),2);notesShape.Element(P+"nvSpPr")!.Element(P+"nvPr")!.Add(new XElement(P+"ph",V("type","body"),V("idx",1)));
                Xml($"ppt/notesSlides/notesSlide{number}.xml",Root(P+"notes",new XElement(P+"cSld",GroupTree(notesShape)),ColorMapOverride()),PresentationContent+"notesSlide+xml");
                Xml($"ppt/notesSlides/_rels/notesSlide{number}.xml.rels",Relations([Relation("rId1","notesMaster","../notesMasters/notesMaster1.xml"),Relation("rId2","slide",$"../slides/slide{number}.xml")]));
                if(!slide.Comments.IsEmpty)warnings.Add("Review comments are retained in native files but are not exported to PPTX.");
            }
            Xml("ppt/presentation.xml",Root(P+"presentation",new XElement(P+"sldMasterIdLst",new XElement(P+"sldMasterId",V("id",2147483648L),new XAttribute(R+"id","rId1"))),new XElement(P+"notesMasterIdLst",new XElement(P+"notesMasterId",new XAttribute(R+"id","rId2"))),slideIds,new XElement(P+"sldSz",V("cx",E(document.Width)),V("cy",E(document.Height))),new XElement(P+"notesSz",V("cx",6858000),V("cy",9144000))),PresentationContent+"presentation.main+xml");
            Xml("ppt/_rels/presentation.xml.rels",Relations(presentationRels));Xml("ppt/presProps.xml",Root(P+"presentationPr"),PresentationContent+"presProps+xml");
            Xml("ppt/slideMasters/slideMaster1.xml",Root(P+"sldMaster",new XElement(P+"cSld",V("name","PresentationSpace"),GroupTree()),ColorMap(),new XElement(P+"sldLayoutIdLst",SlideLayoutEngine.Layouts.Select((_,i)=>new XElement(P+"sldLayoutId",V("id",2147483649L+i),new XAttribute(R+"id","rId"+(i+1))))),new XElement(P+"txStyles",new XElement(P+"titleStyle"),new XElement(P+"bodyStyle"),new XElement(P+"otherStyle"))),PresentationContent+"slideMaster+xml");
            Xml("ppt/slideMasters/_rels/slideMaster1.xml.rels",Relations(SlideLayoutEngine.Layouts.Select((_,i)=>Relation("rId"+(i+1),"slideLayout",$"../slideLayouts/slideLayout{i+1}.xml")).Append(Relation("rIdTheme","theme","../theme/theme1.xml"))));
            for(int i=0;i<SlideLayoutEngine.Layouts.Length;i++)
            {
                var name=SlideLayoutEngine.Layouts[i];var template=SlideFactory.Create(name,document.Width,document.Height);
                string type=name switch{"Title slide"=>"title","Title and content"=>"obj","Two content"=>"twoObj","Title only"=>"titleOnly",_=>"blank"};
                Xml($"ppt/slideLayouts/slideLayout{i+1}.xml",Root(P+"sldLayout",V("type",type),V("preserve",1),new XElement(P+"cSld",V("name",name),GroupTree(template.Shapes.Select((shape,j)=>(object)Shape(shape,j+2)).ToArray())),ColorMapOverride()),PresentationContent+"slideLayout+xml");
                Xml($"ppt/slideLayouts/_rels/slideLayout{i+1}.xml.rels",Relations([Relation("rId1","slideMaster","../slideMasters/slideMaster1.xml")]));
            }
            Xml("ppt/notesMasters/notesMaster1.xml",Root(P+"notesMaster",new XElement(P+"cSld",GroupTree()),ColorMap(),new XElement(P+"notesStyle")),PresentationContent+"notesMaster+xml");
            Xml("ppt/notesMasters/_rels/notesMaster1.xml.rels",Relations([Relation("rId1","theme","../theme/theme1.xml")]));
            Xml("ppt/theme/theme1.xml",Theme(),"application/vnd.openxmlformats-officedocument.theme+xml");Xml("[Content_Types].xml",types);
        }
        return new(output.ToArray(),warnings.ToArray());
    }
    private static XElement Theme()
    {
        string[] names=["dk1","lt1","dk2","lt2","accent1","accent2","accent3","accent4","accent5","accent6","hlink","folHlink"],colors=["000000","FFFFFF","243247","F4F5F7","D35230","4472C4","70AD47","FFC000","7654B3","187EAB","0563C1","954F72"];
        XElement Font(string name)=>new(A+name,new XElement(A+"latin",V("typeface","Arial")),new XElement(A+"ea",V("typeface","")),new XElement(A+"cs",V("typeface","")));
        XElement SchemeFill()=>new(A+"solidFill",new XElement(A+"schemeClr",V("val","phClr")));
        return new(A+"theme",V("name","PresentationSpace"),new XAttribute(XNamespace.Xmlns+"a",A),new XElement(A+"themeElements",new XElement(A+"clrScheme",V("name","Office"),names.Select((name,i)=>new XElement(A+name,new XElement(A+"srgbClr",V("val",colors[i]))))),new XElement(A+"fontScheme",V("name","Arial"),Font("majorFont"),Font("minorFont")),new XElement(A+"fmtScheme",V("name","Office"),new XElement(A+"fillStyleLst",Enumerable.Range(0,3).Select(_=>SchemeFill())),new XElement(A+"lnStyleLst",Enumerable.Range(1,3).Select(i=>new XElement(A+"ln",V("w",i*9525),SchemeFill(),new XElement(A+"prstDash",V("val","solid"))))),new XElement(A+"effectStyleLst",Enumerable.Range(0,3).Select(_=>new XElement(A+"effectStyle",new XElement(A+"effectLst")))),new XElement(A+"bgFillStyleLst",Enumerable.Range(0,3).Select(_=>SchemeFill())))));
    }
    private static void WriteXml(ZipArchive zip,string path,XElement root)
    {
        using var stream=zip.CreateEntry(path,CompressionLevel.Fastest).Open();using var writer=XmlWriter.Create(stream,new XmlWriterSettings{Encoding=new UTF8Encoding(false),Indent=false});new XDocument(new XDeclaration("1.0","utf-8","yes"),root).Save(writer);
    }
    public static ImportResult Import(byte[] data)
    {
        if(data.Length>DocumentSerializer.MaxFileBytes)throw new InvalidDataException("PPTX files are limited to 64 MB.");using var input=new MemoryStream(data,false);using var zip=new ZipArchive(input,ZipArchiveMode.Read);
        if(zip.Entries.Count>10000||zip.Entries.Sum(e=>e.Length)>128L*1024*1024||zip.Entries.Any(e=>e.Length>32L*1024*1024))throw new InvalidDataException("PPTX package exceeds the safe expansion limits.");
        if(zip.Entries.GroupBy(e=>e.FullName,StringComparer.Ordinal).Any(g=>g.Count()>1))throw new InvalidDataException("Duplicate package part names.");
        var warnings=new HashSet<string>();
        XElement Read(string path)
        {
            var entry=zip.GetEntry(path)??throw new InvalidDataException("Missing package part: "+path);using var stream=entry.Open();using var reader=XmlReader.Create(stream,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=8*1024*1024});return XElement.Load(reader);
        }
        Dictionary<string,(string Type,string Path)> Rels(string path)
        {
            string folder=path.Contains('/')?path[..(path.LastIndexOf('/')+1)]:"",name=path[(path.LastIndexOf('/')+1)..];string relPath=folder+"_rels/"+name+".rels";
            if(zip.GetEntry(relPath)is null)return [];
            var result=new Dictionary<string,(string,string)>();foreach(var rel in Read(relPath).Elements(Rel+"Relationship"))
            {
                if((string?)rel.Attribute("TargetMode")=="External"){warnings.Add("External links and linked assets were not loaded.");continue;}
                string target=(string?)rel.Attribute("Target")??"",id=(string?)rel.Attribute("Id")??"",type=(string?)rel.Attribute("Type")??"";
                if(Uri.TryCreate(target,UriKind.Absolute,out _)&&!target.StartsWith('/')){warnings.Add("External relationship ignored.");continue;}
                var segments=new List<string>();foreach(var segment in (target.StartsWith('/')?target[1..]:folder+target).Split('/')){if(segment=="..") {if(segments.Count==0)throw new InvalidDataException("Package relationship escapes its root.");segments.RemoveAt(segments.Count-1);}else if(segment!="."&&segment.Length>0)segments.Add(segment);}
                result[id]=(type,string.Join('/',segments));
            }
            return result;
        }
        var rootRels=Rels("");string presentationPath=rootRels.Values.FirstOrDefault(x=>x.Type.EndsWith("/officeDocument")).Path??"ppt/presentation.xml";var presentation=Read(presentationPath);
        if(presentation.Name!=P+"presentation")throw new InvalidDataException("Only transitional PresentationML PPTX documents are currently supported.");
        var presentationRels=Rels(presentationPath);var size=presentation.Element(P+"sldSz");float width=Number(size,"cx",12192000)/Emu,height=Number(size,"cy",6858000)/Emu;
        string title="Imported presentation";var core=zip.GetEntry("docProps/core.xml");if(core is not null)title=Read(core.FullName).Descendants().FirstOrDefault(e=>e.Name.LocalName=="title")?.Value??title;
        var themeColors=new Dictionary<string,string>{{"tx1","#000000"},{"bg1","#FFFFFF"},{"tx2","#243247"},{"bg2","#F4F5F7"}};
        var themeEntry=zip.Entries.FirstOrDefault(e=>e.FullName.StartsWith("ppt/theme/")&&e.FullName.EndsWith(".xml"));if(themeEntry is not null)foreach(var color in Read(themeEntry.FullName).Descendants(A+"clrScheme").Elements()){var value=color.Elements().FirstOrDefault();themeColors[color.Name.LocalName]="#"+((string?)value?.Attribute("val")??(string?)value?.Attribute("lastClr")??"000000");}
        var assets=ImmutableDictionary.CreateBuilder<string,PresentationAsset>();var assetPaths=new Dictionary<string,string>();var slides=ImmutableArray.CreateBuilder<Slide>();
        foreach(var id in presentation.Element(P+"sldIdLst")?.Elements(P+"sldId")??[])
        {
            if(slides.Count>=2000)throw new InvalidDataException("Too many slides.");string rid=(string?)id.Attribute(R+"id")??"";if(!presentationRels.TryGetValue(rid,out var relation))throw new InvalidDataException("Missing slide relationship.");var root=Read(relation.Path);var rels=Rels(relation.Path);var tree=root.Element(P+"cSld")?.Element(P+"spTree");var shapes=ImmutableArray.CreateBuilder<SlideShape>();
            XElement? layout=null,master=null;var layoutRel=rels.Values.FirstOrDefault(r=>r.Type.EndsWith("/slideLayout"));
            if(layoutRel.Path is not null)
            {
                layout=Read(layoutRel.Path);var masterRel=Rels(layoutRel.Path).Values.FirstOrDefault(r=>r.Type.EndsWith("/slideMaster"));
                if(masterRel.Path is not null)master=Read(masterRel.Path);
            }
            XElement? MatchPlaceholder(XElement? container,XElement? ph,bool matchTypeOnly=false)
            {
                if(container is null||ph is null)return null;
                string type=(string?)ph.Attribute("type")??"obj",index=(string?)ph.Attribute("idx")??"0";
                return container.Element(P+"cSld")?.Element(P+"spTree")?.Elements(P+"sp").FirstOrDefault(candidate=>
                {
                    var p=candidate.Descendants(P+"ph").FirstOrDefault();if(p is null)return false;
                    string candidateType=(string?)p.Attribute("type")??"obj";
                    return matchTypeOnly ? ReadPlaceholder(p)==ReadPlaceholder(ph) : ((string?)p.Attribute("idx")??"0")==index;
                });
            }
            string ReadColor(XElement? container,string fallback)
            {
                if(container is null)return fallback;if(container.Element(A+"noFill")is not null)return "#00000000";
                var fill=container.Element(A+"solidFill")??container;var color=fill.Elements().FirstOrDefault(x=>x.Name==A+"srgbClr"||x.Name==A+"schemeClr"||x.Name==A+"sysClr");if(color is null)return fallback;
                string result=color.Name==A+"schemeClr"?themeColors.GetValueOrDefault((string?)color.Attribute("val")??"",fallback):"#"+((string?)color.Attribute("lastClr")??(string?)color.Attribute("val")??"000000");
                float alpha=Number(color.Element(A+"alpha"),"val",100000);if(alpha<100000&&result.Length==7)result="#"+((int)(Math.Clamp(alpha,0,100000)*255/100000)).ToString("X2")+result[1..];return result;
            }
            void Parse(XElement node,float tx=0,float ty=0,float sx=1,float sy=1)
            {
                if(shapes.Count>20000)throw new InvalidDataException("Too many slide objects.");
                if(node.Name==P+"grpSp")
                {
                    warnings.Add("Groups are imported as individually editable objects; complex group rotations are not preserved.");var x=node.Element(P+"grpSpPr")?.Element(A+"xfrm");float cx=Number(x?.Element(A+"chOff"),"x"),cy=Number(x?.Element(A+"chOff"),"y"),ex=Number(x?.Element(A+"ext"),"cx",1),ey=Number(x?.Element(A+"ext"),"cy",1),cw=Number(x?.Element(A+"chExt"),"cx",ex),ch=Number(x?.Element(A+"chExt"),"cy",ey);float nsx=sx*ex/Math.Max(1,cw),nsy=sy*ey/Math.Max(1,ch),ntx=tx+Number(x?.Element(A+"off"),"x")/Emu*sx-cx/Emu*nsx,nty=ty+Number(x?.Element(A+"off"),"y")/Emu*sy-cy/Emu*nsy;
                    foreach(var child in node.Elements().Where(e=>e.Name==P+"sp"||e.Name==P+"pic"||e.Name==P+"grpSp"||e.Name==P+"graphicFrame"))Parse(child,ntx,nty,nsx,nsy);return;
                }
                if(node.Name!=P+"sp"&&node.Name!=P+"pic"&&node.Name!=P+"cxnSp"&&node.Name!=P+"graphicFrame")return;
                var properties=node.Element(P+"spPr");var xfrm=properties?.Element(A+"xfrm")??node.Element(P+"xfrm");
                var ph=node.Descendants(P+"ph").FirstOrDefault();var layoutShape=MatchPlaceholder(layout,ph);var masterShape=MatchPlaceholder(master,layoutShape?.Descendants(P+"ph").FirstOrDefault()??ph,true);
                xfrm??=layoutShape?.Element(P+"spPr")?.Element(A+"xfrm")??masterShape?.Element(P+"spPr")?.Element(A+"xfrm");
                var off=xfrm?.Elements().FirstOrDefault(e=>e.Name.LocalName=="off");var ext=xfrm?.Elements().FirstOrDefault(e=>e.Name.LocalName=="ext");var bounds=new RectF(tx+Number(off,"x",762000)/Emu*sx,ty+Number(off,"y",762000)/Emu*sy,Math.Max(1,Number(ext,"cx",9144000)/Emu*sx),Math.Max(1,Number(ext,"cy",857250)/Emu*sy));
                var nv=node.Descendants(P+"cNvPr").FirstOrDefault();string name=(string?)nv?.Attribute("name")??"Imported object";bool hidden=(string?)nv?.Attribute("hidden") is "1" or "true";var preset=(string?)properties?.Element(A+"prstGeom")?.Attribute("prst")??"rect";
                var kind=preset switch{"ellipse"=>ShapeKind.Ellipse,"roundRect"=>ShapeKind.RoundRectangle,"triangle"=>ShapeKind.Triangle,"diamond"=>ShapeKind.Diamond,"line"=>ShapeKind.Line,_=>ShapeKind.Rectangle};if(node.Name==P+"cxnSp")kind=ShapeKind.Line;if(properties?.Element(A+"ln")?.Element(A+"tailEnd")is not null)kind=ShapeKind.Arrow;
                if((string?)node.Element(P+"nvSpPr")?.Element(P+"cNvSpPr")?.Attribute("txBox")is "1" or "true")kind=ShapeKind.Text;
                var body=node.Element(P+"txBody");var paragraph=body?.Element(A+"lstStyle")?.Element(A+"lvl1pPr");var run=paragraph?.Element(A+"defRPr")??layoutShape?.Descendants(A+"defRPr").FirstOrDefault()??masterShape?.Descendants(A+"defRPr").FirstOrDefault();string text="";
                var style=new TextStyle{FontSize=Math.Clamp(Number(run,"sz",2100)/75,1,512),FontFamily=(string?)run?.Element(A+"latin")?.Attribute("typeface")??"Arial",Bold=(string?)run?.Attribute("b")is "1" or "true",Italic=(string?)run?.Attribute("i")is "1" or "true",Underline=(string?)run?.Attribute("u")=="sng",Color=ReadColor(run,"#243247"),Alignment=(string?)paragraph?.Attribute("algn")switch{"ctr"=>ParagraphAlignment.Center,"r"=>ParagraphAlignment.Right,_=>ParagraphAlignment.Left},Bullets=paragraph?.Element(A+"buChar")is not null,VerticalAlignment=(string?)body?.Element(A+"bodyPr")?.Attribute("anchor")switch{"ctr"=>VerticalAlignment.Middle,"b"=>VerticalAlignment.Bottom,_=>VerticalAlignment.Top}};
                // A sibling paragraph is never a source of inherited body defaults.
                style=ReadParagraphStyle(paragraph,style);
                var shape=new SlideShape{Kind=kind,Name=name,AlternativeText=(string?)nv?.Attribute("descr")??"",Placeholder=ReadPlaceholder(ph),PlaceholderIndex=Math.Max(0,(int)Number(ph,"idx")),Bounds=bounds,Hidden=hidden,Rotation=Number(xfrm,"rot")/60000,Text=text,TextStyle=style,Fill=ReadColor(properties,kind==ShapeKind.Text?"#00000000":"#D35230"),Stroke=ReadColor(properties?.Element(A+"ln"),"#00000000"),StrokeWidth=Number(properties?.Element(A+"ln"),"w",14288)/Emu};
                shape=ReadRichText(shape,body,ReadColor);
                if(body is not null)shape=ReadTextBox(shape,warnings,body.Element(A+"bodyPr"),layoutShape?.Element(P+"txBody")?.Element(A+"bodyPr"),masterShape?.Element(P+"txBody")?.Element(A+"bodyPr"));
                if(node.Name==P+"pic")
                {
                    string imageId=(string?)node.Descendants(A+"blip").FirstOrDefault()?.Attribute(R+"embed")??"";if(!rels.TryGetValue(imageId,out var imageRel)){warnings.Add("A linked or unsupported picture was omitted.");return;}
                    if(!assetPaths.TryGetValue(imageRel.Path,out var aid)){var entry=zip.GetEntry(imageRel.Path);if(entry is null)return;string extension=Path.GetExtension(imageRel.Path).ToLowerInvariant();if(extension is not (".png" or ".jpg" or ".jpeg" or ".gif" or ".webp")){warnings.Add("An unsupported image format was omitted.");return;}using var stream=entry.Open();using var bytes=new MemoryStream();stream.CopyTo(bytes);aid=Guid.NewGuid().ToString("N");assetPaths[imageRel.Path]=aid;assets[aid]=new(aid,extension is ".jpg" or ".jpeg"?"image/jpeg":extension==".gif"?"image/gif":extension==".webp"?"image/webp":"image/png",Convert.ToBase64String(bytes.ToArray()));}
                    shape=shape with{Kind=ShapeKind.Image,AssetId=aid,Fill="#00000000"};
                }
                if(node.Name==P+"graphicFrame")
                {
                    var table=node.Descendants(A+"tbl").FirstOrDefault();if(table is not null){shape=ReadNativeTable(shape,table,warnings,ReadColor);}
                    else
                    {
                        var chart=node.Descendants(C+"chart").FirstOrDefault();string chartId=(string?)chart?.Attribute(R+"id")??"";
                        if(chart is not null&&rels.TryGetValue(chartId,out var chartRel))
                        {
                            var imported=ReadNativeChart(shape,Read(chartRel.Path),warnings,ReadColor);if(imported is null)return;shape=imported;
                        }
                        else {warnings.Add("SmartArt, media and unsupported graphic frames are omitted on import.");return;}
                    }
                }
                if(properties?.Element(A+"custGeom")is not null||!new[]{"rect","ellipse","roundRect","triangle","diamond","line"}.Contains(preset))warnings.Add("Unsupported preset and custom geometries are approximated as rectangles.");
                shapes.Add(shape);
            }
            if(tree is not null)foreach(var node in tree.Elements())Parse(node);
            string notes="";var notesRel=rels.Values.FirstOrDefault(x=>x.Type.EndsWith("/notesSlide"));if(notesRel.Path is not null){var notesRoot=Read(notesRel.Path);notes=string.Join('\n',notesRoot.Descendants(P+"sp").Where(s=>(string?)s.Descendants(P+"ph").FirstOrDefault()?.Attribute("type")=="body").SelectMany(s=>s.Descendants(A+"p")).Select(p=>string.Concat(p.Descendants(A+"t").Select(t=>t.Value))));}
            var transition=root.Element(P+"transition")?.Elements().FirstOrDefault();var slide=new Slide{LayoutName=(string?)layout?.Element(P+"cSld")?.Attribute("name"),Name=(string?)root.Element(P+"cSld")?.Attribute("name")??shapes.FirstOrDefault(s=>!string.IsNullOrWhiteSpace(s.Text))?.Text.Split('\n')[0]??$"Slide {slides.Count+1}",Background=ReadColor(root.Element(P+"cSld")?.Element(P+"bg")?.Element(P+"bgPr"),ReadColor(layout?.Element(P+"cSld")?.Element(P+"bg")?.Element(P+"bgPr"),ReadColor(master?.Element(P+"cSld")?.Element(P+"bg")?.Element(P+"bgPr"),"#FFFFFF"))),Shapes=shapes.ToImmutable(),Notes=notes,Hidden=(string?)root.Attribute("show")=="0",Transition=transition?.Name.LocalName switch{"fade"=>TransitionKind.Fade,"push"=>TransitionKind.Push,"wipe"=>TransitionKind.Wipe,_=>TransitionKind.None}};slides.Add(slide);
            if(root.Element(P+"timing")is not null)warnings.Add("PowerPoint animation timelines are not imported.");
        }
        if(slides.Count==0)throw new InvalidDataException("No presentation slides were found.");
        warnings.Add("PPTX import supports a subset. Master artwork, inherited theme formatting, advanced effects and unsupported objects may differ. Keep the original PPTX.");
        var document=new PresentationDocument{Title=title,Width=width,Height=height,Slides=slides.ToImmutable(),Assets=assets.ToImmutable()};DocumentSerializer.Validate(document);return new(document,warnings.ToArray());
    }
    private static float Number(XElement? element,string name,float fallback=0)=>float.TryParse((string?)element?.Attribute(name),NumberStyles.Float,CultureInfo.InvariantCulture,out var value)&&float.IsFinite(value)?value:fallback;
}
