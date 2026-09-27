using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PresentationSpace.Core;
using PresentationSpace.Ribbon.Uno;
namespace PresentationSpace.Controls.Uno;

public sealed class SlideSorter : SessionControl
{
    private readonly GridView _grid=new(){SelectionMode=ListViewSelectionMode.Single,IsItemClickEnabled=true,Padding=new(28),Background=OfficePalette.Brush("E9E9E9")};
    private PresentationDocument? _last;
    public event EventHandler? SlideInvoked;
    public SlideSorter(){Content=_grid;_grid.ItemClick+=(_,e)=>{if(e.ClickedItem is GridViewItem item&&item.Tag is int i)Session?.SelectSlide(i);};_grid.DoubleTapped+=(_,_)=>SlideInvoked?.Invoke(this,EventArgs.Empty);}
    protected override void OnSessionChanged(bool preview)
    {
        if(Session is not {} s||preview)return;
        if(ReferenceEquals(_last,s.Document)){_grid.SelectedIndex=s.SlideIndex;return;}_last=s.Document;_grid.Items.Clear();
        for(int i=0;i<s.Document.Slides.Length;i++)
        {
            var slide=s.Document.Slides[i];var previewControl=new SlidePreview{Width=256,Height=256*s.Document.Height/s.Document.Width};previewControl.SetSlide(s.Document,slide);
            var p=new StackPanel{Spacing=8,Margin=new(10)};p.Children.Add(new Border{Child=previewControl,BorderBrush=OfficePalette.Line,BorderThickness=new(1)});p.Children.Add(OfficePalette.Text($"{i+1}    {slide.Name}",11));
            _grid.Items.Add(new GridViewItem{Content=p,Tag=i,Opacity=slide.Hidden?.5:1});
        }
        _grid.SelectedIndex=s.SlideIndex;
    }
}
