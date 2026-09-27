using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PresentationSpace.Core;
using PresentationSpace.Ribbon.Uno;
namespace PresentationSpace.Controls.Uno;

public sealed class PresentationStatusBar : SessionControl
{
    private readonly TextBlock _position=OfficePalette.Text("",10),_message=OfficePalette.Text("Ready",10),_zoomText=OfficePalette.Text("100%",10);
    private readonly Slider _zoom=new(){Minimum=10,Maximum=400,Value=100,Width=108,Height=22,MinHeight=0,StepFrequency=5,VerticalAlignment=Microsoft.UI.Xaml.VerticalAlignment.Center};private bool _updating;
    public event EventHandler<float>? ZoomRequested;public event EventHandler? FitRequested,NotesRequested,NormalRequested,SorterRequested,ShowRequested;
    public string Message{get=>_message.Text;set=>_message.Text=value;}
    public PresentationStatusBar()
    {
        var root=new Grid{Height=29,Background=OfficePalette.Brush("FAFAFA"),ColumnDefinitions={new(){Width=GridLength.Auto},new(){Width=new GridLength(1,GridUnitType.Star)},new(){Width=GridLength.Auto}}};_position.Margin=new(12,0,15,0);root.Children.Add(_position);_message.Margin=new(5,0,5,0);_message.Foreground=OfficePalette.Muted;Grid.SetColumn(_message,1);root.Children.Add(_message);
        var tools=new StackPanel{Orientation=Orientation.Horizontal,Spacing=6,Margin=new(0,0,10,0)};
        void Add(string text,string tooltip,Action action){var b=new Button{Content=text,FontSize=11,Padding=new(7,2,7,2),MinHeight=0,Height=26,Background=OfficePalette.Brush("00FFFFFF"),BorderThickness=new(0)};ToolTipService.SetToolTip(b,tooltip);b.Click+=(_,_)=>action();tools.Children.Add(b);}
        Add("Notes","Show or hide speaker notes",()=>NotesRequested?.Invoke(this,EventArgs.Empty));Add("▣","Normal view",()=>NormalRequested?.Invoke(this,EventArgs.Empty));Add("▦","Slide sorter",()=>SorterRequested?.Invoke(this,EventArgs.Empty));Add("▷","Start slide show",()=>ShowRequested?.Invoke(this,EventArgs.Empty));Add("−","Zoom out",()=>ZoomRequested?.Invoke(this,(float)Math.Max(.1,_zoom.Value/100-.1)));tools.Children.Add(_zoom);Add("+","Zoom in",()=>ZoomRequested?.Invoke(this,(float)Math.Min(4,_zoom.Value/100+.1)));_zoomText.Width=37;tools.Children.Add(_zoomText);Add("⊡","Fit slide to window",()=>FitRequested?.Invoke(this,EventArgs.Empty));Grid.SetColumn(tools,2);root.Children.Add(tools);Content=new Border{Child=root,BorderBrush=OfficePalette.Line,BorderThickness=new(0,1,0,0)};
        _zoom.ValueChanged+=(_,e)=>{if(!_updating)ZoomRequested?.Invoke(this,(float)e.NewValue/100);};
    }
    public void SetZoom(float zoom){_updating=true;_zoom.Value=zoom*100;_zoomText.Text=$"{zoom*100:0}%";_updating=false;}
    protected override void OnSessionChanged(bool preview){if(Session is {} s)_position.Text=$"Slide {s.SlideIndex+1} of {s.Document.Slides.Length}"+(s.Selection.Count>0?$"   ·   {s.Selection.Count} selected":"");}
}
