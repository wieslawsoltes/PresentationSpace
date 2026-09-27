using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PresentationSpace.Core;
using PresentationSpace.Controls.Uno;
using PresentationSpace.Formats;
using PresentationSpace.Rendering.Skia;
using PresentationSpace.Ribbon.Uno;
namespace PresentationSpace.Editor.Uno;

public sealed partial class PresentationEditor
{
    public async Task RestoreRecoveryAsync()
    {
        if(Storage is null)return;
        try{var json=await Storage.ReadRecoveryAsync();if(string.IsNullOrEmpty(json))return;var doc=DocumentSerializer.Deserialize(json);if(await ConfirmAsync("Recover your presentation?",$"A local recovery copy of “{doc.Title}” is available.","Recover")){Session.Load(doc);_lastRecovery=doc;_status.Message="Recovered from local storage";}}
        catch(Exception e){Notice("Recovery could not be loaded: "+e.Message,true);}
    }
    private async Task SaveRecoveryAsync()
    {
        if(Storage is null||!_autoSave||ReferenceEquals(_lastRecovery,Session.Document))return;
        try{var document=Session.Document;await Storage.WriteRecoveryAsync(DocumentSerializer.Serialize(document));_lastRecovery=document;_status.Message="Local recovery saved";}
        catch(Exception e){_status.Message="Recovery unavailable — save a file";Notice("Local recovery failed. Save your presentation to a file. "+e.Message,true);}
    }
    private IWorkspaceStorage RequireStorage()=>Storage??throw new InvalidOperationException("The host application has not provided a storage service.");
    private async Task OpenAsync()
    {
        FlushEdits();if(!await ConfirmDiscardAsync())return;var file=await RequireStorage().OpenAsync([".pspace",".json",".pptx"]);if(file is null)return;
        if(file.Name.EndsWith(".pptx",StringComparison.OrdinalIgnoreCase)){var result=PptxCodec.Import(file.Data);Session.Load(result.Document);if(result.Warnings.Count>0)Notice(string.Join(" ",result.Warnings));}
        else Session.Load(DocumentSerializer.Deserialize(Encoding.UTF8.GetString(file.Data)));
        CloseBackstage();ShowNormal();Viewport.Fit();_status.Message="Opened "+file.Name;
    }
    private async Task SaveNativeAsync()
    {
        FlushEdits();var bytes=Encoding.UTF8.GetBytes(DocumentSerializer.Serialize(Session.Document));if(await RequireStorage().SaveAsync(SafeName(Session.Document.Title)+".pspace",bytes,"application/json")){Session.MarkSaved();_status.Message="Presentation file saved";}
    }
    private async Task ExportAsync(string format)
    {
        FlushEdits();byte[] bytes;string mime;IReadOnlyList<string> warnings=[];
        if(format=="pptx"){var result=PptxCodec.Export(Session.Document);bytes=result.Data;warnings=result.Warnings;mime="application/vnd.openxmlformats-officedocument.presentationml.presentation";}
        else{using var renderer=new SlideRenderer();if(format=="pdf"){bytes=renderer.ExportPdf(Session.Document);mime="application/pdf";}else{bytes=renderer.ExportPng(Session.Document,Session.CurrentSlide,1920);mime="image/png";}}
        if(await RequireStorage().SaveAsync(SafeName(Session.Document.Title)+(format=="png"?$" - Slide {Session.SlideIndex+1}":"")+"."+format,bytes,mime)){_status.Message="Exported "+format.ToUpperInvariant();if(warnings.Count>0)Notice(string.Join(" ",warnings));}
    }
    private async Task InsertPictureAsync()
    {
        var file=await RequireStorage().OpenAsync([".png",".jpg",".jpeg",".gif",".webp"]);if(file is null)return;
        using var data=SkiaSharp.SKData.CreateCopy(file.Data);using var codec=SkiaSharp.SKCodec.Create(data);if(codec is null)throw new InvalidDataException("This picture could not be decoded.");if((long)codec.Info.Width*codec.Info.Height>16_000_000)throw new InvalidDataException("Pictures are limited to 16 megapixels.");
        string extension=Path.GetExtension(file.Name).ToLowerInvariant(),mime=extension is ".jpg" or ".jpeg"?"image/jpeg":extension==".webp"?"image/webp":extension==".gif"?"image/gif":"image/png";
        Session.InsertImage(file.Data,mime,file.Name);float width=Math.Min(700,Session.Document.Width*.65f),height=width*codec.Info.Height/codec.Info.Width;if(height>Session.Document.Height*.75f){height=Session.Document.Height*.75f;width=height*codec.Info.Width/codec.Info.Height;}
        Session.Apply("Picture size",s=>s with{Bounds=new((Session.Document.Width-width)/2,(Session.Document.Height-height)/2,width,height)});ShowNormal();
    }
    private async Task RenameAsync()
    {
        var text=new TextBox{Text=Session.Document.Title,MaxLength=120,MinWidth=320};var dialog=new ContentDialog{XamlRoot=XamlRoot,Title="Presentation name",Content=text,PrimaryButtonText="Rename",CloseButtonText="Cancel",DefaultButton=ContentDialogButton.Primary};if(await dialog.ShowAsync()==ContentDialogResult.Primary&&!string.IsNullOrWhiteSpace(text.Text))Session.EditDocument("Rename presentation",d=>d with{Title=text.Text.Trim()});
    }
    private async Task FindReplaceAsync()
    {
        var find=new TextBox{Header="Find",PlaceholderText="Text to find",MinWidth=340};var replacement=new TextBox{Header="Replace with",PlaceholderText="Replacement text"};var body=new StackPanel{Spacing=15};body.Children.Add(find);body.Children.Add(replacement);body.Children.Add(new TextBlock{Text="Replaces matching text across every slide. You can undo this operation.",FontSize=11,TextWrapping=TextWrapping.Wrap});var dialog=new ContentDialog{XamlRoot=XamlRoot,Title="Find and replace",Content=body,PrimaryButtonText="Replace all",CloseButtonText="Cancel"};if(await dialog.ShowAsync()==ContentDialogResult.Primary&&!string.IsNullOrEmpty(find.Text)){Session.ReplaceText(find.Text,replacement.Text);_status.Message="Replace all complete";}
    }
    private async Task<bool> ConfirmDiscardAsync()
    {
        if(!Session.IsDirty)return true;return await ConfirmAsync("Unsaved changes","Opening or creating a presentation will replace the current editor contents. Save a native .pspace file first to keep all editing features.","Continue without saving");
    }
    private async Task<bool> ConfirmAsync(string title,string message,string primary)
    {
        var dialog=new ContentDialog{XamlRoot=XamlRoot,Title=title,Content=new TextBlock{Text=message,TextWrapping=TextWrapping.Wrap,MaxWidth=440},PrimaryButtonText=primary,CloseButtonText="Cancel",DefaultButton=ContentDialogButton.Close};return await dialog.ShowAsync()==ContentDialogResult.Primary;
    }
    private async Task MessageAsync(string title,string message){var dialog=new ContentDialog{XamlRoot=XamlRoot,Title=title,Content=new ScrollViewer{Content=new TextBlock{Text=message,TextWrapping=TextWrapping.Wrap,FontSize=13,MaxWidth=540},MaxHeight=550},CloseButtonText="Close"};await dialog.ShowAsync();}
    private static string SafeName(string name){foreach(char c in Path.GetInvalidFileNameChars())name=name.Replace(c,'_');return string.IsNullOrWhiteSpace(name)?"Presentation":name;}
    private void ShowBackstage()
    {
        FlushEdits();if(_backstage is not null){_backstage.Visibility=Visibility.Visible;return;}
        _backstage=new Grid{Background=OfficePalette.Brush("FBFAF9"),ColumnDefinitions={new(){Width=new GridLength(190)},new(){Width=new GridLength(1,GridUnitType.Star)}};
        var nav=new StackPanel{Background=OfficePalette.Accent,Padding=new(18,22,18,22),Spacing=7};
        void Nav(string text,Action action){var button=SmallButton(text,action);button.Foreground=OfficePalette.White;button.HorizontalAlignment=HorizontalAlignment.Stretch;button.HorizontalContentAlignment=HorizontalAlignment.Left;button.FontSize=14;button.Height=42;nav.Children.Add(button);}
        Nav("‹  Back",CloseBackstage);Nav("New",()=>Run(()=>CreateAsync(false)));Nav("Open",()=>Run(OpenAsync));Nav("Save",()=>Run(SaveNativeAsync));Nav("Export PowerPoint",()=>Run(()=>ExportAsync("pptx")));Nav("Export PDF",()=>Run(()=>ExportAsync("pdf")));Nav("Export PNG",()=>Run(()=>ExportAsync("png")));Nav("About",()=>Run(()=>MessageAsync("About PresentationSpace","An independent Uno Platform / SkiaSharp presentation editor. MIT licensed. PowerPoint is a trademark of Microsoft; this application is not affiliated with Microsoft.")));_backstage.Children.Add(nav);
        var content=new StackPanel{Margin=new(48,38,48,38),Spacing=22};content.Children.Add(OfficePalette.Text("Good ideas start here.",32,true));content.Children.Add(new TextBlock{Text="Create a presentation, open an existing file, or explore the sample deck.",FontSize=14,Foreground=OfficePalette.Muted,TextWrapping=TextWrapping.Wrap});
        var cards=new StackPanel{Orientation=Orientation.Horizontal,Spacing=22};
        foreach(bool sample in new[]{false,true})
        {
            var doc=sample?SlideFactory.Welcome():new PresentationDocument{Slides=[SlideFactory.Create("Title slide")]};var preview=new SlidePreview{Width=300,Height=169};preview.SetSlide(doc,doc.Slides[0]);var p=new StackPanel{Spacing=12};p.Children.Add(preview);p.Children.Add(OfficePalette.Text(sample?"A clearer way to present":"Blank presentation",14,true));
            var button=new Button{Content=p,Padding=new(14),Background=OfficePalette.White,BorderBrush=OfficePalette.Line,BorderThickness=new(1),CornerRadius=new(8)};button.Click+=(_,_)=>Run(()=>CreateAsync(sample));cards.Children.Add(button);
        }
        content.Children.Add(new ScrollViewer{Content=cards,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled});
        content.Children.Add(OfficePalette.Text("YOUR FILES STAY YOURS",11,true));content.Children.Add(new TextBlock{Text="Editing happens locally. Open and save portable .pspace presentations, import supported PowerPoint content, and export editable PPTX, PNG or PDF. AutoSave stores a recovery copy on this device—it is not a cloud account.",FontSize=13,TextWrapping=TextWrapping.Wrap,MaxWidth=720,HorizontalAlignment=HorizontalAlignment.Left,Foreground=OfficePalette.Muted});content.Children.Add(SmallButton("Open a presentation…",()=>Run(OpenAsync)));
        var scroll=new ScrollViewer{Content=content};Grid.SetColumn(scroll,1);_backstage.Children.Add(scroll);Grid.SetRow(_backstage,1);Grid.SetRowSpan(_backstage,4);_root.Children.Add(_backstage);
    }
    private void CloseBackstage(){if(_backstage is not null)_backstage.Visibility=Visibility.Collapsed;if(Ribbon.SelectedTab=="File")Ribbon.SelectTab("Home");Viewport.Focus(FocusState.Programmatic);}
    private async Task CreateAsync(bool sample){if(!await ConfirmDiscardAsync())return;Session.Load(sample?SlideFactory.Welcome():new PresentationDocument{Slides=[SlideFactory.Create("Title slide")]});CloseBackstage();ShowNormal();Viewport.Fit();}
}
