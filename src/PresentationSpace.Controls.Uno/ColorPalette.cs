using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using PresentationSpace.Ribbon.Uno;
namespace PresentationSpace.Controls.Uno;

public sealed class ColorPalette : UserControl
{
    public static readonly string[] Colors=["#FFFFFF","#F4F4F4","#D9E2F3","#EBC7B7","#D35230","#C00000","#FFC000","#70AD47","#287D61","#00B0F0","#187EAB","#4472C4","#7654B3","#53657D","#243247","#000000"];
    public event EventHandler<string>? ColorSelected;
    public ColorPalette()
    {
        var panel=new StackPanel{Spacing=7};for(int row=0;row<2;row++){var line=new StackPanel{Orientation=Orientation.Horizontal,Spacing=4};for(int col=0;col<8;col++){string color=Colors[row*8+col];var button=new Button{Width=22,Height=22,MinWidth=0,MinHeight=0,Padding=new(0),Background=OfficePalette.Brush(color),BorderBrush=OfficePalette.Brush("C4C4C4"),BorderThickness=new(1),CornerRadius=new(2)};AutomationProperties.SetName(button,color);ToolTipService.SetToolTip(button,color);button.Click+=(_,_)=>ColorSelected?.Invoke(this,color);line.Children.Add(button);}panel.Children.Add(line);}
        var transparent=new Button{Content="No fill",FontSize=11,Padding=new(6,2,6,2),HorizontalAlignment=HorizontalAlignment.Left};transparent.Click+=(_,_)=>ColorSelected?.Invoke(this,"#00000000");panel.Children.Add(transparent);Content=panel;
    }
}
