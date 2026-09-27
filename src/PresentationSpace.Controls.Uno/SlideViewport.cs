using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PresentationSpace.Core;
using PresentationSpace.Rendering.Skia;
using SkiaSharp;
using SkiaSharp.Views.Windows;
using Windows.System;
using Windows.UI.Core;
using CPoint=PresentationSpace.Core.PointF;
using XVertical=Microsoft.UI.Xaml.VerticalAlignment;

namespace PresentationSpace.Controls.Uno;

public sealed class SlideViewport : UserControl
{
    private readonly SKXamlCanvas _surface=new();
    private readonly Canvas _overlay=new();
    private readonly SlideRenderer _renderer=new();
    private EditorSession? _session;
    private CPoint _down;
    private RectF? _marquee;
    private int _handle=-1;
    private bool _dragging;
    private bool _additive;
    private float _scale=1,_ox,_oy;
    private TextBox? _editor;
    private Guid? _editingId;
    public float Zoom{get;private set;}=1;
    public bool FitToWindow{get;private set;}=true;
    public bool ShowGrid{get;set;}
    public bool ShowGuides{get;set;}
    public event EventHandler? ZoomChanged;
    public event EventHandler? SelectionContextRequested;
    public EditorSession? Session
    {
        get=>_session;
        set{if(_session is not null)_session.Changed-=OnChanged;_session=value;if(_session is not null)_session.Changed+=OnChanged;Refresh();}
    }
    public SlideViewport()
    {
        IsTabStop=true;MinHeight=100;var root=new Grid{Background=Ribbon.Uno.OfficePalette.Canvas};root.Children.Add(_surface);root.Children.Add(_overlay);Content=root;
        AutomationProperties.SetName(this,"Slide editing canvas");AutomationProperties.SetAutomationId(this,"slide-canvas");
        _surface.PaintSurface+=Paint;_surface.PointerPressed+=Pressed;_surface.PointerMoved+=Moved;_surface.PointerReleased+=Released;_surface.PointerCanceled+=Canceled;_surface.PointerCaptureLost+=CaptureLost;_surface.DoubleTapped+=DoubleTapped;_surface.PointerWheelChanged+=Wheel;
        _surface.RightTapped+=(_,e)=>{SelectionContextRequested?.Invoke(this,EventArgs.Empty);e.Handled=true;};KeyDown+=OnKeyDown;SizeChanged+=(_,_)=>Refresh();
        Unloaded+=(_,_)=>{_renderer.Dispose();};
    }
    public void Refresh()=>_surface.Invalidate();
    private void OnChanged(object? sender,EditorChangedEventArgs e)=>Refresh();
    public void SetZoom(float zoom){CommitText();Zoom=Math.Clamp(zoom,.1f,4);FitToWindow=false;Refresh();ZoomChanged?.Invoke(this,EventArgs.Empty);}
    public void Fit(){CommitText();FitToWindow=true;Refresh();ZoomChanged?.Invoke(this,EventArgs.Empty);}
    private void Paint(object? sender,SKPaintSurfaceEventArgs e)
    {
        var c=e.Surface.Canvas;c.Clear(SlideRenderer.Color("#E9E9E9"));if(Session is not {} session)return;
        var d=session.Document;float dpi=(float)(e.Info.Width/Math.Max(1,ActualWidth)),availableW=Math.Max(1,(float)ActualWidth-88),availableH=Math.Max(1,(float)ActualHeight-66);
        _scale=FitToWindow?Math.Min(availableW/d.Width,availableH/d.Height):Zoom;_scale=Math.Max(.02f,_scale);Zoom=_scale;
        _ox=((float)ActualWidth-d.Width*_scale)/2;_oy=((float)ActualHeight-d.Height*_scale)/2;
        c.Scale(dpi);using(var shadow=new SKPaint{Color=SlideRenderer.Color("#25000000"),IsAntialias=true})c.DrawRect(_ox+3,_oy+4,d.Width*_scale,d.Height*_scale,shadow);
        c.Translate(_ox,_oy);c.Scale(_scale);_renderer.Render(c,d,session.CurrentSlide);
        if(ShowGrid)
        {
            using var grid=new SKPaint{Color=SlideRenderer.Color("#30939AA5"),StrokeWidth=1/_scale};float step=Math.Max(8,session.GridSize);
            for(float x=0;x<=d.Width;x+=step)for(float y=0;y<=d.Height;y+=step)c.DrawPoint(x,y,grid);
        }
        if(ShowGuides)
        {
            using var guide=new SKPaint{Color=SlideRenderer.Color("#D35230"),StrokeWidth=1/_scale,PathEffect=SKPathEffect.CreateDash([6/_scale,5/_scale],0)};
            c.DrawLine(d.Width/2,0,d.Width/2,d.Height,guide);c.DrawLine(0,d.Height/2,d.Width,d.Height/2,guide);
        }
        _renderer.DrawSelection(c,session.SelectedShapes,_scale);
        if(_marquee is {} m){using var fill=new SKPaint{Color=SlideRenderer.Color("#20D35230")};using var border=new SKPaint{Color=SlideRenderer.Color("#D35230"),StrokeWidth=1/_scale,Style=SKPaintStyle.Stroke};c.DrawRect(m.X,m.Y,m.Width,m.Height,fill);c.DrawRect(m.X,m.Y,m.Width,m.Height,border);}
    }
    private CPoint Position(PointerRoutedEventArgs e){var p=e.GetCurrentPoint(_surface).Position;return new(((float)p.X-_ox)/_scale,((float)p.Y-_oy)/_scale);}
    private static bool Key(VirtualKey key)=> (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key)&CoreVirtualKeyStates.Down)!=0;
    private void Pressed(object sender,PointerRoutedEventArgs e)
    {
        if(Session is not {} s || !e.GetCurrentPoint(_surface).Properties.IsLeftButtonPressed)return;CommitText();Focus(FocusState.Pointer);_down=Position(e);_handle=-1;_additive=Key(VirtualKey.Shift)||Key(VirtualKey.Control);
        if(s.Selection.Count==1 && s.PrimaryShape is {} primary)
        {
            var local=Geometry.Rotate(_down,primary.Bounds.Center,-primary.Rotation);var handles=Geometry.Handles(primary.Bounds);
            for(int i=0;i<handles.Length;i++)if(Math.Abs(local.X-handles[i].X)<8/_scale&&Math.Abs(local.Y-handles[i].Y)<8/_scale){_handle=i;break;}
            if(Math.Abs(local.X-primary.Bounds.Center.X)<10/_scale&&Math.Abs(local.Y-(primary.Bounds.Y-31/_scale))<10/_scale)_handle=8;
        }
        if(_handle<0)
        {
            var hit=s.CurrentSlide.Shapes.Reverse().FirstOrDefault(x=>Geometry.HitTest(x,_down,5/_scale));
            if(hit is null){if(!_additive)s.Select(null);_marquee=new(_down.X,_down.Y,0,0);}
            else if(!s.Selection.Contains(hit.Id)||_additive)s.Select(hit.Id,_additive);
        }
        _dragging=true;s.BeginGesture();_surface.CapturePointer(e.Pointer);e.Handled=true;
    }
    private void Moved(object sender,PointerRoutedEventArgs e)
    {
        if(!_dragging||Session is not {} s)return;var p=Position(e);var delta=new CPoint(p.X-_down.X,p.Y-_down.Y);
        if(_marquee is not null){_marquee=RectF.Between(_down,p);Refresh();return;}
        if(Math.Abs(delta.X)+Math.Abs(delta.Y)<.4f)return;
        bool shift=Key(VirtualKey.Shift),snap=s.SnapToGrid&&!Key(VirtualKey.Menu);
        if(_handle==8)
        {
            var b=s.PrimaryShape!.Bounds;float angle=MathF.Atan2(p.Y-b.Center.Y,p.X-b.Center.X)*180/MathF.PI+90;if(shift)angle=Geometry.Snap(angle,15);s.PreviewShapes(x=>x with{Rotation=angle});
        }
        else if(_handle>=0)s.PreviewShapes(x=>{var local0=Geometry.Rotate(_down,x.Bounds.Center,-x.Rotation);var local1=Geometry.Rotate(p,x.Bounds.Center,-x.Rotation);return x with{Bounds=Geometry.Resize(x.Bounds,_handle,new(local1.X-local0.X,local1.Y-local0.Y),shift)};});
        else
        {
            if(shift){if(Math.Abs(delta.X)>Math.Abs(delta.Y))delta=delta with{Y=0};else delta=delta with{X=0};}
            if(snap)delta=new(Geometry.Snap(delta.X,s.GridSize),Geometry.Snap(delta.Y,s.GridSize));
            s.PreviewShapes(x=>x with{Bounds=x.Bounds with{X=x.Bounds.X+delta.X,Y=x.Bounds.Y+delta.Y}});
        }
        e.Handled=true;
    }
    private void Released(object sender,PointerRoutedEventArgs e)
    {
        if(!_dragging)return;_dragging=false;if(_marquee is {} m){Session?.CancelGesture();Session?.SelectRect(m,_additive);_marquee=null;}else Session?.CommitGesture();_surface.ReleasePointerCapture(e.Pointer);Refresh();e.Handled=true;
    }
    private void Canceled(object sender,PointerRoutedEventArgs e){_dragging=false;_marquee=null;Session?.CancelGesture();Refresh();}
    private void CaptureLost(object sender,PointerRoutedEventArgs e){if(_dragging)Canceled(sender,e);}
    private void DoubleTapped(object sender,DoubleTappedRoutedEventArgs e)
    {
        if(Session is not {} s)return;var p=e.GetPosition(_surface);var hit=s.CurrentSlide.Shapes.Reverse().FirstOrDefault(x=>Geometry.HitTest(x,new(((float)p.X-_ox)/_scale,((float)p.Y-_oy)/_scale),5/_scale));
        if(hit is not null && hit.Kind is not (ShapeKind.Image or ShapeKind.Chart or ShapeKind.Table)){s.Select(hit.Id);EditText();}e.Handled=true;
    }
    public void EditText()
    {
        if(Session?.PrimaryShape is not {} shape)return;CommitText();_editingId=shape.Id;
        var b=shape.Bounds;_editor=new TextBox{Text=shape.Text,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,FontSize=Math.Max(8,shape.TextStyle.FontSize*_scale),Width=Math.Max(50,b.Width*_scale),Height=Math.Max(40,b.Height*_scale),Padding=new(3),BorderThickness=new(1),BorderBrush=Ribbon.Uno.OfficePalette.Accent,Background=Ribbon.Uno.OfficePalette.Brush("F7FFFFFF"),Foreground=Ribbon.Uno.OfficePalette.Brush(shape.TextStyle.Color),FontWeight=shape.TextStyle.Bold?Microsoft.UI.Text.FontWeights.Bold:Microsoft.UI.Text.FontWeights.Normal};
        Canvas.SetLeft(_editor,_ox+b.X*_scale);Canvas.SetTop(_editor,_oy+b.Y*_scale);_overlay.Children.Add(_editor);_editor.KeyDown+=(_,e)=>{if(e.Key==VirtualKey.Escape){CommitText();e.Handled=true;}};_editor.LostFocus+=(_,_)=>CommitText();_editor.Focus(FocusState.Programmatic);_editor.SelectAll();
    }
    public void CommitText()
    {
        if(_editor is not {} editor||Session is not {} s)return;var id=_editingId;string text=editor.Text;_editor=null;_editingId=null;_overlay.Children.Remove(editor);
        var shape=s.CurrentSlide.Shapes.FirstOrDefault(x=>x.Id==id);if(shape is not null && shape.Text!=text)s.EditSlide("Edit text",slide=>slide with{Shapes=slide.Shapes.SetItem(slide.Shapes.IndexOf(shape),shape with{Text=text})});
    }
    private void Wheel(object sender,PointerRoutedEventArgs e){if(!Key(VirtualKey.Control))return;SetZoom(Zoom*(e.GetCurrentPoint(_surface).Properties.MouseWheelDelta>0?1.1f:1/1.1f));e.Handled=true;}
    private void OnKeyDown(object sender,KeyRoutedEventArgs e)
    {
        if(_editor is not null||Session is not {} s)return;bool ctrl=Key(VirtualKey.Control),shift=Key(VirtualKey.Shift);float step=shift?10:1;
        if(ctrl)switch(e.Key){case VirtualKey.A:s.SelectAll();break;case VirtualKey.C:s.Copy();break;case VirtualKey.X:s.Cut();break;case VirtualKey.V:s.Paste();break;case VirtualKey.D:s.DuplicateSelection();break;case VirtualKey.Z:if(shift)s.Redo();else s.Undo();break;case VirtualKey.Y:s.Redo();break;case VirtualKey.G:if(shift)s.Ungroup();else s.Group();break;default:return;}
        else switch(e.Key){case VirtualKey.Delete:case VirtualKey.Back:s.DeleteSelection();break;case VirtualKey.Escape:s.CancelGesture();s.Select(null);break;case VirtualKey.Left:s.Nudge(-step,0);break;case VirtualKey.Right:s.Nudge(step,0);break;case VirtualKey.Up:s.Nudge(0,-step);break;case VirtualKey.Down:s.Nudge(0,step);break;case VirtualKey.F2:case VirtualKey.Enter:EditText();break;default:return;}e.Handled=true;
    }
}
