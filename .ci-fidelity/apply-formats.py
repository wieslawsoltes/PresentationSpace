from pathlib import Path
p=Path('src/PresentationSpace.Formats/PptxCodec.cs');s=p.read_text()
s=s.replace('public static class PptxCodec','public static partial class PptxCodec')
a=s.index('    private static XElement TextBody(');b=s.index('    private static XElement Shape(',a)
s=s[:a]+'''    private static XElement TextBody(string text,TextStyle style) => RichTextBody(new SlideShape { Text = text, TextStyle = style });
'''+s[b:]
s=s.replace('V("name",s.Name),V("hidden"','V("name",s.Name),V("descr",s.AlternativeText),V("hidden"')
s=s.replace('new XElement(P+"nvPr")),new XElement(P+"spPr",Transform(s)', 'new XElement(P+"nvPr",s.Placeholder == PlaceholderKind.None ? null : Placeholder(s))),new XElement(P+"spPr",Transform(s)')
s=s.replace('TextBody(s.Text,s.TextStyle));','RichTextBody(s));')
s=s.replace('foreach(var shape in Expand(original,warnings))','foreach(var shape in new[]{original})')
s=s.replace('                        string? imageRid=null;', '''                        if(shape.Kind==ShapeKind.Table){shapes.Add(NativeTable(shape,objectId++));continue;}
                        if(shape.Kind==ShapeKind.Chart){shapes.Add(NativeChart(zip,types,shape,objectId++,number,rels));continue;}
                        string? imageRid=null;''')
