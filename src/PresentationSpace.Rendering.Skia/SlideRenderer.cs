using System.Globalization;
using System.Text;
using PresentationSpace.Core;
using SkiaSharp;

namespace PresentationSpace.Rendering.Skia;

/// <summary>Disposable, single-thread-affine renderer. Coordinates are 96-DPI slide units.</summary>
public sealed class SlideRenderer : IDisposable
{
    private sealed record CachedImage(SKImage Image, string Source, long Bytes, long Used);
    private readonly Dictionary<string, CachedImage> _images = [];
    private readonly Dictionary<string, SKTypeface> _faces = [];
    private long _clock, _imageBytes;
    public long ImageCacheBudget { get; set; } = 64 * 1024 * 1024;
    public static SKColor Color(string? value, SKColor? fallback = null) => value is not null && SKColor.TryParse(value, out var c) ? c : fallback ?? SKColors.Transparent;
    public void Render(SKCanvas canvas, PresentationDocument document, Slide slide, float animationTime = float.PositiveInfinity)
    {
        canvas.Save(); canvas.ClipRect(new(0,0,document.Width,document.Height)); canvas.Clear(Color(slide.Background,SKColors.White));
        foreach (var shape in slide.Shapes)
        {
            if (shape.Hidden) continue;
            float progress = shape.Animation == AnimationKind.None ? 1 : Math.Clamp((animationTime - shape.AnimationOrder * .35f) / Math.Max(.01f,shape.AnimationDuration),0,1);
            if (progress <= 0) continue;
            canvas.Save();
            var b = shape.Bounds;
            canvas.RotateDegrees(shape.Rotation,b.Center.X,b.Center.Y);
            if (shape.Animation == AnimationKind.FlyIn) canvas.Translate(0,80*(1-progress));
            using var layer = new SKPaint { Color = SKColors.White.WithAlpha((byte)(255*shape.Opacity*(shape.Animation == AnimationKind.Fade ? progress : 1))) };
            canvas.SaveLayer(layer);
            DrawShape(canvas,document,shape);
            canvas.Restore(); canvas.Restore();
        }
        canvas.Restore();
    }
    private SKTypeface Face(TextStyle s)
    {
        string key = $"{s.FontFamily}|{s.Bold}|{s.Italic}";
        if (!_faces.TryGetValue(key,out var face))
        {
            face = SKTypeface.FromFamilyName(s.FontFamily,new SKFontStyle(s.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,SKFontStyleWidth.Normal,s.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright));
            _faces[key] = face;
        }
        return face;
    }
    private void DrawShape(SKCanvas c, PresentationDocument d, SlideShape s)
    {
        var b = s.Bounds; var r = new SKRect(b.X,b.Y,b.Right,b.Bottom);
        using var fill = new SKPaint { IsAntialias = true, Color = Color(s.Fill) };
        using var stroke = new SKPaint { IsAntialias = true, Color = Color(s.Stroke), Style = SKPaintStyle.Stroke, StrokeWidth = s.StrokeWidth, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
        switch (s.Kind)
        {
            case ShapeKind.Ellipse: c.DrawOval(r,fill); if (s.StrokeWidth>0) c.DrawOval(r,stroke); break;
            case ShapeKind.RoundRectangle: c.DrawRoundRect(r,Math.Min(18,b.Width/8),Math.Min(18,b.Height/8),fill); if(s.StrokeWidth>0)c.DrawRoundRect(r,Math.Min(18,b.Width/8),Math.Min(18,b.Height/8),stroke); break;
            case ShapeKind.Triangle:
            case ShapeKind.Diamond:
                using (var path = new SKPath())
                {
                    path.MoveTo(b.Center.X,b.Y); path.LineTo(b.Right,s.Kind == ShapeKind.Diamond ? b.Center.Y : b.Bottom);
                    if(s.Kind == ShapeKind.Diamond) path.LineTo(b.Center.X,b.Bottom);
                    path.LineTo(b.X,s.Kind == ShapeKind.Diamond ? b.Center.Y : b.Bottom); path.Close(); c.DrawPath(path,fill); if(s.StrokeWidth>0)c.DrawPath(path,stroke);
                }
                break;
            case ShapeKind.Line:
            case ShapeKind.Arrow:
                c.DrawLine(b.X,b.Y,b.Right,b.Bottom,stroke);
                if(s.Kind == ShapeKind.Arrow)
                {
                    float a = MathF.Atan2(b.Height,b.Width), size = Math.Max(12,s.StrokeWidth*4);
                    using var arrow = new SKPath(); arrow.MoveTo(b.Right,b.Bottom); arrow.LineTo(b.Right-size*MathF.Cos(a-.5f),b.Bottom-size*MathF.Sin(a-.5f)); arrow.LineTo(b.Right-size*MathF.Cos(a+.5f),b.Bottom-size*MathF.Sin(a+.5f)); arrow.Close(); fill.Color=stroke.Color; c.DrawPath(arrow,fill);
                }
                break;
            case ShapeKind.Image:
                var image = GetImage(d,s.AssetId);
                if(image is not null)
                {
                    c.Save(); c.ClipRect(r); float scale = Math.Min(b.Width/image.Width,b.Height/image.Height); float w=image.Width*scale,h=image.Height*scale;
                    c.DrawImage(image,new SKRect(b.Center.X-w/2,b.Center.Y-h/2,b.Center.X+w/2,b.Center.Y+h/2),new SKSamplingOptions(SKFilterMode.Linear,SKMipmapMode.Linear)); c.Restore();
                }
                else { fill.Color=Color("#F0F1F4"); c.DrawRect(r,fill); DrawText(c,"Picture unavailable",b,new(){FontSize=20,Alignment=ParagraphAlignment.Center,VerticalAlignment=Core.VerticalAlignment.Middle}); }
                break;
            case ShapeKind.Table: DrawTable(c,s); break;
            case ShapeKind.Chart: DrawChart(c,s); break;
            default: c.DrawRect(r,fill); if(s.StrokeWidth>0)c.DrawRect(r,stroke); break;
        }
        if (!string.IsNullOrEmpty(s.Text)) DrawText(c,s.Text,b,s.TextStyle,s.Kind == ShapeKind.Text ? 3 : 12);
    }
    public void DrawText(SKCanvas c, string text, RectF b, TextStyle style, float padding = 3)
    {
        using var font = new SKFont(Face(style),style.FontSize) { Edging=SKFontEdging.SubpixelAntialias, Subpixel=true };
        using var paint = new SKPaint { IsAntialias=true,Color=Color(style.Color,SKColors.Black) };
        float width = Math.Max(1,b.Width-padding*2), height = Math.Max(1,b.Height-padding*2);
        var lines = Wrap(text,font,width,style.Bullets).ToArray(); var metrics=font.Metrics;
        float lineHeight=Math.Max(style.FontSize,metrics.Descent-metrics.Ascent)*style.LineSpacing;
        float used=lines.Length*lineHeight, top=style.VerticalAlignment switch { Core.VerticalAlignment.Middle => Math.Max(0,(height-used)/2), Core.VerticalAlignment.Bottom => Math.Max(0,height-used), _=>0 };
        c.Save(); c.ClipRect(new SKRect(b.X,b.Y,b.Right,b.Bottom)); float y=b.Y+padding+top-metrics.Ascent;
        foreach(var line in lines)
        {
            float measure=font.MeasureText(line), x=b.X+padding+(style.Alignment switch {ParagraphAlignment.Center=>(width-measure)/2,ParagraphAlignment.Right=>width-measure,_=>0});
            c.DrawText(line,x,y,SKTextAlign.Left,font,paint);
            if(style.Underline)c.DrawRect(x,y+Math.Max(2,style.FontSize*.09f),measure,Math.Max(1,style.FontSize*.045f),paint);
            y+=lineHeight; if(y>b.Bottom+lineHeight)break;
        }
        c.Restore();
    }
    private static IEnumerable<string> Wrap(string text,SKFont font,float width,bool bullets)
    {
        foreach(string paragraph in text.Replace("\r","").Split('\n'))
        {
            string line=""; var words=paragraph.Split(' ');
            if(bullets)line="• ";
            foreach(var word in words)
            {
                string candidate=line+(line.Length>0 && !line.EndsWith(' ') ? " " : "")+word;
                if(font.MeasureText(candidate)<=width) {line=candidate; continue;}
                if(line.Length>0){yield return line;line="";}
                var elements=StringInfo.GetTextElementEnumerator(word);
                while(elements.MoveNext())
                {
                    string next=elements.GetTextElement();
                    if(line.Length>0 && font.MeasureText(line+next)>width){yield return line;line="";}
                    line+=next;
                }
            }
            yield return line;
        }
    }
    private void DrawTable(SKCanvas c,SlideShape s)
    {
        int columns=Math.Clamp(s.TableColumns,1,100), rows=Math.Max(1,(s.Cells.Length+columns-1)/columns); var b=s.Bounds; float w=b.Width/columns,h=b.Height/rows;
        using var paint=new SKPaint { IsAntialias=true };
        using var line=new SKPaint { Color=Color("#D8DEE8"),Style=SKPaintStyle.Stroke,StrokeWidth=1 };
        for(int row=0;row<rows;row++)for(int col=0;col<columns;col++)
        {
            var cell=new RectF(b.X+col*w,b.Y+row*h,w,h); paint.Color=Color(row==0?s.Fill:row%2==0?"#F1F4F8":"#FFFFFF"); c.DrawRect(cell.X,cell.Y,w,h,paint); c.DrawRect(cell.X,cell.Y,w,h,line);
            int index=row*columns+col; if(index<s.Cells.Length)DrawText(c,s.Cells[index],cell,s.TextStyle with { FontSize=Math.Min(s.TextStyle.FontSize,Math.Max(9,h*.35f)),Color=row==0?"#FFFFFF":s.TextStyle.Color,Bold=row==0,VerticalAlignment=Core.VerticalAlignment.Middle },10);
        }
    }
    private void DrawChart(SKCanvas c,SlideShape s)
    {
        var b=s.Bounds; var values=s.Values.IsEmpty ? new float[]{42,68,54,89} : s.Values.ToArray(); int n=values.Length;
        float min=Math.Min(0,values.Min()),max=Math.Max(1,values.Max()),range=Math.Max(1,max-min); var area=new RectF(b.X+40,b.Y+22,b.Width-55,b.Height-70);
        using var grid=new SKPaint { Color=Color("#E2E6ED"),StrokeWidth=1,IsAntialias=true };
        for(int i=0;i<=4;i++) {float y=area.Y+area.Height*i/4;c.DrawLine(area.X,y,area.Right,y,grid);DrawText(c,(max-range*i/4).ToString("0.#",CultureInfo.InvariantCulture),new(b.X,y-9,34,24),new(){FontSize=13,Color="#7C8492",Alignment=ParagraphAlignment.Right});}
        float slot=area.Width/n,zero=area.Y+area.Height*max/range;
        using var bar=new SKPaint{Color=Color(s.Fill),IsAntialias=true};
        for(int i=0;i<n;i++)
        {
            float y=area.Y+area.Height*(max-values[i])/range,x=area.X+i*slot+slot*.2f,w=slot*.6f;
            c.DrawRoundRect(new SKRect(x,Math.Min(zero,y),x+w,Math.Max(zero,y)),3,3,bar);
            DrawText(c,values[i].ToString("0.#",CultureInfo.InvariantCulture),new(x-10,y-27,w+20,26),new(){FontSize=16,Color=s.TextStyle.Color,Bold=true,Alignment=ParagraphAlignment.Center});
            DrawText(c,i<s.Labels.Length?s.Labels[i]:$"{i+1}",new(area.X+i*slot,area.Bottom+10,slot,35),new(){FontSize=16,Color="#667487",Alignment=ParagraphAlignment.Center});
        }
    }
    private SKImage? GetImage(PresentationDocument document,string? id)
    {
        if(id is null || !document.Assets.TryGetValue(id,out var asset))return null;
        if(_images.TryGetValue(id,out var cached))
        {
            if(cached.Source==asset.Base64){_images[id]=cached with{Used=++_clock};return cached.Image;}
            cached.Image.Dispose();_imageBytes-=cached.Bytes;_images.Remove(id);
        }
        try
        {
            using var data=SKData.CreateCopy(Convert.FromBase64String(asset.Base64)); using var codec=SKCodec.Create(data);
            if(codec is null || (long)codec.Info.Width*codec.Info.Height>16_000_000)return null;
            var image=SKImage.FromEncodedData(data);if(image is null)return null;
            long bytes=(long)image.Width*image.Height*4;
            while(_images.Count>0 && _imageBytes+bytes>ImageCacheBudget){var oldest=_images.MinBy(x=>x.Value.Used);oldest.Value.Image.Dispose();_imageBytes-=oldest.Value.Bytes;_images.Remove(oldest.Key);}
            _images[id]=new(image,asset.Base64,bytes,++_clock);_imageBytes+=bytes;return image;
        }
        catch(FormatException){return null;}
    }
    public void DrawSelection(SKCanvas c,IEnumerable<SlideShape> selected,float scale)
    {
        float inv=1/Math.Max(.02f,scale);using var line=new SKPaint{Color=Color("#D35230"),Style=SKPaintStyle.Stroke,StrokeWidth=1.3f*inv,IsAntialias=true};using var white=new SKPaint{Color=SKColors.White,IsAntialias=true};
        var items=selected.ToArray();
        foreach(var shape in items)
        {
            var b=shape.Bounds;c.Save();c.RotateDegrees(shape.Rotation,b.Center.X,b.Center.Y);c.DrawRect(b.X,b.Y,b.Width,b.Height,line);
            if(items.Length==1)
            {
                c.DrawLine(b.Center.X,b.Y,b.Center.X,b.Y-27*inv,line);c.DrawCircle(b.Center.X,b.Y-31*inv,4*inv,white);c.DrawCircle(b.Center.X,b.Y-31*inv,4*inv,line);
                foreach(var p in Geometry.Handles(b)){c.DrawRect(p.X-3.5f*inv,p.Y-3.5f*inv,7*inv,7*inv,white);c.DrawRect(p.X-3.5f*inv,p.Y-3.5f*inv,7*inv,7*inv,line);}
            }
            c.Restore();
        }
    }
    public byte[] ExportPng(PresentationDocument document,Slide slide,int width=1920)
    {
        width=Math.Clamp(width,64,8192);int height=(int)Math.Ceiling(width*document.Height/document.Width);
        if((long)width*height>32_000_000)throw new InvalidOperationException("Export is limited to 32 megapixels.");
        using var surface=SKSurface.Create(new SKImageInfo(width,height,SKColorType.Rgba8888,SKAlphaType.Premul));surface.Canvas.Scale(width/document.Width);Render(surface.Canvas,document,slide);
        using var image=surface.Snapshot();using var data=image.Encode(SKEncodedImageFormat.Png,100);return data.ToArray();
    }
    public byte[] ExportPdf(PresentationDocument document)
    {
        using var output=new MemoryStream();using(var pdf=SKDocument.CreatePdf(output))
        {
            foreach(var slide in document.Slides.Where(s=>!s.Hidden)){var canvas=pdf.BeginPage(document.Width*.75f,document.Height*.75f);canvas.Scale(.75f);Render(canvas,document,slide);pdf.EndPage();}pdf.Close();
        }
        return output.ToArray();
    }
    public void Dispose(){foreach(var i in _images.Values)i.Image.Dispose();foreach(var f in _faces.Values)f.Dispose();_images.Clear();_faces.Clear();_imageBytes=0;}
}
