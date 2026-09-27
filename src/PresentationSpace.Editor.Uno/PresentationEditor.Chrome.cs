using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PresentationSpace.Controls.Uno;
using PresentationSpace.Ribbon.Uno;
using Windows.Foundation;

namespace PresentationSpace.Editor.Uno;

public sealed partial class PresentationEditor
{
    private readonly CompactToggleSwitch _autoSaveSwitch = new() { IsChecked = true };
    private readonly TextBlock _autoSaveLabel = OfficePalette.Text("AutoSave", 10);
    private readonly Dictionary<string, FrameworkElement> _chrome = [];
    private readonly AutoSuggestBox _compactSearch = new() { PlaceholderText = "Search commands (Alt+Q)", FontSize = 12, MinHeight = 32 };
    private readonly Flyout _searchFlyout = new();
    private Button? _undoQuick, _redoQuick, _commentsQuick, _presentQuick, _shareQuick, _moreQuick;
    private PaneSplitter? _leftSplitter, _rightSplitter;
    private bool _inspectorOpen, _notesVisible = true;

    private T TrackChrome<T>(string name, T element) where T : FrameworkElement
    {
        _chrome[name] = element;
        // Diagnostics names must not replace stable public automation IDs used by hosts.
        if (string.IsNullOrEmpty(AutomationProperties.GetAutomationId(element)))
            AutomationProperties.SetAutomationId(element, name);
        return element;
    }

