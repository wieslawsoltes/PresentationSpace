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
    private readonly SemaphoreSlim _recoveryLock = new(1, 1);

    public async Task RestoreRecoveryAsync()
    {
        if (Storage is null) return;
        try
        {
            var json = await Storage.ReadRecoveryAsync();
            if (string.IsNullOrEmpty(json)) return;
            var document = DocumentSerializer.Deserialize(json);
            if (await ConfirmAsync("Recover your presentation?", $"A local recovery copy of “{document.Title}” is available.", "Recover"))
            {
                Session.Load(document);
                _lastRecovery = document;
                _status.Message = "Recovered from local storage";
            }
        }
        catch (Exception error) { Notice("Recovery could not be loaded: " + error.Message, true); }
    }

    private async Task SaveRecoveryAsync()
    {
        if (Storage is null || !_autoSave) return;
        await _recoveryLock.WaitAsync();
        try
        {
            var document = Session.Document;
            if (ReferenceEquals(_lastRecovery, document)) return;
            await Storage.WriteRecoveryAsync(DocumentSerializer.Serialize(document));
            _lastRecovery = document;
            _status.Message = "Local recovery saved";
        }
        catch (Exception error)
        {
            _status.Message = "Recovery unavailable — save a file";
            Notice("Local recovery failed. Save your presentation to a file. " + error.Message, true);
        }
        finally { _recoveryLock.Release(); }
    }

    private IWorkspaceStorage RequireStorage() => Storage ?? throw new InvalidOperationException("The host application has not provided a storage service.");

    private async Task OpenAsync()
    {
        FlushEdits();
        if (!await ConfirmDiscardAsync()) return;
        var file = await RequireStorage().OpenAsync([".pspace", ".json", ".pptx"]);
        if (file is null) return;
        if (file.Name.EndsWith(".pptx", StringComparison.OrdinalIgnoreCase))
        {
            var result = PptxCodec.Import(file.Data);
            Session.Load(result.Document);
            if (result.Warnings.Count > 0) Notice(string.Join(" ", result.Warnings));
        }
        else Session.Load(DocumentSerializer.Deserialize(Encoding.UTF8.GetString(file.Data)));
        CloseBackstage();
        ShowNormal();
        Viewport.Fit();
        _status.Message = "Opened " + file.Name;
    }

    private async Task SaveNativeAsync()
    {
        FlushEdits();
        var document = Session.Document;
        var bytes = Encoding.UTF8.GetBytes(DocumentSerializer.Serialize(document));
        if (await RequireStorage().SaveAsync(SafeName(document.Title) + ".pspace", bytes, "application/json"))
        {
            if (ReferenceEquals(document, Session.Document)) Session.MarkSaved();
            _status.Message = ReferenceEquals(document, Session.Document) ? "Presentation file saved" : "Snapshot saved; newer changes remain unsaved";
        }
    }

    private async Task ExportAsync(string format)
    {
        FlushEdits();
        var document = Session.Document;
        int slideIndex = Session.SlideIndex;
        byte[] bytes;
        string mime;
        IReadOnlyList<string> warnings = [];
        if (format == "pptx")
        {
            var result = PptxCodec.Export(document);
            bytes = result.Data;
            warnings = result.Warnings;
            mime = "application/vnd.openxmlformats-officedocument.presentationml.presentation";
        }
        else
        {
            using var renderer = new SlideRenderer();
            if (format == "pdf") { bytes = renderer.ExportPdf(document); mime = "application/pdf"; }
            else { bytes = renderer.ExportPng(document, document.Slides[slideIndex], 1920); mime = "image/png"; }
        }
        if (warnings.Count > 0) Notice(string.Join(" ", warnings));
        string filename = SafeName(document.Title) + (format == "png" ? $" - Slide {slideIndex + 1}" : "") + "." + format;
        if (await RequireStorage().SaveAsync(filename, bytes, mime)) _status.Message = "Exported " + format.ToUpperInvariant();
    }

    private async Task InsertPictureAsync()
    {
        var file = await RequireStorage().OpenAsync([".png", ".jpg", ".jpeg", ".gif", ".webp"]);
        if (file is null) return;
        using var data = SkiaSharp.SKData.CreateCopy(file.Data);
        using var codec = SkiaSharp.SKCodec.Create(data);
        if (codec is null) throw new InvalidDataException("This picture could not be decoded.");
        if ((long)codec.Info.Width * codec.Info.Height > 16_000_000) throw new InvalidDataException("Pictures are limited to 16 megapixels.");
        string mime = RasterHeader.Read(file.Data).MimeType;
        Session.InsertImage(file.Data, mime, file.Name);
        float width = Math.Min(700, Session.Document.Width * .65f), height = width * codec.Info.Height / codec.Info.Width;
        if (height > Session.Document.Height * .75f) { height = Session.Document.Height * .75f; width = height * codec.Info.Width / codec.Info.Height; }
        Session.Apply("Picture size", shape => shape with { Picture = new() { Fit = PictureFit.Contain }, Bounds = new((Session.Document.Width - width) / 2, (Session.Document.Height - height) / 2, width, height) });
        ShowNormal(); Ribbon.SelectTab("Picture Format");
    }

    private async Task RenameAsync()
    {
        var text = new TextBox { Text = Session.Document.Title, MaxLength = 120, MinWidth = 0, Width = Math.Min(320, Math.Max(160, ActualWidth - 88)) };
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Presentation name", Content = text, PrimaryButtonText = "Rename", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(text.Text))
            Session.EditDocument("Rename presentation", document => document with { Title = text.Text.Trim() });
    }

    private async Task FindReplaceAsync()
    {
        var find = new TextBox { Header = "Find", PlaceholderText = "Text to find", MinWidth = 0, Width = Math.Min(340, Math.Max(160, ActualWidth - 88)) };
        var replacement = new TextBox { Header = "Replace with", PlaceholderText = "Replacement text" };
        var body = new StackPanel { Spacing = 15 };
        body.Children.Add(find);
        body.Children.Add(replacement);
        body.Children.Add(new TextBlock { Text = "Replaces matching text across every slide. You can undo this operation.", FontSize = 11, TextWrapping = TextWrapping.Wrap });
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Find and replace", Content = body, PrimaryButtonText = "Replace all", CloseButtonText = "Cancel" };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrEmpty(find.Text))
        {
            Session.ReplaceText(find.Text, replacement.Text);
            _status.Message = "Replace all complete";
        }
    }

    private async Task<bool> ConfirmDiscardAsync()
    {
        if (!Session.IsDirty) return true;
        return await ConfirmAsync("Unsaved changes", "Opening or creating a presentation will replace the current editor contents. Save a native .pspace file first to keep all editing features.", "Continue without saving");
    }

    private async Task<bool> ConfirmAsync(string title, string message, string primary)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 440 },
            PrimaryButtonText = primary, CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async Task MessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = title,
            Content = new ScrollViewer { Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 13, MaxWidth = 540 }, MaxHeight = 550 },
            CloseButtonText = "Close"
        };
        await dialog.ShowAsync();
    }

    private static string SafeName(string name)
    {
        foreach (char character in Path.GetInvalidFileNameChars()) name = name.Replace(character, '_');
        return string.IsNullOrWhiteSpace(name) ? "Presentation" : name;
    }

    private void ShowBackstage()
    {
        FlushEdits();
        if (_backstage is not null) { _backstage.Visibility = Visibility.Visible; return; }
        _backstage = new Grid
        {
            Background = OfficePalette.Brush("FBFAF9"),
            ColumnDefinitions =
            {
                new() { Width = new GridLength(ActualWidth < 640 ? 136 : 190) },
                new() { Width = new GridLength(1, GridUnitType.Star) }
            }
        };
        var nav = new StackPanel { Background = OfficePalette.Accent, Padding = new(18, 22, 18, 22), Spacing = 7 };
        void Nav(string text, Action action)
        {
            var button = SmallButton(text, action);
            button.Foreground = OfficePalette.White;
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.FontSize = 14;
            button.Height = 42;
            nav.Children.Add(button);
        }
        Nav("‹  Back", CloseBackstage);
        Nav("New", () => Run(() => CreateAsync(false)));
        Nav("Open", () => Run(OpenAsync));
        Nav("Save", () => Run(SaveNativeAsync));
        Nav("Export PowerPoint", () => Run(() => ExportAsync("pptx")));
        Nav("Export PDF", () => Run(() => ExportAsync("pdf")));
        Nav("Export PNG", () => Run(() => ExportAsync("png")));
        Nav("About", () => Run(() => MessageAsync("About PresentationSpace", "An independent Uno Platform / SkiaSharp presentation editor. MIT licensed. PowerPoint is a trademark of Microsoft; this application is not affiliated with Microsoft.")));
        _backstage.Children.Add(nav);
        var content = new StackPanel { Margin = new(20, 24, 20, 24), Spacing = 22 };
        content.Children.Add(new TextBlock { Text = "Good ideas start here.", FontSize = 28, TextWrapping = TextWrapping.Wrap, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        content.Children.Add(new TextBlock { Text = "Create a presentation, open an existing file, or explore the sample deck.", FontSize = 14, Foreground = OfficePalette.Muted, TextWrapping = TextWrapping.Wrap });
        var cards = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 22 };
        foreach (bool sample in new[] { false, true })
        {
            var document = sample ? SlideFactory.Welcome() : new PresentationDocument { Slides = [SlideFactory.Create("Title slide")] };
            var preview = new SlidePreview { Width = 300, Height = 169 };
            preview.SetSlide(document, document.Slides[0]);
            var panel = new StackPanel { Spacing = 12 };
            panel.Children.Add(preview);
            panel.Children.Add(OfficePalette.Text(sample ? "A clearer way to present" : "Blank presentation", 14, true));
            var button = new Button { Content = panel, Padding = new(14), Background = OfficePalette.White, BorderBrush = OfficePalette.Line, BorderThickness = new(1), CornerRadius = new(8) };
            button.Click += (_, _) => Run(() => CreateAsync(sample));
            cards.Children.Add(button);
        }
        content.Children.Add(new ScrollViewer { Content = cards, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });
        content.Children.Add(OfficePalette.Text("YOUR FILES STAY YOURS", 11, true));
        content.Children.Add(new TextBlock
        {
            Text = "Editing happens locally. Open and save portable .pspace presentations, import supported PowerPoint content, and export editable PPTX, PNG or PDF. AutoSave stores a recovery copy on this device—it is not a cloud account.",
            FontSize = 13, TextWrapping = TextWrapping.Wrap, MaxWidth = 720,
            HorizontalAlignment = HorizontalAlignment.Left, Foreground = OfficePalette.Muted
        });
        content.Children.Add(SmallButton("Open a presentation…", () => Run(OpenAsync)));
        var scroll = new ScrollViewer { Content = content };
        Grid.SetColumn(scroll, 1);
        _backstage.Children.Add(scroll);
        Grid.SetRow(_backstage, 1);
        Grid.SetRowSpan(_backstage, 4);
        _root.Children.Add(_backstage);
    }

    private void CloseBackstage()
    {
        if (_backstage is not null) _backstage.Visibility = Visibility.Collapsed;
        if (Ribbon.SelectedTab == "File") Ribbon.SelectTab("Home");
        Viewport.Focus(FocusState.Programmatic);
    }

    private async Task CreateAsync(bool sample)
    {
        if (!await ConfirmDiscardAsync()) return;
        Session.Load(sample ? SlideFactory.Welcome() : new PresentationDocument { Slides = [SlideFactory.Create("Title slide")] });
        CloseBackstage();
        ShowNormal();
        Viewport.Fit();
    }
}
