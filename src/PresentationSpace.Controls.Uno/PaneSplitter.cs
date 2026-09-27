using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PresentationSpace.Ribbon.Uno;
namespace PresentationSpace.Controls.Uno;

public sealed class PaneSplitter : Border
{
    private bool _dragging;private double _start,_width;
    public ColumnDefinition? TargetColumn{get;set;}
    public bool Reverse{get;set;}
    public double Minimum{get;set;}=140;
    public double Maximum{get;set;}=480;
    public PaneSplitter()
    {
        Width=5;Background=OfficePalette.Brush("00FFFFFF");
        PointerEntered+=(_,_)=>Background=OfficePalette.Brush("D8D8D8");PointerExited+=(_,_)=>{if(!_dragging)Background=OfficePalette.Brush("00FFFFFF");};
        PointerPressed+=(_,e)=>{if(TargetColumn is null)return;_dragging=true;_start=e.GetCurrentPoint(null).Position.X;_width=TargetColumn.ActualWidth;CapturePointer(e.Pointer);e.Handled=true;};
        PointerMoved+=(_,e)=>{if(!_dragging||TargetColumn is null)return;double dx=e.GetCurrentPoint(null).Position.X-_start;TargetColumn.Width=new GridLength(Math.Clamp(_width+(Reverse?-dx:dx),Minimum,Maximum));e.Handled=true;};
        PointerReleased+=(_,e)=>{_dragging=false;ReleasePointerCapture(e.Pointer);Background=OfficePalette.Brush("00FFFFFF");};PointerCaptureLost+=(_,_)=>_dragging=false;
    }
}
