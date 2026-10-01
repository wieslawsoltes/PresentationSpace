using System.Collections.Immutable;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PresentationSpace.Core;
using PresentationSpace.Ribbon.Uno;
using VAlign=Microsoft.UI.Xaml.VerticalAlignment;
namespace PresentationSpace.Controls.Uno;

public enum InspectorMode { Format, Selection, Comments }
public sealed class FormatPane : SessionControl
{
    private readonly StackPanel _body=new(){Spacing=13,Margin=new(16,14,16,24)};
    private readonly TextBlock _title=OfficePalette.Text("Format Shape",16,true);
    private PictureLayoutEditor? _pictureEditor;
    private bool _committingPicture;
    public void FocusPictureCrop() => _pictureEditor?.FocusCrop();
    private TextLayoutEditor? _textLayoutEditor;
    public void FocusTextLayout() => _textLayoutEditor?.FocusFirstField();
    public void FocusTabStops() => _textLayoutEditor?.FocusTabStops();
    private TableDataEditor? _tableEditor;
    private bool _committingTable;
    public TableDataEditor? TableEditor => _tableEditor;
    public void FocusTableText() => _tableEditor?.FocusText();
    private ChartDataEditor? _chartEditor;
    public void FocusChartData() => _chartEditor?.FocusData();
    private bool _building;private Guid? _lastSelection;private SlideShape? _lastShape;private InspectorMode _mode;
    public InspectorMode Mode{get=>_mode;set{_mode=value;Rebuild();}}
    public event EventHandler? CloseRequested;
    public FormatPane()
    {
        var root=new Grid{Background=OfficePalette.White,RowDefinitions={new(){Height=new GridLength(46)},new(){Height=new GridLength(1,GridUnitType.Star)}}};
        var heading=new Grid{Margin=new(16,0,8,0),ColumnDefinitions={new(){Width=new GridLength(1,GridUnitType.Star)},new(){Width=GridLength.Auto}}};heading.Children.Add(_title);
        var close=new Button{Content="×",FontSize=21,Background=OfficePalette.White,BorderThickness=new(0),Padding=new(8,0,8,0)};Grid.SetColumn(close,1);close.Click+=(_,_)=>CloseRequested?.Invoke(this,EventArgs.Empty);heading.Children.Add(close);root.Children.Add(heading);
        var scroll=new ScrollViewer{Content=_body,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};Grid.SetRow(scroll,1);root.Children.Add(scroll);
        Content=new Border{Child=root,BorderBrush=OfficePalette.Line,BorderThickness=new(1,0,0,0)};
    }
    protected override void OnSessionChanged(bool preview)
    {
        if(_building||_committingTable||_committingPicture||Session is not {} s)return;
        if(preview&&_lastSelection==s.PrimaryShape?.Id)return;
        if(_mode==InspectorMode.Format&&ReferenceEquals(_lastShape,s.PrimaryShape)&&_lastSelection==s.PrimaryShape?.Id&&_body.Children.Count>0)return;
        if(_mode==InspectorMode.Format && _tableEditor is not null && s.PrimaryShape is {Kind:ShapeKind.Table} table && _lastSelection==table.Id && _lastShape?.Bounds==table.Bounds && _lastShape.Rotation==table.Rotation && _lastShape.Opacity==table.Opacity && _lastShape.AlternativeText==table.AlternativeText)
        { _lastShape=table; _tableEditor.IsEnabled=!table.Locked; _tableEditor.SetValue(TableModel.Get(table)); return; }
        Rebuild();
    }
    public void Rebuild()
    {
        if(_building||Session is not {} s)return;_building=true;
        try
        {
            _chartEditor=null;_tableEditor=null;_textLayoutEditor=null;_pictureEditor=null;_body.Children.Clear();_lastSelection=s.PrimaryShape?.Id;_lastShape=s.PrimaryShape;_title.Text=_mode switch{InspectorMode.Selection=>"Selection",InspectorMode.Comments=>"Comments",_=>s.PrimaryShape is null?"Format Background":"Format Shape"};
            if(_mode==InspectorMode.Selection){BuildSelection(s);return;}if(_mode==InspectorMode.Comments){BuildComments(s);return;}
            var shape=s.PrimaryShape;
            if(shape is null){Section("Slide background");Palette(color=>s.EditSlide("Slide background",x=>x with{Background=color}));Hint("Select an object to edit its size, position, text and appearance.");return;}
            Hint(shape.Name+(s.Selection.Count>1?$" · {s.Selection.Count} objects selected":""));
            Section("Accessibility");var alternative=new TextBox{Header="Alternative text",AcceptsReturn=true,Text=shape.AlternativeText,TextWrapping=TextWrapping.Wrap,FontSize=12};
            alternative.IsEnabled=!shape.Locked;
            var alternativeTarget=SelectionEditSnapshot.Capture(s);
            alternative.LostFocus+=(_,_)=>{if(!_building&&alternative.Text!=shape.AlternativeText)alternativeTarget.TryApply(s,"Alternative text",x=>x with{AlternativeText=alternative.Text});};_body.Children.Add(alternative);
            if(shape.Kind==ShapeKind.Image)
            {
                Section("Picture crop & layout");
                if(s.Selection.Count==1)
                {
                    var editor=new PictureLayoutEditor{IsEnabled=!shape.Locked}; _pictureEditor=editor; editor.SetValue(PictureModel.Resolve(shape));
                    var target=SelectionEditSnapshot.Capture(s);
                    editor.ValueChanged+=(_,picture)=>
                    {
                        if(!ReferenceEquals(_pictureEditor,editor)||!target.IsCurrent(s))
                            throw new InvalidOperationException("The picture changed. Reopen its layout before applying this draft.");
                        _committingPicture=true;
                        try { target.TryApply(s,"Picture layout",x=>PictureModel.Apply(x,picture)); _lastShape=s.PrimaryShape; target=SelectionEditSnapshot.Capture(s); }
                        finally { _committingPicture=false; }
                    };
                    _body.Children.Add(editor);
                }
                else Hint("Select one picture to enter crop percentages. Ribbon commands apply to all selected pictures.");
            }
            if(shape.Kind==ShapeKind.Table)
            {
                Section("Table design & layout");
                if(s.Selection.Count==1)
                {
                    _tableEditor=new TableDataEditor{IsEnabled=!shape.Locked};_tableEditor.SetValue(TableModel.Get(shape));
                    var id=shape.Id;
                    _tableEditor.ValueChanged+=(_,table)=>
                    {
                        _committingTable=true;
                        try { s.EditSlide("Edit table",slide=>slide with{Shapes=slide.Shapes.Select(x=>x.Id==id&&!x.Locked?TableModel.Apply(x,table):x).ToImmutableArray()}); _lastShape=s.PrimaryShape; }
                        finally { _committingTable=false; }
                    };
                    _body.Children.Add(_tableEditor);
                }
                else Hint("Select one table to edit its cells.");
            }
            if(shape.Kind==ShapeKind.Chart)
            {
                Section("Chart design");
                if(s.Selection.Count==1)
                {
                    _chartEditor=new ChartDataEditor{IsEnabled=!shape.Locked};_chartEditor.SetValue(ChartModel.Get(shape));
                    var id=shape.Id;
                    _chartEditor.ValueChanged+=(_,chart)=>s.EditSlide("Edit chart",slide=>slide with{Shapes=slide.Shapes.Select(x=>x.Id==id&&!x.Locked?ChartModel.Apply(x,chart):x).ToImmutableArray()});
                    _body.Children.Add(_chartEditor);
                }
                else Hint("Select one chart to edit its data and design.");
            }
            else if(shape.Kind!=ShapeKind.Table) {Section("Fill");Palette(color=>s.Apply("Shape fill",x=>x with{Fill=color}));}
            Section("Size & position");
            NumericPair("X",shape.Bounds.X,v=>s.Apply("Position X",x=>x with{Bounds=x.Bounds with{X=v}}),"Y",shape.Bounds.Y,v=>s.Apply("Position Y",x=>x with{Bounds=x.Bounds with{Y=v}}));
            NumericPair("Width",shape.Bounds.Width,v=>s.Apply("Width",x=>x with{Bounds=x.Bounds with{Width=Math.Clamp(v,8,16384)}}),"Height",shape.Bounds.Height,v=>s.Apply("Height",x=>x with{Bounds=x.Bounds with{Height=Math.Clamp(v,8,16384)}}));
            NumericPair("Rotation",shape.Rotation,v=>s.Apply("Rotation",x=>x with{Rotation=v%360}),"Opacity %",shape.Opacity*100,v=>s.Apply("Opacity",x=>x with{Opacity=Math.Clamp(v/100,0,1)}));
            if(shape.Kind!=ShapeKind.Table){Section("Line");Palette(color=>s.Apply("Outline color",x=>x with{Stroke=color}));Number("Line width",shape.StrokeWidth,v=>s.Apply("Outline width",x=>x with{StrokeWidth=Math.Clamp(v,0,100)}));}
            if(shape.Kind is not (ShapeKind.Image or ShapeKind.Chart or ShapeKind.Table))
            {
                Section("Text box & paragraph layout");
                if(s.Selection.Count==1)
                {
                    var source=shape;
                    _textLayoutEditor=new TextLayoutEditor{IsEnabled=!shape.Locked};_textLayoutEditor.SetValue(shape);
                    _textLayoutEditor.ValueChanged+=(_,next)=>
                    {
                        if(s.PrimaryShape is not {} current || current.Id!=source.Id || current.Locked || !ReferenceEquals(current,source))
                            throw new InvalidOperationException("The object changed. Reopen its text layout before applying this draft.");
                        s.Apply("Text box and paragraph layout",x=>x.Id==source.Id?next:x);
                        source=s.PrimaryShape!;
                    };
                    _body.Children.Add(_textLayoutEditor);
                }
                Section("Text");var text=new TextBox{AcceptsReturn=true,Text=shape.Text,TextWrapping=TextWrapping.Wrap,MinHeight=78,FontSize=12};text.IsEnabled=!shape.Locked;var textTarget=SelectionEditSnapshot.Capture(s);text.LostFocus+=(_,_)=>{if(!_building&&text.Text!=shape.Text)textTarget.TryApply(s,"Edit text",x=>x with{Text=text.Text});};_body.Children.Add(text);
                Number("Font size",shape.TextStyle.FontSize,v=>s.Apply("Font size",x=>x with{TextStyle=x.TextStyle with{FontSize=Math.Clamp(v,1,512)}}));Palette(color=>s.Apply("Text color",x=>x with{TextStyle=x.TextStyle with{Color=color}}));
                Choice("Vertical alignment",Enum.GetNames<Core.VerticalAlignment>(),shape.TextStyle.VerticalAlignment.ToString(),value=>s.Apply("Text vertical alignment",x=>x with{TextStyle=x.TextStyle with{VerticalAlignment=Enum.Parse<Core.VerticalAlignment>(value)}}));
            }
            Section("Arrange");var arrange=new StackPanel{Spacing=6};arrange.Children.Add(Button("Bring to front",s.BringToFront));arrange.Children.Add(Button("Send to back",s.SendToBack));_body.Children.Add(arrange);
        }
        finally{_building=false;}
    }
    private void BuildSelection(EditorSession s)
    {
        Hint("Select an object. Use the controls to hide or lock it. Object order is front to back.");
        foreach(var shape in s.CurrentSlide.Shapes.Reverse())
        {
            var row=new Grid{ColumnDefinitions={new(){Width=new GridLength(1,GridUnitType.Star)},new(){Width=GridLength.Auto},new(){Width=GridLength.Auto}}};
            var select=Button(shape.Name,()=>s.Select(shape.Id));select.HorizontalAlignment=HorizontalAlignment.Stretch;select.Background=s.Selection.Contains(shape.Id)?OfficePalette.Brush("FBECE6"):OfficePalette.White;row.Children.Add(select);
            var visible=Button(shape.Hidden?"Show":"Hide",()=>s.EditSlide("Object visibility",slide=>slide with{Shapes=slide.Shapes.Select(x=>x.Id==shape.Id?x with{Hidden=!x.Hidden}:x).ToImmutableArray()}));Grid.SetColumn(visible,1);row.Children.Add(visible);
            var locked=Button(shape.Locked?"Unlock":"Lock",()=>s.EditSlide("Object locking",slide=>slide with{Shapes=slide.Shapes.Select(x=>x.Id==shape.Id?x with{Locked=!x.Locked}:x).ToImmutableArray()}));Grid.SetColumn(locked,2);row.Children.Add(locked);_body.Children.Add(row);
        }
        ActionButton("Select all",s.SelectAll);
    }
    private void BuildComments(EditorSession s)
    {
        Hint("Comments are stored in this presentation. They are local—not a cloud collaboration session.");
        var text=new TextBox{PlaceholderText="Add a comment",AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=82,FontSize=12};_body.Children.Add(text);
        ActionButton("Post comment",()=>{if(!string.IsNullOrWhiteSpace(text.Text))s.EditSlide("Add comment",slide=>slide with{Comments=slide.Comments.Add(new(Guid.NewGuid(),"Author",text.Text.Trim(),DateTimeOffset.UtcNow))});});
        foreach(var comment in s.CurrentSlide.Comments)
        {
            var p=new StackPanel{Spacing=7};p.Children.Add(OfficePalette.Text(comment.Author+" · "+comment.Created.ToLocalTime().ToString("g"),10,true));p.Children.Add(new TextBlock{Text=comment.Text,FontSize=12,TextWrapping=TextWrapping.Wrap,Foreground=OfficePalette.Ink});
            p.Children.Add(Button(comment.Resolved?"Reopen":"Resolve",()=>s.EditSlide("Resolve comment",slide=>slide with{Comments=slide.Comments.Select(c=>c.Id==comment.Id?c with{Resolved=!c.Resolved}:c).ToImmutableArray()})));
            _body.Children.Add(new Border{Child=p,Padding=new(12),CornerRadius=new(5),Background=OfficePalette.Brush(comment.Resolved?"F6F6F6":"FFF2EB"),Opacity=comment.Resolved?.65:1});
        }
    }
    private void Section(string title)=>_body.Children.Add(OfficePalette.Text(title,12,true));
    private void Hint(string text)=>_body.Children.Add(new TextBlock{Text=text,FontSize=11,TextWrapping=TextWrapping.Wrap,Foreground=OfficePalette.Muted});
    private void Palette(Action<string> selected){var p=new ColorPalette();p.ColorSelected+=(_,color)=>selected(color);_body.Children.Add(p);}
    private static Button Button(string label,Action action){var b=new Button{Content=new TextBlock{Text=label,TextTrimming=TextTrimming.CharacterEllipsis},MinWidth=0,MinHeight=0,FontSize=11,Padding=new(7,5,7,5),HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Left};Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(b,label);b.Click+=(_,_)=>action();return b;}
    private void ActionButton(string label,Action action)=>_body.Children.Add(Button(label,action));
    private StackPanel Numeric(string name,float value,Action<float> changed)
    {
        var p=new StackPanel{Spacing=3};p.Children.Add(OfficePalette.Text(name,10));var box=new TextBox{Text=value.ToString("0.##",CultureInfo.InvariantCulture),FontSize=12,MinWidth=90,Padding=new(7,4,7,4)};
        var target=Session is {} session?SelectionEditSnapshot.Capture(session):null;
        box.IsEnabled=Session?.PrimaryShape?.Locked!=true;
        bool submitted=false;
        box.LostFocus+=(_,_)=>{if(!_building&&!submitted&&Session is {} current&&target?.IsCurrent(current)==true&&float.TryParse(box.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var f)&&float.IsFinite(f)&&Math.Abs(f-value)>.001f){submitted=true;changed(Math.Clamp(f,-100000,100000));}};p.Children.Add(box);return p;
    }
    private void Number(string label,float value,Action<float> changed)=>_body.Children.Add(Numeric(label,value,changed));
    private void NumericPair(string label,float value,Action<float> changed,string label2,float value2,Action<float> changed2){var p=new Grid{ColumnSpacing=9,ColumnDefinitions={new(){Width=new GridLength(1,GridUnitType.Star)},new(){Width=new GridLength(1,GridUnitType.Star)}}};p.Children.Add(Numeric(label,value,changed));var second=Numeric(label2,value2,changed2);Grid.SetColumn(second,1);p.Children.Add(second);_body.Children.Add(p);}
    private void Choice(string label,IEnumerable<string> values,string selected,Action<string> changed){var c=new ComboBox{Header=label,ItemsSource=values.ToArray(),SelectedItem=selected,HorizontalAlignment=HorizontalAlignment.Stretch,FontSize=12};c.SelectionChanged+=(_,_)=>{if(!_building&&c.SelectedItem is string text&&text!=selected)changed(text);};_body.Children.Add(c);}
}
