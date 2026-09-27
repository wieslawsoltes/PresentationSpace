using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PresentationSpace.Core;
using PresentationSpace.Controls.Uno;
using PresentationSpace.Ribbon.Uno;
using Windows.System;
using Windows.UI.Core;
using VAlign=Microsoft.UI.Xaml.VerticalAlignment;
namespace PresentationSpace.Editor.Uno;

/// <summary>Reusable editor shell. Rendering, commands and storage can be consumed independently.</summary>
public sealed partial class PresentationEditor : UserControl
{
    public EditorSession Session{get;}
    public SlideViewport Viewport{get;}=new();
    public IWorkspaceStorage? Storage{get;set;}
    public RibbonControl Ribbon{get;}=new();
    public bool IsPresenting=>_player.Visibility==Visibility.Visible;
    public event EventHandler? ViewChanged;
    private readonly Grid _root=new(),_workspace=new();
    private readonly SlideFilmstrip _filmstrip=new();private readonly FormatPane _format=new();private readonly NotesPane _notes=new();private readonly SlideSorter _sorter=new();private readonly PresentationPlayer _player=new(){Visibility=Visibility.Collapsed};private readonly PresentationStatusBar _status=new();
    private readonly ColumnDefinition _filmColumn=new(){Width=new GridLength(220)},_formatColumn=new(){Width=new GridLength(0)};
    private readonly RowDefinition _notesRow=new(){Height=new GridLength(86)};
    private readonly InfoBar _notice=new(){IsOpen=false,IsClosable=true};
    private readonly TextBlock _documentName=OfficePalette.Text("Presentation",12,true),_saveState=OfficePalette.Text("Saved",10);
    private readonly AutoSuggestBox _search=new(){PlaceholderText="Search commands (Alt+Q)",Width=256,FontSize=12,MinHeight=29,MaxHeight=32};
    private readonly List<(string Title,Action Execute)> _commands=[];
    private readonly DispatcherTimer _recoveryTimer=new(){Interval=TimeSpan.FromMilliseconds(900)};
    private PresentationDocument? _lastRecovery,_observed;
    private bool _autoSave=true,_busy,_syncing;
    private Grid? _backstage;
    private ComboBox? _fontFamily,_fontSize;
    private RibbonCommandButton? _bold,_italic,_underline;
    public PresentationEditor():this(null,null){}
    public PresentationEditor(EditorSession? session,IWorkspaceStorage? storage)
    {
        Session=session??new EditorSession(SlideFactory.Welcome());Storage=storage;FontFamily=new FontFamily("Segoe UI");Foreground=OfficePalette.Ink;
        // Establish the initial baseline before any child can raise Loaded or selection events.
        // An unchanged welcome document must never overwrite an existing recovery file.
        _observed=Session.Document;
        Viewport.Session=Session;_filmstrip.Session=Session;_format.Session=Session;_notes.Session=Session;_sorter.Session=Session;_status.Session=Session;
        _root.Background=OfficePalette.Brush("F5F5F5");_root.RowDefinitions.Add(new(){Height=new GridLength(44)});_root.RowDefinitions.Add(new(){Height=GridLength.Auto});_root.RowDefinitions.Add(new(){Height=GridLength.Auto});_root.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});_root.RowDefinitions.Add(new(){Height=new GridLength(29)});
        _root.Children.Add(BuildTitleBar());Grid.SetRow(Ribbon,1);_root.Children.Add(Ribbon);Grid.SetRow(_notice,2);_root.Children.Add(_notice);Grid.SetRow(_workspace,3);_root.Children.Add(_workspace);Grid.SetRow(_status,4);_root.Children.Add(_status);BuildWorkspace();BuildRibbon();BuildCommands();
        Grid.SetRowSpan(_player,5);_root.Children.Add(_player);Content=_root;
        _filmstrip.SlideInvoked+=(_,_)=>{ShowNormal();Viewport.Focus(FocusState.Programmatic);};_format.CloseRequested+=(_,_)=>HideInspector();_sorter.SlideInvoked+=(_,_)=>ShowNormal();
        Viewport.SelectionContextRequested+=(_,_)=>ShowInspector(InspectorMode.Format);Viewport.ZoomChanged+=(_,_)=>_status.SetZoom(Viewport.Zoom);
        _status.ZoomRequested+=(_,zoom)=>Viewport.SetZoom(zoom);_status.FitRequested+=(_,_)=>Viewport.Fit();_status.NotesRequested+=(_,_)=>ToggleNotes();_status.NormalRequested+=(_,_)=>ShowNormal();_status.SorterRequested+=(_,_)=>ShowSorter();_status.ShowRequested+=(_,_)=>StartShow(false);
        _player.Closed+=(_,_)=>{Viewport.Focus(FocusState.Programmatic);ViewChanged?.Invoke(this,EventArgs.Empty);};
        Session.Changed+=OnSessionChanged;_recoveryTimer.Tick+=async(_,_)=>{_recoveryTimer.Stop();await SaveRecoveryAsync();};KeyDown+=HandleKey;
        Loaded+=(_,_)=>{OnSessionChanged(this,new());DispatcherQueue.TryEnqueue(()=>Viewport.Focus(FocusState.Programmatic));};Unloaded+=(_,_)=>_recoveryTimer.Stop();
        SizeChanged+=(_,_)=>{if(ActualWidth<820)_filmColumn.Width=new GridLength(150);else if(_filmColumn.Width.Value<170)_filmColumn.Width=new GridLength(220);if(ActualWidth<1050)HideInspector();_search.Visibility=ActualWidth>1050?Visibility.Visible:Visibility.Collapsed;};
    }
    private UIElement BuildTitleBar()
    {
        var bar=new Grid{Background=OfficePalette.Brush("F7EAE5"),Padding=new(12,0,12,0),ColumnSpacing=15,ColumnDefinitions={new(){Width=GridLength.Auto},new(){Width=new GridLength(1,GridUnitType.Star)},new(){Width=GridLength.Auto},new(){Width=GridLength.Auto}}};
        var left=new StackPanel{Orientation=Orientation.Horizontal,Spacing=10,VerticalAlignment=VAlign.Center};
        left.Children.Add(new Border{Background=OfficePalette.Accent,CornerRadius=new(4),Width=27,Height=27,Child=new TextBlock{Text="P",Foreground=OfficePalette.White,FontSize=19,FontWeight=Microsoft.UI.Text.FontWeights.Bold,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VAlign.Center}});
        var auto=new ToggleSwitch{IsOn=true,OffContent="",OnContent="",MinWidth=0,Width=45,MinHeight=0,Height=26};ToolTipService.SetToolTip(auto,"AutoSave a local recovery copy. This is not cloud storage.");auto.Toggled+=(_,_)=>{_autoSave=auto.IsOn;if(_autoSave&&Session.IsDirty)_recoveryTimer.Start();};left.Children.Add(OfficePalette.Text("AutoSave",10));left.Children.Add(auto);
        left.Children.Add(Quick("Save","\uE74E",()=>Run(SaveNativeAsync)));left.Children.Add(Quick("Undo","\uE7A7",()=>Session.Undo()));left.Children.Add(Quick("Redo","\uE7A6",()=>Session.Redo()));bar.Children.Add(left);
        var titlePanel=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};_documentName.MaxWidth=330;_documentName.TextTrimming=TextTrimming.CharacterEllipsis;titlePanel.Children.Add(_documentName);titlePanel.Children.Add(OfficePalette.Text("⌄",11));_saveState.Foreground=OfficePalette.Muted;titlePanel.Children.Add(_saveState);
        var titleButton=new Button{Content=titlePanel,Background=OfficePalette.Brush("00FFFFFF"),BorderThickness=new(0),Padding=new(4),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VAlign.Center};titleButton.Click+=(_,_)=>Run(RenameAsync);Grid.SetColumn(titleButton,1);bar.Children.Add(titleButton);
        Grid.SetColumn(_search,2);bar.Children.Add(_search);
        var right=new StackPanel{Orientation=Orientation.Horizontal,Spacing=7,VerticalAlignment=VAlign.Center};right.Children.Add(SmallButton("Comments",()=>ShowInspector(InspectorMode.Comments)));var present=SmallButton("▷  Present",()=>StartShow(false));present.Background=OfficePalette.White;right.Children.Add(present);
        var share=SmallButton("Share  ⌄",()=>{});share.Foreground=OfficePalette.White;share.Background=OfficePalette.Accent;share.Flyout=MakeMenu([("PowerPoint file (.pptx)",()=>Run(()=>ExportAsync("pptx"))),("PDF handout",()=>Run(()=>ExportAsync("pdf"))),("Native editable file",()=>Run(SaveNativeAsync))]);right.Children.Add(share);Grid.SetColumn(right,3);bar.Children.Add(right);return bar;
    }
    private void BuildWorkspace()
    {
        _workspace.ColumnDefinitions.Add(_filmColumn);_workspace.ColumnDefinitions.Add(new(){Width=new GridLength(5)});_workspace.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});_workspace.ColumnDefinitions.Add(new(){Width=new GridLength(5)});_workspace.ColumnDefinitions.Add(_formatColumn);
        _workspace.Children.Add(_filmstrip);var leftSplitter=new PaneSplitter{TargetColumn=_filmColumn,Minimum=140,Maximum=360};Grid.SetColumn(leftSplitter,1);_workspace.Children.Add(leftSplitter);
        var center=new Grid{RowDefinitions={new(){Height=new GridLength(1,GridUnitType.Star)},_notesRow}};center.Children.Add(Viewport);_sorter.Visibility=Visibility.Collapsed;center.Children.Add(_sorter);Grid.SetRow(_notes,1);center.Children.Add(_notes);Grid.SetColumn(center,2);_workspace.Children.Add(center);
        var rightSplitter=new PaneSplitter{TargetColumn=_formatColumn,Reverse=true,Minimum=260,Maximum=420};Grid.SetColumn(rightSplitter,3);_workspace.Children.Add(rightSplitter);Grid.SetColumn(_format,4);_workspace.Children.Add(_format);
    }
    private void OnSessionChanged(object? sender,EditorChangedEventArgs e)
    {
        _syncing=true;_documentName.Text=Session.Document.Title;_saveState.Text=Session.IsDirty?"• Edited":"✓";
        if(_fontFamily is not null)_fontFamily.SelectedItem=(Viewport.CurrentTextStyle ?? Session.PrimaryShape?.TextStyle)?.FontFamily??"Arial";
        if(_fontSize is not null)_fontSize.SelectedItem=((Viewport.CurrentTextStyle ?? Session.PrimaryShape?.TextStyle)?.FontSize??28).ToString("0.##",System.Globalization.CultureInfo.InvariantCulture);
        void Mark(RibbonCommandButton? button,bool active){if(button is not null)button.Background=active?OfficePalette.Brush("F4D9CC"):OfficePalette.Brush("00FFFFFF");}
        Mark(_bold,(Viewport.CurrentTextStyle ?? Session.PrimaryShape?.TextStyle)?.Bold==true);Mark(_italic,(Viewport.CurrentTextStyle ?? Session.PrimaryShape?.TextStyle)?.Italic==true);Mark(_underline,(Viewport.CurrentTextStyle ?? Session.PrimaryShape?.TextStyle)?.Underline==true);_syncing=false;
        if(!e.IsPreview&&!ReferenceEquals(_observed,Session.Document)){_observed=Session.Document;if(_autoSave&&Storage is not null){_recoveryTimer.Stop();_recoveryTimer.Start();_status.Message="Saving local recovery…";}}
        _status.SetZoom(Viewport.Zoom);
    }
    public void ShowInspector(InspectorMode mode){Viewport.CommitText();_format.Mode=mode;_formatColumn.Width=new GridLength(296);_format.Rebuild();}
    public void HideInspector()=>_formatColumn.Width=new GridLength(0);
    public void ToggleNotes(){_notes.Commit();_notesRow.Height=new GridLength(_notesRow.Height.Value>0?0:100);}
    public void ShowNormal(){Viewport.Visibility=Visibility.Visible;_sorter.Visibility=Visibility.Collapsed;Viewport.Focus(FocusState.Programmatic);ViewChanged?.Invoke(this,EventArgs.Empty);}
    public void ShowSorter(){FlushEdits();Viewport.Visibility=Visibility.Collapsed;_sorter.Visibility=Visibility.Visible;ViewChanged?.Invoke(this,EventArgs.Empty);}
    public void StartShow(bool fromBeginning){FlushEdits();if(Session.Document.Slides.All(s=>s.Hidden)){Notice("Every slide is hidden. Unhide a slide before presenting.");return;}_player.Start(Session.Document,fromBeginning?0:Session.SlideIndex);ViewChanged?.Invoke(this,EventArgs.Empty);}
    public void FlushEdits(){Viewport.CommitText();_notes.Commit();Session.CommitGesture();}
    private void Insert(ShapeKind kind){FlushEdits();Session.Insert(kind);ShowNormal();if(kind==ShapeKind.Text)Viewport.EditText();if(kind is ShapeKind.Chart or ShapeKind.Table)ShowInspector(InspectorMode.Format);}
    private void NewSlide(string layout="Title and content"){FlushEdits();Session.AddSlide(layout);ShowNormal();}
    private static bool Key(VirtualKey key)=>(Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key)&CoreVirtualKeyStates.Down)!=0;
    private void HandleKey(object sender,KeyRoutedEventArgs e)
    {
        if(IsPresenting)return;bool ctrl=Key(VirtualKey.Control),shift=Key(VirtualKey.Shift),alt=Key(VirtualKey.Menu);
        if(ctrl&&e.Key==VirtualKey.S)Run(SaveNativeAsync);else if(ctrl&&e.Key==VirtualKey.O)Run(OpenAsync);else if(ctrl&&e.Key==VirtualKey.M)NewSlide();else if(e.Key==VirtualKey.F5)StartShow(!shift);else if(alt&&e.Key==VirtualKey.Q)FocusCommandSearch();else if(e.Key==VirtualKey.Escape&&_backstage?.Visibility==Visibility.Visible)CloseBackstage();else return;e.Handled=true;
    }
    private void BuildCommands()
    {
        _commands.AddRange([("New slide",()=>NewSlide()),("Open presentation",()=>Run(OpenAsync)),("Save presentation",()=>Run(SaveNativeAsync)),("Insert text box",()=>Insert(ShapeKind.Text)),("Insert rectangle",()=>Insert(ShapeKind.Rectangle)),("Insert picture",()=>Run(InsertPictureAsync)),("Insert table",()=>Insert(ShapeKind.Table)),("Insert chart",()=>Insert(ShapeKind.Chart)),("Undo",()=>Session.Undo()),("Redo",()=>Session.Redo()),("Find and replace",()=>Run(FindReplaceAsync)),("Slide sorter",ShowSorter),("Normal view",ShowNormal),("Selection pane",()=>ShowInspector(InspectorMode.Selection)),("Format shape",()=>ShowInspector(InspectorMode.Format)),("Comments",()=>ShowInspector(InspectorMode.Comments)),("Start slide show",()=>StartShow(true)),("Export PowerPoint",()=>Run(()=>ExportAsync("pptx"))),("Export PDF",()=>Run(()=>ExportAsync("pdf"))),("Export PNG",()=>Run(()=>ExportAsync("png")))]);
        _search.TextChanged+=(_,e)=>{if(e.Reason==AutoSuggestionBoxTextChangeReason.UserInput)_search.ItemsSource=_commands.Where(c=>c.Title.Contains(_search.Text,StringComparison.OrdinalIgnoreCase)).Select(c=>c.Title).Take(10).ToArray();};
        _search.QuerySubmitted+=SubmitCommand;
    }
    private static Button SmallButton(string text,Action action){var b=new Button{Content=text,FontSize=11,Padding=new(11,4,11,4),MinHeight=29,CornerRadius=new(4),Background=OfficePalette.Brush("00FFFFFF"),BorderThickness=new(0)};b.Click+=(_,_)=>action();return b;}
    private static Button Quick(string label,string glyph,Action action){var b=new Button{Content=new FontIcon{Glyph=glyph,FontSize=14},Padding=new(4),MinWidth=25,MinHeight=27,Background=OfficePalette.Brush("00FFFFFF"),BorderThickness=new(0)};ToolTipService.SetToolTip(b,label);AutomationProperties.SetName(b,label);b.Click+=(_,_)=>action();return b;}
    private static MenuFlyout MakeMenu(IEnumerable<(string Title,Action Execute)> actions){var menu=new MenuFlyout();foreach(var (title,action)in actions){var item=new MenuFlyoutItem{Text=title};item.Click+=(_,_)=>action();menu.Items.Add(item);}return menu;}
    private void Notice(string message,bool error=false){_notice.Title=error?"Action could not be completed":"PresentationSpace";_notice.Message=message;_notice.Severity=error?InfoBarSeverity.Error:InfoBarSeverity.Informational;_notice.IsOpen=true;}
    private async void Run(Func<Task> operation){if(_busy)return;_busy=true;try{await operation();}catch(Exception e){Notice(e.Message,true);}finally{_busy=false;}}
}
