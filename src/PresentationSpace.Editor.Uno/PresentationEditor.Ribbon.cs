using System.Collections.Immutable;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PresentationSpace.Core;
using PresentationSpace.Controls.Uno;
using PresentationSpace.Ribbon.Uno;
namespace PresentationSpace.Editor.Uno;

public sealed partial class PresentationEditor
{
    private RibbonCommandButton Cmd(string id,string label,string glyph,Action action,bool large=true,string? shortcut=null)=>new(id,label,glyph,()=>{Viewport.CommitText();action();},large,shortcut);
    private RibbonCommandButton Menu(string id,string label,string glyph,IEnumerable<(string Title,Action Execute)> items,bool large=true){var b=Cmd(id,label,glyph,()=>{},large);b.Flyout=MakeMenu(items);return b;}
    private RibbonCommandButton Colors(string id,string label,string glyph,Action<string> selected,bool large=false){var button=Cmd(id,label,glyph,()=>{},large);var colors=new ColorPalette{Margin=new(5)};var flyout=new Flyout{Content=colors};colors.ColorSelected+=(_,color)=>{selected(color);flyout.Hide();};button.Flyout=flyout;return button;}
    private static CheckBox Check(string title,bool value,Action<bool> action){var c=new CheckBox{Content=title,IsChecked=value,FontSize=11,MinHeight=0,Height=27,Padding=new(0)};c.Checked+=(_,_)=>action(true);c.Unchecked+=(_,_)=>action(false);return c;}
    private void BuildRibbon()
    {
        BuildFidelityCommands();
        Ribbon.SetTabs([new("File",()=>[]),new("Home",HomeGroups),new("Insert",InsertGroups),new("Draw",DrawGroups),new("Design",DesignGroups),new("Transitions",TransitionGroups),new("Animations",AnimationGroups),new("Slide Show",ShowGroups),new("Review",ReviewGroups),new("View",ViewGroups),new("Shape Format",FormatGroups),new("Table Design",TableDesignGroups),new("Help",HelpGroups)]);
        Ribbon.SelectTab("Home");Ribbon.TabChanged+=(_,title)=>{if(title=="File")ShowBackstage();};
    }
    private IEnumerable<RibbonGroup> HomeGroups()
    {
        yield return new("Clipboard",Cmd("paste","Paste","\uE77F",Session.Paste,true,"Ctrl+V"),RibbonGroup.Column(Cmd("cut","Cut","\uE8C6",Session.Cut,false,"Ctrl+X"),Cmd("copy","Copy","\uE8C8",Session.Copy,false,"Ctrl+C"),Cmd("duplicate","Duplicate","\uE8A5",Session.DuplicateSelection,false,"Ctrl+D")));
        yield return new("Slides",Cmd("new-slide","New\nSlide","\uE710",()=>NewSlide(),true,"Ctrl+M"),RibbonGroup.Column(Menu("layout","Layout  ⌄","\uE8A1",new[]{"Title slide","Title and content","Two content","Title only","Blank"}.Select(name=>(name,(Action)(()=>SetLayout(name)))),false),Cmd("duplicate-slide","Duplicate Slide","\uE8A5",Session.DuplicateSlide,false),Cmd("delete-slide","Delete Slide","\uE74D",Session.DeleteSlide,false)));
        var fontPanel=new StackPanel{Spacing=5,Margin=new(4,3,4,0)};var faceRow=new StackPanel{Orientation=Orientation.Horizontal,Spacing=5};
        _fontFamily=new ComboBox{ItemsSource=new[]{"Arial","Aptos","Calibri","Segoe UI","Verdana","Georgia","Times New Roman","Courier New"},SelectedItem=Session.PrimaryShape?.TextStyle.FontFamily??"Arial",Width=137,FontSize=11,MinHeight=0,Height=29,Padding=new(6,3,6,3)};_fontFamily.SelectionChanged+=(_,_)=>{if(!_syncing&&_fontFamily.SelectedItem is string family)Viewport.FormatText("Font family",style=>style with{FontFamily=family});};
        _fontSize=new ComboBox{ItemsSource=new[]{"12","14","16","18","20","24","28","32","36","40","44","48","54","60","72","80","96","120"},SelectedItem=(Session.PrimaryShape?.TextStyle.FontSize??28).ToString("0",CultureInfo.InvariantCulture),Width=63,MinHeight=0,Height=29,Padding=new(6,3,6,3),FontSize=11};_fontSize.SelectionChanged+=(_,_)=>{if(!_syncing&&float.TryParse(_fontSize.SelectedItem as string,NumberStyles.Float,CultureInfo.InvariantCulture,out var size))Viewport.FormatText("Font size",style=>style with{FontSize=size});};faceRow.Children.Add(_fontFamily);faceRow.Children.Add(_fontSize);fontPanel.Children.Add(faceRow);
        var fontRow=new StackPanel{Orientation=Orientation.Horizontal,Spacing=1};_bold=Cmd("bold","","\uE8DD",Viewport.ToggleBold,false);_italic=Cmd("italic","","\uE8DB",Viewport.ToggleItalic,false);_underline=Cmd("underline","","\uE8DC",Viewport.ToggleUnderline,false);
        ToolTipService.SetToolTip(_bold,"Bold");ToolTipService.SetToolTip(_italic,"Italic");ToolTipService.SetToolTip(_underline,"Underline");fontRow.Children.Add(_bold);fontRow.Children.Add(_italic);fontRow.Children.Add(_underline);fontRow.Children.Add(Cmd("grow-font","","\uE8E8",()=>Viewport.FormatText("Increase font size",style=>style with{FontSize=Math.Min(512,style.FontSize+2)}),false));fontRow.Children.Add(Cmd("shrink-font","","\uE8E7",()=>Viewport.FormatText("Decrease font size",style=>style with{FontSize=Math.Max(1,style.FontSize-2)}),false));fontRow.Children.Add(Colors("font-color","A  ⌄","\uE8D3",color=>Viewport.FormatText("Font color",style=>style with{Color=color})));fontPanel.Children.Add(fontRow);yield return new("Font",fontPanel);
        var paragraph=new StackPanel{Spacing=5,Margin=new(2,4,2,0)};var p1=new StackPanel{Orientation=Orientation.Horizontal};p1.Children.Add(Cmd("bullets","","\uE8FD",Viewport.ToggleBullets,false));p1.Children.Add(Menu("line-spacing","Spacing  ⌄","\uE8F1",new[]{1f,1.15f,1.5f,2f}.Select(value=>(value.ToString(CultureInfo.InvariantCulture),(Action)(()=>Viewport.FormatParagraph("Line spacing",style=>style with{LineSpacing=value})))),false));paragraph.Children.Add(p1);
        var p2=new StackPanel{Orientation=Orientation.Horizontal};p2.Children.Add(Cmd("align-text-left","","\uE8E4",()=>TextAlign(ParagraphAlignment.Left),false));p2.Children.Add(Cmd("align-text-center","","\uE8E3",()=>TextAlign(ParagraphAlignment.Center),false));p2.Children.Add(Cmd("align-text-right","","\uE8E2",()=>TextAlign(ParagraphAlignment.Right),false));paragraph.Children.Add(p2);yield return new("Paragraph",paragraph);
        yield return new("Drawing",Menu("shapes","Shapes  ⌄","\uE91B",ShapeActions()),RibbonGroup.Column(Colors("shape-fill","Shape Fill  ⌄","\uE790",color=>Session.Apply("Shape fill",s=>s with{Fill=color})),Colors("shape-outline","Shape Outline  ⌄","\uE70F",color=>Session.Apply("Shape outline",s=>s with{Stroke=color})),Cmd("format-pane","Format Shape","\uE713",()=>ShowInspector(InspectorMode.Format),false)));
        yield return new("Arrange",Menu("arrange","Arrange  ⌄","\uE8A1",ArrangeActions()));
        yield return new("Editing",RibbonGroup.Column(Cmd("find","Find / Replace","\uE721",()=>Run(FindReplaceAsync),false),Cmd("select","Select All","\uE8B3",Session.SelectAll,false),Cmd("selection-pane","Selection Pane","\uE8A4",()=>ShowInspector(InspectorMode.Selection),false)));
    }
    private IEnumerable<(string Title,Action Execute)> ShapeActions()=>new[]{ShapeKind.Rectangle,ShapeKind.RoundRectangle,ShapeKind.Ellipse,ShapeKind.Triangle,ShapeKind.Diamond,ShapeKind.Line,ShapeKind.Arrow}.Select(kind=>(kind.ToString(),(Action)(()=>Insert(kind))));
    private IEnumerable<(string Title,Action Execute)> ArrangeActions()
    {
        yield return("Bring to Front",Session.BringToFront);yield return("Send to Back",Session.SendToBack);yield return("Group",Session.Group);yield return("Ungroup",Session.Ungroup);
        foreach(var kind in Enum.GetValues<AlignKind>())yield return("Align "+kind,()=>Session.Align(kind));yield return("Distribute Horizontally",()=>Session.Distribute(true));yield return("Distribute Vertically",()=>Session.Distribute(false));yield return("Selection Pane",()=>ShowInspector(InspectorMode.Selection));
    }
    private IEnumerable<RibbonGroup> InsertGroups()
    {
        yield return new("Slides",Cmd("insert-new-slide","New Slide","\uE710",()=>NewSlide()));
        yield return new("Tables",Cmd("insert-table","Table","\uE80A",()=>Insert(ShapeKind.Table)));
        yield return new("Images",Cmd("insert-picture","Pictures","\uE91B",()=>Run(InsertPictureAsync)));
        yield return new("Illustrations",Menu("insert-shapes","Shapes  ⌄","\uE91B",ShapeActions()),Cmd("insert-chart","Chart","\uE9D2",()=>Insert(ShapeKind.Chart)));
        yield return new("Text",Cmd("insert-text","Text Box","\uE8D2",()=>Insert(ShapeKind.Text)),Cmd("slide-number","Slide\nNumber","\uE8D5",AddSlideNumbers));
        yield return new("Comments",Cmd("insert-comment","Comment","\uE90A",()=>ShowInspector(InspectorMode.Comments)));
    }
    private IEnumerable<RibbonGroup> DrawGroups()
    {
        yield return new("Drawing tools",Cmd("draw-line","Line","\uE70F",()=>Insert(ShapeKind.Line)),Cmd("draw-arrow","Arrow","\uE72A",()=>Insert(ShapeKind.Arrow)),Menu("draw-shapes","Shapes  ⌄","\uE91B",ShapeActions()));
        yield return new("Style",Colors("draw-outline","Line Color","\uE790",color=>Session.Apply("Line color",s=>s with{Stroke=color}),true),Menu("draw-width","Line Width","\uE8E4",new[]{1f,2,3,4,6,8}.Select(w=>(w+" px",(Action)(()=>Session.Apply("Line width",s=>s with{StrokeWidth=w}))))));
    }
    private IEnumerable<RibbonGroup> DesignGroups()
    {
        var themes=new List<UIElement>();foreach(string theme in new[]{"Office","Ocean","Forest","Violet","Slate"})
        {
            var preview=new SlidePreview{Width=115,Height=65};var sample=SlideFactory.ApplyTheme(SlideFactory.Welcome(),theme);preview.SetSlide(sample,sample.Slides[0]);var content=new StackPanel{Spacing=2};content.Children.Add(preview);content.Children.Add(OfficePalette.Text(theme,10));var button=new Button{Content=content,Padding=new(3),BorderThickness=new(1),BorderBrush=Session.Document.Theme==theme?OfficePalette.Accent:OfficePalette.Line,Background=OfficePalette.White};button.Click+=(_,_)=>Session.EditDocument("Apply theme",d=>SlideFactory.ApplyTheme(d,theme));themes.Add(button);
        }
        yield return new("Themes",themes.ToArray());yield return new("Customize",Menu("slide-size","Slide Size  ⌄","\uE740",[("Widescreen (16:9)",()=>ResizeSlides(1280,720)),("Standard (4:3)",()=>ResizeSlides(960,720)),("Portrait (9:16)",()=>ResizeSlides(720,1280))]),Colors("background-color","Background","\uE790",color=>Session.EditSlide("Slide background",s=>s with{Background=color}),true),Cmd("background-pane","Format\nBackground","\uE713",()=>{Session.Select(null);ShowInspector(InspectorMode.Format);}));
    }
    private IEnumerable<RibbonGroup> TransitionGroups()
    {
        yield return new("Preview",Cmd("preview-transition","Preview","\uE768",()=>StartShow(false)));
        yield return new("Transition to This Slide",Enum.GetValues<TransitionKind>().Select(kind=>(UIElement)Cmd("transition-"+kind,kind.ToString(),"\uE8B2",()=>Session.EditSlide("Slide transition",s=>s with{Transition=kind}))).ToArray());
        yield return new("Timing",Menu("transition-duration","Duration","\uE823",new[]{.3f,.5f,1,2}.Select(t=>(t.ToString(CultureInfo.InvariantCulture)+" seconds",(Action)(()=>Session.EditSlide("Transition duration",s=>s with{TransitionDuration=t}))))),Cmd("transition-all","Apply\nto All","\uE8B3",()=>{var source=Session.CurrentSlide;Session.EditDocument("Apply transition to all",d=>d with{Slides=d.Slides.Select(s=>s with{Transition=source.Transition,TransitionDuration=source.TransitionDuration}).ToImmutableArray()});}));
    }
    private IEnumerable<RibbonGroup> AnimationGroups()
    {
        yield return new("Preview",Cmd("preview-animation","Preview","\uE768",()=>StartShow(false)));
        yield return new("Animation",Enum.GetValues<AnimationKind>().Select(kind=>(UIElement)Cmd("animation-"+kind,kind.ToString(),"\uE734",()=>Session.Apply("Object animation",s=>s with{Animation=kind,AnimationOrder=Session.CurrentSlide.Shapes.IndexOf(s)}))).ToArray());
        yield return new("Timing",Menu("animation-duration","Duration","\uE823",new[]{.3f,.5f,1,2}.Select(t=>(t.ToString(CultureInfo.InvariantCulture)+" seconds",(Action)(()=>Session.Apply("Animation duration",s=>s with{AnimationDuration=t}))))),Cmd("animation-selection","Selection\nPane","\uE8A4",()=>ShowInspector(InspectorMode.Selection)));
    }
    private IEnumerable<RibbonGroup> ShowGroups()
    {
        yield return new("Start Slide Show",Cmd("show-beginning","From\nBeginning","\uE768",()=>StartShow(true),true,"F5"),Cmd("show-current","From Current\nSlide","\uE8A1",()=>StartShow(false),true,"Shift+F5"));
        yield return new("Set Up",Cmd("hide-slide","Hide / Show\nSlide","\uED1A",()=>Session.EditSlide("Toggle hidden slide",s=>s with{Hidden=!s.Hidden})),Cmd("show-notes","Speaker\nNotes","\uE70B",ToggleNotes));
    }
    private IEnumerable<RibbonGroup> ReviewGroups()
    {
        yield return new("Proofing",Cmd("review-find","Find and\nReplace","\uE721",()=>Run(FindReplaceAsync)));
        yield return new("Comments",Cmd("review-comments","Comments","\uE90A",()=>ShowInspector(InspectorMode.Comments)));
        yield return new("Accessibility",Cmd("review-structure","Selection\nPane","\uE8A4",()=>ShowInspector(InspectorMode.Selection)),Cmd("review-notes","Speaker\nNotes","\uE70B",ToggleNotes));
    }
    private IEnumerable<RibbonGroup> ViewGroups()
    {
        yield return new("Presentation Views",Cmd("view-normal","Normal","\uE8A1",ShowNormal),Cmd("view-sorter","Slide\nSorter","\uE80A",ShowSorter));
        yield return new("Show",RibbonGroup.Column(Check("Gridlines",Viewport.ShowGrid,value=>{Viewport.ShowGrid=value;Viewport.Refresh();}),Check("Guides",Viewport.ShowGuides,value=>{Viewport.ShowGuides=value;Viewport.Refresh();}),Check("Snap to grid",Session.SnapToGrid,value=>Session.SnapToGrid=value)));
        yield return new("Zoom",Cmd("view-fit","Fit to\nWindow","\uE740",Viewport.Fit),Menu("view-zoom","Zoom","\uE71E",new[]{.25f,.5f,.75f,1,1.5f,2,4}.Select(z=>($"{z*100:0}%",(Action)(()=>Viewport.SetZoom(z))))));
        yield return new("Panes",Cmd("view-selection","Selection\nPane","\uE8A4",()=>ShowInspector(InspectorMode.Selection)),Cmd("view-format","Format\nPane","\uE713",()=>ShowInspector(InspectorMode.Format)),Cmd("view-notes","Notes","\uE70B",ToggleNotes));
        yield return new("Ribbon",Cmd("collapse-ribbon","Collapse\nRibbon","\uE70E",Ribbon.ToggleCollapsed));
    }
    private IEnumerable<RibbonGroup> FormatGroups()
    {
        yield return new("Insert Shapes",Menu("format-shapes","Shapes  ⌄","\uE91B",ShapeActions()),Cmd("format-text","Text Box","\uE8D2",()=>Insert(ShapeKind.Text)));
        yield return new("Shape Styles",Colors("format-fill","Shape Fill","\uE790",color=>Session.Apply("Shape fill",s=>s with{Fill=color}),true),Colors("format-outline","Outline","\uE70F",color=>Session.Apply("Shape outline",s=>s with{Stroke=color}),true));
        yield return new("Arrange",Menu("format-arrange","Arrange  ⌄","\uE8A1",ArrangeActions()),Cmd("rotate-right","Rotate\nRight 90°","\uE7AD",()=>Session.Apply("Rotate right",s=>s with{Rotation=(s.Rotation+90)%360})));
        yield return new("Text body", Cmd("text-layout-pane", "Text Box\nLayout", "\uE8D2", () => OpenTextBodyEditor()),
            Cmd("custom-tab-pane", "Custom\nTabs", "\uE8A4", () => OpenTextBodyEditor(true)));
        yield return new("Text layout", Cmd("text-shrink-fit", "Shrink Text\nto Fit", "\uE8D2", () => FitSelectedText(false)), Cmd("text-shape-fit", "Resize Shape\nto Text", "\uE740", () => FitSelectedText(true)));
        yield return new("Size",Cmd("format-size","Size and\nPosition","\uE740",()=>ShowInspector(InspectorMode.Format)));
    }
    private IEnumerable<RibbonGroup> HelpGroups()
    {
        yield return new("Help",Cmd("help-shortcuts","Keyboard\nShortcuts","\uE765",()=>Run(()=>MessageAsync("Keyboard shortcuts","Ctrl+M: new slide\nCtrl+S: save native presentation\nCtrl+O: open\nCtrl+Z / Ctrl+Y: undo / redo\nCtrl+C / X / V / D: copy / cut / paste / duplicate objects\nCtrl+A: select all objects\nCtrl+G / Ctrl+Shift+G: group / ungroup\nArrow keys: nudge; Shift: 10 units\nF2 or double-click: edit text\nF5 / Shift+F5: present from beginning / current\nEsc: end slide show\nCtrl+wheel: zoom\nAlt while dragging: bypass snapping\nShift while resizing: constrain aspect ratio"))),Cmd("help-about","About","\uE946",()=>Run(()=>MessageAsync("PresentationSpace 0.8","An independent presentation editor built with Uno Platform 6.7 and SkiaSharp.\n\nThe ribbon and editing workflow are inspired by PowerPoint. This release is not a full or pixel-exact Microsoft PowerPoint replacement. Advanced native PPTX features, cloud coauthoring, media editing and complex typography remain outside the supported subset.\n\nMIT licensed. Not affiliated with Microsoft."))));
    }
    private void TextAlign(ParagraphAlignment alignment)=>Viewport.FormatParagraph("Text alignment",style=>style with{Alignment=alignment});
    private void SetLayout(string layout){FlushEdits();Session.ApplyLayout(layout);}
    private void ResizeSlides(float width,float height)
    {
        FlushEdits(); Session.EditDocument("Slide size", d => DocumentLayout.Resize(d, width, height)); Viewport.Fit();
    }
    private void AddSlideNumbers()=>Session.EditDocument("Slide numbers",d=>d with{Slides=d.Slides.Select((slide,index)=>slide with{Shapes=slide.Shapes.Where(s=>s.Name!="Slide number").Append(SlideFactory.Text((index+1).ToString(),d.Width-90,d.Height-45,65,30,16,"#7C8492") with{Name="Slide number"}).ToImmutableArray()}).ToImmutableArray()});
}