a=s.index('    private static IEnumerable<SlideShape> Expand(');b=s.index('    private static XElement Theme()',a);s=s[:a]+s[b:]
s=s.replace('Relation("rId1","slideLayout","../slideLayouts/slideLayout1.xml"),Relation("rId2","notesSlide"', 'Relation("rId1","slideLayout",$"../slideLayouts/slideLayout{Math.Max(0,SlideLayoutEngine.Layouts.IndexOf(slide.LayoutName??"Blank"))+1}.xml"),Relation("rId2","notesSlide"')
s=s.replace('new XElement(P+"sldLayoutIdLst",new XElement(P+"sldLayoutId",V("id",2147483649L),new XAttribute(R+"id","rId1")))', 'new XElement(P+"sldLayoutIdLst",SlideLayoutEngine.Layouts.Select((_,i)=>new XElement(P+"sldLayoutId",V("id",2147483649L+i),new XAttribute(R+"id","rId"+(i+1)))))')
s=s.replace('Relations([Relation("rId1","slideLayout","../slideLayouts/slideLayout1.xml"),Relation("rId2","theme","../theme/theme1.xml")])', 'Relations(SlideLayoutEngine.Layouts.Select((_,i)=>Relation("rId"+(i+1),"slideLayout",$"../slideLayouts/slideLayout{i+1}.xml")).Append(Relation("rIdTheme","theme","../theme/theme1.xml")))')
a=s.index('            Xml("ppt/slideLayouts/slideLayout1.xml"');b=s.index('            Xml("ppt/notesMasters/notesMaster1.xml"',a)
s=s[:a]+'''            for(int i=0;i<SlideLayoutEngine.Layouts.Length;i++)
            {
                var name=SlideLayoutEngine.Layouts[i];var template=SlideFactory.Create(name,document.Width,document.Height);
                string type=name switch{"Title slide"=>"title","Title and content"=>"obj","Two content"=>"twoObj","Title only"=>"titleOnly",_=>"blank"};
                Xml($"ppt/slideLayouts/slideLayout{i+1}.xml",Root(P+"sldLayout",V("type",type),V("preserve",1),new XElement(P+"cSld",V("name",name),GroupTree(template.Shapes.Select((shape,j)=>(object)Shape(shape,j+2)).ToArray())),ColorMapOverride()),PresentationContent+"slideLayout+xml");
                Xml($"ppt/slideLayouts/_rels/slideLayout{i+1}.xml.rels",Relations([Relation("rId1","slideMaster","../slideMasters/slideMaster1.xml")]));
            }
'''+s[b:]
a=s.index('            XElement? layout=null;');b=s.index('            string ReadColor(',a)
s=s[:a]+'''            XElement? layout=null,master=null;var layoutRel=rels.Values.FirstOrDefault(r=>r.Type.EndsWith("/slideLayout"));
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
'''+s[b:]
a=s.index('                if(xfrm is null)');b=s.index('                var off=xfrm?',a)
s=s[:a]+'''                var ph=node.Descendants(P+"ph").FirstOrDefault();var layoutShape=MatchPlaceholder(layout,ph);var masterShape=MatchPlaceholder(master,layoutShape?.Descendants(P+"ph").FirstOrDefault()??ph,true);
                xfrm??=layoutShape?.Element(P+"spPr")?.Element(A+"xfrm")??masterShape?.Element(P+"spPr")?.Element(A+"xfrm");
'''+s[b:]
s=s.replace('var body=node.Element(P+"txBody");var run=body?.Descendants(A+"rPr").FirstOrDefault();var paragraph=body?.Element(A+"p")?.Element(A+"pPr");', 'var body=node.Element(P+"txBody");var paragraph=body?.Element(A+"p")?.Element(A+"pPr");var run=paragraph?.Element(A+"defRPr")??body?.Element(A+"lstStyle")?.Element(A+"lvl1pPr")?.Element(A+"defRPr")??layoutShape?.Descendants(A+"defRPr").FirstOrDefault()??masterShape?.Descendants(A+"defRPr").FirstOrDefault();')
s=s.replace('Kind=kind,Name=name,Bounds=bounds,Hidden=hidden,','Kind=kind,Name=name,AlternativeText=(string?)nv?.Attribute("descr")??"",Placeholder=ReadPlaceholder(ph),PlaceholderIndex=Math.Max(0,(int)Number(ph,"idx")),Bounds=bounds,Hidden=hidden,')
s=s.replace('                if(node.Name==P+"pic")\n','                shape=ReadRichText(shape,body,ReadColor);\n                if(node.Name==P+"pic")\n')
s=s.replace('else {warnings.Add("Native charts, SmartArt, media and unsupported graphic frames are omitted on import.");return;}', '''else
                    {
                        var chart=node.Descendants(C+"chart").FirstOrDefault();string chartId=(string?)chart?.Attribute(R+"id")??"";
                        if(chart is not null&&rels.TryGetValue(chartId,out var chartRel))
                        {
                            var imported=ReadNativeChart(shape,Read(chartRel.Path),warnings,ReadColor);if(imported is null)return;shape=imported;
                        }
                        else {warnings.Add("SmartArt, media and unsupported graphic frames are omitted on import.");return;}
                    }''')
s=s.replace('                if(body?.Descendants(A+"rPr").Select(r=>r.ToString()).Distinct().Count()>1)warnings.Add("Mixed rich-text runs are simplified to one style per text box.");shapes.Add(shape);', '                shapes.Add(shape);')
s=s.replace('var slide=new Slide{Name=', 'var slide=new Slide{LayoutName=(string?)layout?.Element(P+"cSld")?.Attribute("name"),Name=')
s=s.replace('Background=ReadColor(root.Element(P+"cSld")?.Element(P+"bg")?.Element(P+"bgPr"),"#FFFFFF")','Background=ReadColor(root.Element(P+"cSld")?.Element(P+"bg")?.Element(P+"bgPr"),ReadColor(layout?.Element(P+"cSld")?.Element(P+"bg")?.Element(P+"bgPr"),ReadColor(master?.Element(P+"cSld")?.Element(P+"bg")?.Element(P+"bgPr"),"#FFFFFF")))')
p.write_text(s)
