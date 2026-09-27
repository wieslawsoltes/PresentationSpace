using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace PresentationSpace.Ribbon.Uno;

/// <summary>Horizontal ribbon scrolling with discoverable buttons only when the content overflows.</summary>
public sealed class RibbonScroller : UserControl
{
    private readonly ScrollViewer _scroll;
    private readonly Button _previous, _next;
    private readonly FrameworkElement _items;
    private bool _queued;
    public event EventHandler? ViewChanged;
    public double Offset => _scroll.HorizontalOffset;
    public bool HasOverflow => _previous.Visibility == Visibility.Visible;
    public RibbonScroller(FrameworkElement items, string name)
    {
        _items = items;
        _scroll = new() { Content = items, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Enabled,
            VerticalScrollMode = ScrollMode.Disabled, IsTabStop = false };
        var root = new Grid { ColumnDefinitions = { new() { Width = GridLength.Auto }, new() { Width = new GridLength(1, GridUnitType.Star) }, new() { Width = GridLength.Auto } } };
        _previous = Arrow("\uE76B", "Scroll " + name + " left", -1);
        _next = Arrow("\uE76C", "Scroll " + name + " right", 1);
        root.Children.Add(_previous); Grid.SetColumn(_scroll, 1); root.Children.Add(_scroll);
        Grid.SetColumn(_next, 2); root.Children.Add(_next); Content = root;
        _scroll.ViewChanged += (_, _) => QueueUpdate();
        SizeChanged += (_, _) => QueueUpdate(); items.SizeChanged += (_, _) => QueueUpdate();
        Loaded += (_, _) => QueueUpdate();
    }
    private Button Arrow(string glyph, string name, int direction)
    {
        var button = new Button { Content = new FontIcon { Glyph = glyph, FontSize = 11 }, Width = 26,
            MinWidth = 0, MinHeight = 0, Padding = new(0), BorderThickness = new(0),
            Background = OfficePalette.Brush("FAFAFA"), Visibility = Visibility.Collapsed,
            VerticalAlignment = VerticalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(button, name); ToolTipService.SetToolTip(button, name);
        button.Click += (_, _) => _scroll.ChangeView(Math.Clamp(_scroll.HorizontalOffset + direction * Math.Max(120, _scroll.ViewportWidth * .75), 0, _scroll.ScrollableWidth), null, null, true);
        return button;
    }
    private void QueueUpdate()
    {
        if (_queued) return; _queued = true;
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            _queued = false;
            // Compare with the entire host, not the smaller viewport after showing the arrows.
            // This avoids self-sustaining overflow and visibility oscillation near a breakpoint.
            bool overflow = ActualWidth > 0 && _items.ActualWidth > ActualWidth + 1;
            _previous.Visibility = _next.Visibility = overflow ? Visibility.Visible : Visibility.Collapsed;
            _previous.IsEnabled = _scroll.HorizontalOffset > 1;
            _next.IsEnabled = _scroll.HorizontalOffset < _scroll.ScrollableWidth - 1;
            ViewChanged?.Invoke(this, EventArgs.Empty);
        })) _queued = false;
    }
    public void Reveal(FrameworkElement element)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!IsLoaded || element.ActualWidth <= 0) return;
            var point = element.TransformToVisual(_scroll).TransformPoint(new Point(0, 0));
            double delta = point.X < 0 ? point.X : point.X + element.ActualWidth > _scroll.ViewportWidth ? point.X + element.ActualWidth - _scroll.ViewportWidth : 0;
            if (Math.Abs(delta) > 1) _scroll.ChangeView(Math.Clamp(_scroll.HorizontalOffset + delta, 0, _scroll.ScrollableWidth), null, null, true);
        });
    }
}
