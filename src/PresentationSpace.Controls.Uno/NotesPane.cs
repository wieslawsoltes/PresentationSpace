using System.Collections.Immutable;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using PresentationSpace.Core;
using PresentationSpace.Ribbon.Uno;
namespace PresentationSpace.Controls.Uno;

public sealed class NotesPane : SessionControl
{
    private readonly TextBox _text=new(){AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,PlaceholderText="Click to add notes",FontSize=12,BorderThickness=new(0),Background=OfficePalette.White,Padding=new(15,10,15,10),HorizontalAlignment=HorizontalAlignment.Stretch,VerticalAlignment=Microsoft.UI.Xaml.VerticalAlignment.Stretch};
    private Guid? _slideId;private bool _updating,_dirty;private readonly DispatcherTimer _timer=new(){Interval=TimeSpan.FromMilliseconds(700)};
    public NotesPane(){Content=new Border{Child=_text,BorderBrush=OfficePalette.Line,BorderThickness=new(0,1,0,0)};AutomationProperties.SetName(_text,"Speaker notes");AutomationProperties.SetAutomationId(_text,"speaker-notes");_text.TextChanged+=(_,_)=>{if(!_updating){_dirty=true;_timer.Stop();_timer.Start();}};_text.LostFocus+=(_,_)=>Commit();_timer.Tick+=(_,_)=>Commit();Unloaded+=(_,_)=>_timer.Stop();}
    public void Commit()
    {
        _timer.Stop();if(!_dirty||Session is not {} s||_slideId is not {} id)return;_dirty=false;string text=_text.Text;int index=s.Document.Slides.FindIndex(x=>x.Id==id);if(index<0)return;
        if(s.Document.Slides[index].Notes!=text)s.EditDocument("Edit speaker notes",d=>d with{Slides=d.Slides.SetItem(index,d.Slides[index] with{Notes=text})});
    }
    protected override void OnSessionChanged(bool preview)
    {
        if(Session is not {} s||preview)return;
        if(_slideId!=s.CurrentSlide.Id)Commit();if(_dirty)return;
        _updating=true;_slideId=s.CurrentSlide.Id;_text.Text=s.CurrentSlide.Notes;_updating=false;
    }
}
