using System.Collections.Immutable;
using PresentationSpace.Core;
using SkiaSharp;

namespace PresentationSpace.Rendering.Skia;

/// <summary>Original editable picture sample. One raster is reused by six independently framed native pictures.</summary>
public static class PictureLayoutSample
{
    public static PresentationDocument Create()
    {
        using var surface = SKSurface.Create(new SKImageInfo(640,400)); var canvas = surface.Canvas;
        canvas.Clear(SlideRenderer.Color("#A9D6EB"));
        using var paint = new SKPaint { IsAntialias = true };
        paint.Color = SlideRenderer.Color("#FFD677"); canvas.DrawCircle(470,94,43,paint);
        using var hills = new SKPath(); hills.MoveTo(0,285); hills.LineTo(170,104); hills.LineTo(390,325); hills.LineTo(540,156); hills.LineTo(640,274); hills.LineTo(640,400); hills.LineTo(0,400); hills.Close();
        paint.Color = SlideRenderer.Color("#1E6D71"); canvas.DrawPath(hills,paint);
        paint.Color = SlideRenderer.Color("#133D53"); canvas.DrawRect(0,330,640,70,paint);
        paint.Color = SlideRenderer.Color("#F38659"); canvas.DrawRoundRect(new SKRect(45,224,142,330),5,5,paint);
        paint.Color = SKColors.White; canvas.DrawRect(62,242,25,32,paint); canvas.DrawRect(104,242,21,32,paint);
        using var image = surface.Snapshot(); using var encoded = image.Encode(SKEncodedImageFormat.Png,100);
        string id = Guid.NewGuid().ToString("N"); var asset = new PresentationAsset(id,"image/png",Convert.ToBase64String(encoded.ToArray()));
        var shapes = ImmutableArray.CreateBuilder<SlideShape>();
        shapes.Add(SlideFactory.Text("Pictures that keep their framing",52,34,1160,58,42,"#17384D"));
        shapes.Add(SlideFactory.Text("One source image. Editable crops, proportions, flips and transparency.",54,99,1140,32,21,"#526777"));
        void Picture(string label, float x, float y, PictureSpec spec, float rotation = 0)
        {
            shapes.Add(SlideFactory.Text(label,x,y-35,350,28,19,"#17384D"));
            shapes.Add(new() { Name = label, Kind = ShapeKind.Image, AssetId = id, Bounds = new(x,y,350,180),
                Picture = spec, Fill = "#E4EAF0", Stroke = "#FFFFFF", StrokeWidth = 2, Rotation = rotation });
        }
        Picture("FIT · entire source",54,190,new() {Fit=PictureFit.Contain});
        Picture("FILL · proportional crop",462,190,new() {Fit=PictureFit.Cover});
        Picture("STRETCH · full frame",870,190,new());
        Picture("CROP · left 20%, top 10%",54,458,new() {Source=new(.2f,.1f,0,0)});
        Picture("FLIP · horizontal + ellipse",462,458,new() {Fit=PictureFit.Cover,FlipHorizontal=true,Mask=PictureMask.Ellipse});
        Picture("TRANSPARENCY · 50%",870,458,new() {Fit=PictureFit.Cover,Opacity=.5f});
        shapes.Add(SlideFactory.Text("Picture Format  /  Crop & Layout     •     The original pixels remain embedded.",54,671,1170,26,16,"#526777"));
        return new() { Title = "Picture layout", Slides = [new() {Name="Pictures · framing and transparency",Background="#F5F7FA",Shapes=shapes.ToImmutable()}], Assets=ImmutableDictionary<string,PresentationAsset>.Empty.Add(id,asset) };
    }
}
