using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace PresentationSpace.Ribbon.Uno;

public static class OfficePalette
{
    public static SolidColorBrush Brush(string hex)
    {
        hex=hex.TrimStart('#'); if(hex.Length==6)hex="FF"+hex;
        uint value=Convert.ToUInt32(hex,16);return new(Color.FromArgb((byte)(value>>24),(byte)(value>>16),(byte)(value>>8),(byte)value));
    }
    public static readonly SolidColorBrush Accent=Brush("D35230"), Ink=Brush("242424"), Muted=Brush("616161"), Line=Brush("E4E4E4"), White=Brush("FFFFFF"), Canvas=Brush("E9E9E9");
    public static TextBlock Text(string text,double size=12,bool bold=false)=>new(){Text=text,FontSize=size,Foreground=Ink,FontWeight=bold?Microsoft.UI.Text.FontWeights.SemiBold:Microsoft.UI.Text.FontWeights.Normal,VerticalAlignment=VerticalAlignment.Center};
}

public sealed class RibbonCommandButton : Button
{
    public string CommandId{get;}
    public RibbonCommandButton(string id,string label,string glyph,Action action,bool large=true,string? shortcut=null)
    {
        CommandId=id;Background=OfficePalette.Brush("00FFFFFF");BorderThickness=new(0);CornerRadius=new(4);Padding=new(7,4,7,4);MinWidth=large?52:28;Height=large?76:27;Foreground=OfficePalette.Ink;
        var content=new StackPanel{Orientation=large?Orientation.Vertical:Orientation.Horizontal,Spacing=large?5:7,HorizontalAlignment=HorizontalAlignment.Center};
        content.Children.Add(new FontIcon{Glyph=glyph,FontSize=large?25:15,Foreground=OfficePalette.Accent});
        if(!string.IsNullOrEmpty(label))content.Children.Add(new TextBlock{Text=label,FontSize=11,TextAlignment=TextAlignment.Center,Foreground=OfficePalette.Ink,TextWrapping=TextWrapping.Wrap,MaxWidth=large?82:145,VerticalAlignment=VerticalAlignment.Center});
        Content=content;AutomationProperties.SetName(this,label.Replace('\n',' '));AutomationProperties.SetAutomationId(this,id);ToolTipService.SetToolTip(this,label.Replace('\n',' ')+(shortcut is null?"":" ("+shortcut+")"));Click+=(_,_)=>action();
    }
}

public sealed class RibbonGroup : UserControl
{
    public string Title{get;}
    public StackPanel Items{get;}=new(){Orientation=Orientation.Horizontal,Spacing=3,VerticalAlignment=VerticalAlignment.Top};
    public RibbonGroup(string title,params UIElement[] items)
    {
        Title=title;var grid=new Grid{RowDefinitions={new(){Height=new GridLength(1,GridUnitType.Star)},new(){Height=new GridLength(18)}}};
        foreach(var item in items)Items.Children.Add(item);grid.Children.Add(Items);
        var caption=new TextBlock{Text=title,FontSize=10,Foreground=OfficePalette.Muted,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};Grid.SetRow(caption,1);grid.Children.Add(caption);
        Content=new Border{Child=grid,BorderBrush=OfficePalette.Line,BorderThickness=new(0,0,1,0),Padding=new(7,3,8,0),Height=102};
    }
    public static StackPanel Column(params UIElement[] items){var p=new StackPanel{Spacing=1,VerticalAlignment=VerticalAlignment.Top};foreach(var item in items)p.Children.Add(item);return p;}
}

public sealed record RibbonTab(string Title,Func<IEnumerable<RibbonGroup>> Build);

public sealed class RibbonControl : UserControl
{
    private readonly StackPanel _tabs=new(){Orientation=Orientation.Horizontal,Spacing=1};
    private readonly StackPanel _groups=new(){Orientation=Orientation.Horizontal};
    private readonly List<RibbonTab> _definitions=[];
    private readonly List<Button> _buttons=[];
    public string SelectedTab{get;private set;}="";
    public event EventHandler<string>? TabChanged;
    public bool IsCollapsed{get;private set;}
    private readonly ScrollViewer _groupScroll;
    public RibbonControl()
    {
        var root=new Grid{Background=OfficePalette.White,RowDefinitions={new(){Height=GridLength.Auto},new(){Height=GridLength.Auto}}};
        var tabsScroll=new ScrollViewer{Content=_tabs,HorizontalScrollBarVisibility=ScrollBarVisibility.Hidden,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,HorizontalScrollMode=ScrollMode.Enabled};root.Children.Add(tabsScroll);
        _groupScroll=new(){Content=_groups,HorizontalScrollBarVisibility=ScrollBarVisibility.Hidden,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,HorizontalScrollMode=ScrollMode.Enabled};Grid.SetRow(_groupScroll,1);root.Children.Add(_groupScroll);
        Content=new Border{Child=root,BorderBrush=OfficePalette.Line,BorderThickness=new(0,0,0,1)};
    }
    public void SetTabs(IEnumerable<RibbonTab> tabs)
    {
        _definitions.Clear();_definitions.AddRange(tabs);_tabs.Children.Clear();_buttons.Clear();
        foreach(var tab in _definitions)
        {
            var button=new Button{Content=tab.Title,FontSize=12,MinHeight=34,Padding=new(13,4,13,4),Background=OfficePalette.White,BorderThickness=new(0,0,0,2),BorderBrush=OfficePalette.White,CornerRadius=new(0),Foreground=OfficePalette.Ink};
            AutomationProperties.SetName(button,tab.Title+" tab");button.Click+=(_,_)=>SelectTab(tab.Title);button.DoubleTapped+=(_,_)=>ToggleCollapsed();_buttons.Add(button);_tabs.Children.Add(button);
        }
        if(_definitions.Count>0)SelectTab(_definitions[0].Title);
    }
    public void SelectTab(string title)
    {
        var tab=_definitions.FirstOrDefault(t=>t.Title==title);if(tab is null)return;SelectedTab=title;
        for(int i=0;i<_buttons.Count;i++){bool selected=_definitions[i].Title==title;_buttons[i].BorderBrush=selected?OfficePalette.Accent:OfficePalette.White;_buttons[i].Foreground=selected?OfficePalette.Accent:OfficePalette.Ink;}
        _groups.Children.Clear();foreach(var group in tab.Build())_groups.Children.Add(group);TabChanged?.Invoke(this,title);
    }
    public void ToggleCollapsed(){IsCollapsed=!IsCollapsed;_groupScroll.Visibility=IsCollapsed?Visibility.Collapsed:Visibility.Visible;}
}
