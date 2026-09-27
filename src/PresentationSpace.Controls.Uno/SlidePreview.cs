using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PresentationSpace.Core;
using PresentationSpace.Rendering.Skia;
using SkiaSharp.Views.Windows;
namespace PresentationSpace.Controls.Uno;

public sealed class SlidePreview : UserControl
{
    private readonly SKXamlCanvas _canvas=new();
    private readonly SlideRenderer _renderer=new();
    private PresentationDocument? _document;
    private Slide? _slide;
    public SlidePreview(){Content=_canvas;_canvas.PaintSurface+=Paint;SizeChanged+=(_,_)=>_canvas.Invalidate();Unloaded+=(_,_)=>_renderer.Dispose();}
    public void SetSlide(PresentationDocument document,Slide slide)
    {
        if(ReferenceEquals(_slide,slide)&&_document?.Width==document.Width&&_document?.Height==document.Height)return;
        _document=document;_slide=slide;_canvas.Invalidate();
    }
    private void Paint(object? sender,SKPaintSurfaceEventArgs e)
    {
        if(_document is not {} d||_slide is not {} slide)return;
        var c=e.Surface.Canvas;c.ResetMatrix();c.Save();c.Scale(e.Info.Width/d.Width,e.Info.Height/d.Height);_renderer.Render(c,d,slide);c.Restore();
    }
}
