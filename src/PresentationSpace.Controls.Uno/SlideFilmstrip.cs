using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using PresentationSpace.Core;
using PresentationSpace.Ribbon.Uno;
using Windows.ApplicationModel.DataTransfer;
using VAlign=Microsoft.UI.Xaml.VerticalAlignment;
namespace PresentationSpace.Controls.Uno;

public sealed class SlideFilmstrip : SessionControl
{
    private sealed record Item(Guid Id,ListViewItem Container,SlidePreview Preview,TextBlock Number,TextBlock Title,Border Frame);
    private readonly ListView _list=new(){SelectionMode=ListViewSelectionMode.Single,Background=OfficePalette.Brush("F6F6F6"),Padding=new(8,7,8,20),CanDragItems=true,AllowDrop=true};
    private readonly List<Item> _items=[];
    private bool _updating;
    public event EventHandler? SlideInvoked;
    public SlideFilmstrip()
    {
        Content=_list;AutomationProperties.SetName(_list,"Slide thumbnails");AutomationProperties.SetAutomationId(_list,"slide-filmstrip");
        _list.SelectionChanged+=(_,_)=>{if(!_updating&&_list.SelectedIndex>=0){Session?.SelectSlide(_list.SelectedIndex);SlideInvoked?.Invoke(this,EventArgs.Empty);}};
        _list.DragItemsStarting+=(_,e)=>{if(e.Items.FirstOrDefault() is ListViewItem item&&item.Tag is Guid id)e.Data.SetText("presentationspace-slide:"+id);e.Data.RequestedOperation=DataPackageOperation.Move;};
        SizeChanged+=(_,_)=>UpdateSizes();
    }
    protected override void OnSessionChanged(bool preview)
    {
        if(Session is not {} s)return;_updating=true;
        try
        {
            if(!_items.Select(i=>i.Id).SequenceEqual(s.Document.Slides.Select(x=>x.Id)))
            {
                _items.Clear();_list.Items.Clear();
                foreach(var slide in s.Document.Slides)
                {
                    var number=OfficePalette.Text("",11);number.VerticalAlignment=VAlign.Top;number.Margin=new(0,4,5,0);number.Width=18;
                    var thumbnail=new SlidePreview();var frame=new Border{Child=thumbnail,BorderThickness=new(2),BorderBrush=OfficePalette.Line,Background=OfficePalette.White,CornerRadius=new(2)};
                    var title=OfficePalette.Text(slide.Name,10);title.TextTrimming=TextTrimming.CharacterEllipsis;title.Margin=new(2,4,0,0);
                    var column=new StackPanel();column.Children.Add(frame);column.Children.Add(title);
                    var row=new StackPanel{Orientation=Orientation.Horizontal};row.Children.Add(number);row.Children.Add(column);
                    var container=new ListViewItem{Content=row,Tag=slide.Id,Padding=new(0,5,0,5),HorizontalContentAlignment=HorizontalAlignment.Stretch,AllowDrop=true};
                    var capturedId=slide.Id;container.ContextFlyout=BuildMenu(capturedId);
                    container.DragOver+=(_,e)=>{if(e.DataView.Contains(StandardDataFormats.Text))e.AcceptedOperation=DataPackageOperation.Move;};
                    container.Drop+=async(_,e)=>
                    {
                        var deferral=e.GetDeferral();try{string text=await e.DataView.GetTextAsync();if(text.StartsWith("presentationspace-slide:")&&Guid.TryParse(text[24..],out var source)&&Session is {} editor){int from=editor.Document.Slides.FindIndex(x=>x.Id==source),to=editor.Document.Slides.FindIndex(x=>x.Id==capturedId);editor.MoveSlide(from,to);}}finally{deferral.Complete();}
                    };
                    _items.Add(new(slide.Id,container,thumbnail,number,title,frame));_list.Items.Add(container);
                }
            }
            for(int i=0;i<_items.Count;i++)
            {
                var item=_items[i];var slide=s.Document.Slides[i];item.Number.Text=(i+1).ToString();item.Title.Text=slide.Name;item.Preview.SetSlide(s.Document,slide);item.Frame.BorderBrush=i==s.SlideIndex?OfficePalette.Accent:OfficePalette.Line;item.Container.Opacity=slide.Hidden?.45:1;
                AutomationProperties.SetName(item.Container,$"Slide {i+1}: {slide.Name}");
            }
            _list.SelectedIndex=s.SlideIndex;UpdateSizes();
        }
        finally{_updating=false;}
    }
    private void UpdateSizes()
    {
        float ratio=Session is {} s?s.Document.Height/s.Document.Width:.5625f;double width=Math.Max(88,ActualWidth-52);
        foreach(var item in _items){item.Preview.Width=width;item.Preview.Height=width*ratio;item.Title.MaxWidth=width;}
    }
    private MenuFlyout BuildMenu(Guid id)
    {
        var menu=new MenuFlyout();
        void Add(string title,Action<EditorSession> action){var item=new MenuFlyoutItem{Text=title};item.Click+=(_,_)=>{if(Session is {} s){s.SelectSlide(s.Document.Slides.FindIndex(x=>x.Id==id));action(s);}};menu.Items.Add(item);}
        Add("New slide",s=>s.AddSlide());Add("Duplicate slide",s=>s.DuplicateSlide());Add("Delete slide",s=>s.DeleteSlide());menu.Items.Add(new MenuFlyoutSeparator());
        Add("Move up",s=>s.MoveSlide(s.SlideIndex,s.SlideIndex-1));Add("Move down",s=>s.MoveSlide(s.SlideIndex,s.SlideIndex+1));Add("Hide / show slide",s=>s.EditSlide("Toggle hidden slide",x=>x with{Hidden=!x.Hidden}));return menu;
    }
}

internal static class ImmutableArraySearch
{
    public static int FindIndex<T>(this System.Collections.Immutable.ImmutableArray<T> source,Func<T,bool> predicate){for(int i=0;i<source.Length;i++)if(predicate(source[i]))return i;return -1;}
}
