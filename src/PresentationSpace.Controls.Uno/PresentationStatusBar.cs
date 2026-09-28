using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using PresentationSpace.Core;
using PresentationSpace.Ribbon.Uno;

namespace PresentationSpace.Controls.Uno;

public sealed class PresentationStatusBar : SessionControl
{
    private readonly TextBlock _position = OfficePalette.Text("", 10), _message = OfficePalette.Text("Ready", 10), _zoomText = OfficePalette.Text("100%", 10);
    private readonly Slider _zoom = new() { Minimum = 10, Maximum = 400, Value = 100, Width = 108, Height = 28, MinHeight = 0, StepFrequency = 5, VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center };
    private readonly Button _notesButton;
    private bool _updating;
    public event EventHandler<float>? ZoomRequested;
    public event EventHandler? FitRequested, NotesRequested, NormalRequested, SorterRequested, ShowRequested;
    public string Message { get => _message.Text; set => _message.Text = value; }
    public PresentationStatusBar()
    {
        var root = new Grid { Height = 31, Background = OfficePalette.Brush("FAFAFA"), ColumnDefinitions = { new() { Width = GridLength.Auto }, new() { Width = new GridLength(1, GridUnitType.Star) }, new() { Width = GridLength.Auto } } };
        _position.Margin = new(8, 0, 6, 0); _position.TextTrimming = TextTrimming.CharacterEllipsis;
        root.Children.Add(_position); _message.Margin = new(4, 0, 4, 0); _message.Foreground = OfficePalette.Muted;
        _message.TextTrimming = TextTrimming.CharacterEllipsis; Grid.SetColumn(_message, 1); root.Children.Add(_message);
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new(0, 0, 6, 0), VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center };
        Button Add(string text, string name, Action action)
        {
            var button = new Button { Content = text, FontSize = 11, Padding = new(5, 2, 5, 2), MinWidth = 0, MinHeight = 0, Height = 28,
                VerticalContentAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center, Background = OfficePalette.Brush("00FFFFFF"), BorderThickness = new(0) };
            AutomationProperties.SetName(button, name); ToolTipService.SetToolTip(button, name); button.Click += (_, _) => action(); tools.Children.Add(button); return button;
        }
        _notesButton = Add("Notes", "Show or hide speaker notes", () => NotesRequested?.Invoke(this, EventArgs.Empty));
        Add("▣", "Normal view", () => NormalRequested?.Invoke(this, EventArgs.Empty));
        Add("▦", "Slide sorter", () => SorterRequested?.Invoke(this, EventArgs.Empty));
        Add("▷", "Start slide show", () => ShowRequested?.Invoke(this, EventArgs.Empty));
        Add("−", "Zoom out", () => ZoomRequested?.Invoke(this, (float)Math.Max(.1, _zoom.Value / 100 - .1)));
        tools.Children.Add(_zoom); AutomationProperties.SetName(_zoom, "Slide zoom percent");
        Add("+", "Zoom in", () => ZoomRequested?.Invoke(this, (float)Math.Min(4, _zoom.Value / 100 + .1)));
        _zoomText.Width = 35; tools.Children.Add(_zoomText);
        Add("⊡", "Fit slide to window", () => FitRequested?.Invoke(this, EventArgs.Empty));
        Grid.SetColumn(tools, 2); root.Children.Add(tools); Content = new Border { Child = root, BorderBrush = OfficePalette.Line, BorderThickness = new(0, 1, 0, 0) };
        _zoom.ValueChanged += (_, e) => { if (!_updating) ZoomRequested?.Invoke(this, (float)e.NewValue / 100); };
        SizeChanged += (_, _) =>
        {
            _zoom.Visibility = ActualWidth >= 700 ? Visibility.Visible : Visibility.Collapsed;
            _message.Visibility = ActualWidth >= 1000 ? Visibility.Visible : Visibility.Collapsed;
            _position.MaxWidth = ActualWidth < 360 ? 72 : ActualWidth < 520 ? 100 : 240;
            _notesButton.Content = ActualWidth < 520 ? "▤" : "Notes";
        };
    }
    public void SetZoom(float zoom)
    {
        double value = Math.Clamp(zoom * 100, 10, 400);
        string label = $"{zoom * 100:0}%";
        if (_zoom.Value == value && _zoomText.Text == label) return;
        _updating = true; _zoom.Value = value; _zoomText.Text = label; _updating = false;
    }
    protected override void OnSessionChanged(bool preview)
    {
        if (Session is { } session)
            _position.Text = $"Slide {session.SlideIndex + 1} of {session.Document.Slides.Length}" + (session.Selection.Count > 0 ? $"   ·   {session.Selection.Count} selected" : "");
    }
}
