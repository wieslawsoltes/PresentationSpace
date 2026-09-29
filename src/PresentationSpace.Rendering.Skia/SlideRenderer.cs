using System.Globalization;
using PresentationSpace.Core;
using SkiaSharp;

namespace PresentationSpace.Rendering.Skia;

/// <summary>Disposable, single-thread-affine renderer. Coordinates are 96-DPI slide units.</summary>
public sealed partial class SlideRenderer : IDisposable
{
    private sealed record CachedImage(SKImage Image, string Source, long Bytes, long Used);
    private readonly Dictionary<string, CachedImage> _images = [];
    private long _clock, _imageBytes;
    public long ImageCacheBudget { get; set; } = 64 * 1024 * 1024;
    public ITypefaceResolver? TypefaceResolver { get; set; }
    /// <summary>Optional application-wide provider. Fonts remain owned by the provider.</summary>
    public static ITypefaceResolver? DefaultTypefaceResolver { get; set; }
    public static SKColor Color(string? value, SKColor? fallback = null) => value is not null && SKColor.TryParse(value, out var c) ? c : fallback ?? SKColors.Transparent;

    public void Render(SKCanvas canvas, PresentationDocument document, Slide slide, float animationTime = float.PositiveInfinity)
    {
        PrepareCache();
        if (float.IsPositiveInfinity(animationTime) && DrawRetainedScene(canvas, document, slide)) return;
        RenderContents(canvas, document, slide, animationTime);
    }
    private void RenderContents(SKCanvas canvas, PresentationDocument document, Slide slide, float animationTime)
    {
        canvas.Save();
        try
        {
            canvas.ClipRect(new(0, 0, document.Width, document.Height));
            using (var background = new SKPaint { Color = Color(slide.Background, SKColors.White) })
                canvas.DrawRect(0, 0, document.Width, document.Height, background);
            foreach (var shape in slide.Shapes)
            {
                if (shape.Hidden || shape.Opacity <= 0) continue;
                float progress = shape.Animation == AnimationKind.None ? 1 : Math.Clamp((animationTime - shape.AnimationOrder * .35f) / Math.Max(.01f, shape.AnimationDuration), 0, 1);
                if (progress <= 0) continue;
                canvas.Save();
                try
                {
                    var bounds = shape.Bounds;
                    canvas.RotateDegrees(shape.Rotation, bounds.Center.X, bounds.Center.Y);
                    if (shape.Animation == AnimationKind.FlyIn) canvas.Translate(0, 80 * (1 - progress));
                    float opacity = shape.Opacity * (shape.Animation == AnimationKind.Fade ? progress : 1);
                    if (opacity < .9999f)
                    {
                        using var layer = new SKPaint { Color = SKColors.White.WithAlpha((byte)(255 * opacity)) };
                        canvas.SaveLayer(layer);
                        try { DrawRetainedShape(canvas, document, shape); }
                        finally { canvas.Restore(); }
                    }
                    else DrawRetainedShape(canvas, document, shape);
                }
                finally { canvas.Restore(); }
            }
        }
        finally { canvas.Restore(); }
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
        if (!string.IsNullOrEmpty(s.Text))
        {
            DrawRichText(c,s,s.Kind == ShapeKind.Text ? 3 : 12);
        }
    }
    public void DrawText(SKCanvas c, string text, RectF b, TextStyle style, float padding = 3) =>
        TextLayout.Draw(c, text, style, b, padding);
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
            if(items.Length==1 && !shape.Locked)
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
    public void Dispose(){ClearRenderCache();_tableLayouts.Clear();foreach(var i in _images.Values)i.Image.Dispose();_images.Clear();_imageBytes=0;}
}