    private UIElement BuildTitleBar()
    {
        var bar = TrackChrome("title-bar", new Grid
        {
            Background = OfficePalette.Brush("F7EAE5"), Padding = new(12, 0, 12, 0), ColumnSpacing = 8,
            ColumnDefinitions = { new() { Width = GridLength.Auto }, new() { Width = new GridLength(1, GridUnitType.Star) }, new() { Width = GridLength.Auto }, new() { Width = GridLength.Auto } }
        });
        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(TrackChrome("title-logo", new Border
        {
            Background = OfficePalette.Accent, CornerRadius = new(4), Width = 27, Height = 27,
            Margin = new(0, 0, 4, 0), Child = new TextBlock { Text = "P", Foreground = OfficePalette.White, FontSize = 19,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        }));
        left.Children.Add(TrackChrome("autosave-label", _autoSaveLabel));
        left.Children.Add(TrackChrome("autosave-switch", _autoSaveSwitch));
        TrackChrome("autosave-track", _autoSaveSwitch.Track); TrackChrome("autosave-thumb", _autoSaveSwitch.Thumb);
        ToolTipService.SetToolTip(_autoSaveSwitch, "AutoSave a local recovery copy. This is not cloud storage.");
        _autoSaveSwitch.Checked += (_, _) => SetAutoSave(true);
        _autoSaveSwitch.Unchecked += (_, _) => SetAutoSave(false);
        left.Children.Add(TrackChrome("quick-save", Quick("Save", "\uE74E", () => Run(SaveNativeAsync))));
        _undoQuick = TrackChrome("quick-undo", Quick("Undo", "\uE7A7", () => { FlushEdits(); Session.Undo(); }));
        _redoQuick = TrackChrome("quick-redo", Quick("Redo", "\uE7A6", () => { FlushEdits(); Session.Redo(); }));
        left.Children.Add(_undoQuick); left.Children.Add(_redoQuick); bar.Children.Add(left);

        var title = new Grid { ColumnSpacing = 6, ColumnDefinitions = { new() { Width = new GridLength(1, GridUnitType.Star) }, new() { Width = GridLength.Auto }, new() { Width = GridLength.Auto } } };
        _documentName.MaxWidth = 330; _documentName.TextTrimming = TextTrimming.CharacterEllipsis;
        _documentName.HorizontalAlignment = HorizontalAlignment.Stretch;
        title.Children.Add(_documentName); var chevron = OfficePalette.Text("⌄", 11); Grid.SetColumn(chevron, 1); title.Children.Add(chevron);
        _saveState.Foreground = OfficePalette.Muted; Grid.SetColumn(_saveState, 2); title.Children.Add(_saveState);
        var titleButton = TrackChrome("document-title", new Button { Content = title, Background = OfficePalette.Brush("00FFFFFF"),
            BorderThickness = new(0), MinWidth = 0, MinHeight = 0, Height = 32, Padding = new(4),
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center });
        AutomationProperties.SetName(titleButton, "Rename presentation");
        titleButton.Click += (_, _) => Run(RenameAsync); Grid.SetColumn(titleButton, 1); bar.Children.Add(titleButton);
        _search.VerticalAlignment = VerticalAlignment.Center; _search.Width = 244;
        Grid.SetColumn(_search, 2); bar.Children.Add(TrackChrome("command-search", _search));
        AutomationProperties.SetName(_search, "Search commands");

        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        _commentsQuick = TrackChrome("quick-comments", SmallButton("Comments", () => ShowInspector(InspectorMode.Comments)));
        _presentQuick = TrackChrome("quick-present", SmallButton("▷  Present", () => StartShow(false)));
        AutomationProperties.SetName(_presentQuick, "Present from current slide");
        _presentQuick.Background = OfficePalette.White;
        _shareQuick = TrackChrome("quick-share", SmallButton("Share  ⌄", () => { }));
        _shareQuick.Foreground = OfficePalette.White; _shareQuick.Background = OfficePalette.Accent;
        _shareQuick.Flyout = MakeMenu([("PowerPoint file (.pptx)", () => Run(() => ExportAsync("pptx"))), ("PDF handout", () => Run(() => ExportAsync("pdf"))), ("Native editable file", () => Run(SaveNativeAsync))]);
        _moreQuick = TrackChrome("quick-more", Quick("More presentation actions", "\uE712", () => { }));
        _moreQuick.Flyout = MakeMenu([
            ("Search commands", FocusCommandSearch), ("Save presentation", () => Run(SaveNativeAsync)),
            ("Undo", () => { FlushEdits(); Session.Undo(); }), ("Redo", () => { FlushEdits(); Session.Redo(); }),
            ("Toggle AutoSave local recovery", () => _autoSaveSwitch.IsChecked = !_autoSave),
            ("Comments", () => ShowInspector(InspectorMode.Comments)), ("Slide thumbnails", ToggleFilmstrip),
            ("Present from beginning", () => StartShow(true)), ("Export PowerPoint", () => Run(() => ExportAsync("pptx"))),
            ("Export PDF", () => Run(() => ExportAsync("pdf"))) ]);
        right.Children.Add(_commentsQuick); right.Children.Add(_presentQuick); right.Children.Add(_shareQuick); right.Children.Add(_moreQuick);
        Grid.SetColumn(right, 3); bar.Children.Add(right);
        _searchFlyout.Content = _compactSearch;
        _searchFlyout.Opened += (_, _) =>
        {
            if (!_commandQueued && _compactSearch.XamlRoot is { } root &&
                !IsSearchDescendant(FocusManager.GetFocusedElement(root) as DependencyObject, _compactSearch))
                _compactSearch.Focus(FocusState.Programmatic);
        };
        AutomationProperties.SetName(_compactSearch, "Search commands");
        return bar;
    }

    private void SetAutoSave(bool enabled)
    {
        _autoSave = enabled;
        _recoveryTimer.Stop();
        if (enabled && Session.IsDirty) _recoveryTimer.Start();
        _status.Message = enabled ? "AutoSave local recovery on" : "AutoSave off — save a file to keep changes";
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool? _filmstripOverride;
    private void ToggleFilmstrip() { _filmstripOverride = !(_filmstripOverride ?? ActualWidth >= 640); ApplyEditorLayout(); }
    private void ApplyEditorLayout()
    {
        double width = ActualWidth, height = ActualHeight;
        if (width <= 0) return;
        void Visible(UIElement? element, bool visible) { if (element is not null) element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed; }
        Visible(_search, width >= 1180); Visible(_autoSaveLabel, width >= 520);
        Visible(_undoQuick, width >= 720); Visible(_redoQuick, width >= 720);
        Visible(_commentsQuick, width >= 1000); Visible(_shareQuick, width >= 1000); Visible(_moreQuick, width < 1180);
        Visible(_saveState, width >= 840);
        if (_presentQuick is not null) { _presentQuick.Content = width < 720 ? "▷" : "▷  Present"; _presentQuick.Padding = new(width < 720 ? 7 : 11, 4, width < 720 ? 7 : 11, 4); }
        bool film = _filmstripOverride ?? width >= 640;
        _filmColumn.Width = new GridLength(!film ? 0 : width < 820 ? 140 : Math.Clamp(_filmColumn.Width.Value, 180, 360));
        Visible(_filmstrip, film); Visible(_leftSplitter, film);
        _workspace.ColumnDefinitions[1].Width = new GridLength(film ? 5 : 0);
        bool docked = width >= 1100 && _inspectorOpen;
        _formatColumn.Width = new GridLength(docked ? 296 : 0);
        _workspace.ColumnDefinitions[3].Width = new GridLength(docked ? 5 : 0);
        Visible(_rightSplitter, docked); Visible(_format, _inspectorOpen);
        Grid.SetColumn(_format, docked ? 4 : 0); Grid.SetColumnSpan(_format, docked ? 1 : 5);
        _format.Width = docked ? double.NaN : Math.Min(320, Math.Max(0, width - 8));
        _format.HorizontalAlignment = docked ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
        _notesRow.Height = new GridLength(_notesVisible && height >= 520 ? 86 : 0);
        Visible(_notes, _notesVisible && height >= 520);
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Read-only arranged UI geometry for host diagnostics and cross-platform clipping tests.</summary>
    public string GetChromeDiagnostics()
    {
        var values = new List<string>();
        foreach (var (name, element) in _chrome)
        {
            var origin = element.TransformToVisual(this).TransformPoint(new Point(0, 0));
            values.Add(FormattableString.Invariant($"\"{name}\":{{\"x\":{origin.X},\"y\":{origin.Y},\"width\":{element.ActualWidth},\"height\":{element.ActualHeight},\"visible\":{(element.Visibility == Visibility.Visible ? "true" : "false")}}}"));
        }
        return "{" + string.Join(",", values) + "}";
    }
    public bool AutoSaveEnabled => _autoSave;
}
