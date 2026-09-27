using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using PresentationSpace.Core;
using PresentationSpace.Rendering.Skia;
using PresentationSpace.Ribbon.Uno;
using SkiaSharp;
using SkiaSharp.Views.Windows;
using Windows.System;
namespace PresentationSpace.Controls.Uno;

public sealed class PresentationPlayer : UserControl
{
    private readonly SKXamlCanvas _canvas=new();private readonly SlideRenderer _renderer=new();private readonly DispatcherTimer _timer=new(){Interval=TimeSpan.FromMilliseconds(16)};private readonly Stopwatch _clock=new();
    private PresentationDocument? _document;private int[] _slides=[];private int _position,_previous=-1;private int _blank;
    private readonly TextBlock _counter=OfficePalette.Text("",12);public event EventHandler? Closed;
    public PresentationPlayer()
    {
        IsTabStop=true;var grid=new Grid{Background=OfficePalette.Brush("000000")};grid.Children.Add(_canvas);
        var bar=new StackPanel{Orientation=Orientation.Horizontal,Spacing=7,VerticalAlignment=Microsoft.UI.Xaml.VerticalAlignment.Bottom,HorizontalAlignment=HorizontalAlignment.Left,Margin=new(18),Opacity=.9};
        Button Add(string text,Action action){var b=new Button{Content=text,FontSize=12,Padding=new(10,5,10,5),Background=OfficePalette.Brush("E6FFFFFF")};b.Click+=(_,_)=>action();bar.Children.Add(b);return b;}
        Add("‹",Previous);Add("›",Next);Add("End show",Close);_counter.Foreground=OfficePalette.White;bar.Children.Add(_counter);grid.Children.Add(bar);Content=grid;
        _canvas.PaintSurface+=Paint;_canvas.Tapped+=(_,_)=>Next();_timer.Tick+=(_,_)=>{_canvas.Invalidate();if(_clock.Elapsed.TotalSeconds>MaxAnimationTime())_timer.Stop();};SizeChanged+=(_,_)=>_canvas.Invalidate();
        KeyDown+=(_,e)=>{switch(e.Key){case VirtualKey.Escape:Close();break;case VirtualKey.Left:case VirtualKey.Up:case VirtualKey.PageUp:Previous();break;case VirtualKey.Right:case VirtualKey.Down:case VirtualKey.Space:case VirtualKey.Enter:case VirtualKey.PageDown:Next();break;case VirtualKey.Home:_position=0;Restart();break;case VirtualKey.End:_position=Math.Max(0,_slides.Length-1);Restart();break;case VirtualKey.B:_blank=_blank==1?0:1;_canvas.Invalidate();break;case VirtualKey.W:_blank=_blank==2?0:2;_canvas.Invalidate();break;default:return;}e.Handled=true;};
        Unloaded+=(_,_)=>{_timer.Stop();_renderer.Dispose();};AutomationProperties.SetName(this,"Slide show");AutomationProperties.SetAutomationId(this,"slide-show");
    }
    public void Start(PresentationDocument document,int from=0)
    {
        _document=document;_slides=Enumerable.Range(0,document.Slides.Length).Where(i=>!document.Slides[i].Hidden).ToArray();_position=Array.FindIndex(_slides,i=>i>=from);if(_position<0)_position=0;_previous=-1;_blank=0;Visibility=Visibility.Visible;Restart();Focus(FocusState.Programmatic);
    }
    private float MaxAnimationTime()
    {
        if(_document is null||_slides.Length==0)return 0;var slide=_document.Slides[_slides[_position]];return Math.Max(slide.TransitionDuration,slide.Shapes.Where(s=>s.Animation!=AnimationKind.None).Select(s=>s.AnimationOrder*.35f+s.AnimationDuration).DefaultIfEmpty(0).Max())+.1f;
    }
    private void Restart(){_blank=0;_clock.Restart();_timer.Start();_counter.Text=$"  {_position+1} / {_slides.Length}";_canvas.Invalidate();}
    public void Next(){if(_position+1>=_slides.Length){Close();return;}_previous=_position;_position++;Restart();}
    public void Previous(){if(_position<=0)return;_previous=_position;_position--;Restart();}
    public void Close(){_timer.Stop();Visibility=Visibility.Collapsed;Closed?.Invoke(this,EventArgs.Empty);}
    private void Paint(object? sender,SKPaintSurfaceEventArgs e)
    {
        var c=e.Surface.Canvas;c.ResetMatrix();c.Clear(_blank==2?SKColors.White:SKColors.Black);if(_blank!=0||_document is not {} d||_slides.Length==0)return;
        float scale=Math.Min(e.Info.Width/d.Width,e.Info.Height/d.Height);c.Save();c.Translate((e.Info.Width-d.Width*scale)/2,(e.Info.Height-d.Height*scale)/2);c.Scale(scale);
        var slide=d.Slides[_slides[_position]];float t=(float)_clock.Elapsed.TotalSeconds,progress=Math.Clamp(t/Math.Max(.01f,slide.TransitionDuration),0,1);
        if(_previous>=0&&progress<1&&slide.Transition!=TransitionKind.None)
        {
            _renderer.Render(c,d,d.Slides[_slides[_previous]]);c.Save();
            if(slide.Transition==TransitionKind.Fade){using var opacity=new SKPaint{Color=SKColors.White.WithAlpha((byte)(255*progress))};c.SaveLayer(opacity);_renderer.Render(c,d,slide,t);c.Restore();}
            else if(slide.Transition==TransitionKind.Push){c.ClipRect(new(0,0,d.Width,d.Height));c.Translate(d.Width*(1-progress),0);_renderer.Render(c,d,slide,t);}
            else {c.ClipRect(new(0,0,d.Width*progress,d.Height));_renderer.Render(c,d,slide,t);}
            c.Restore();
        }
        else _renderer.Render(c,d,slide,t);
        c.Restore();
    }
}
