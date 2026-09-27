using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using PresentationSpace.Core;
using PresentationSpace.Rendering.Skia;
using PresentationSpace.Ribbon.Uno;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace PresentationSpace.Controls.Uno;

/// <summary>Viewport-bounded slide tiles with recycling, keyboard navigation and drag/drop reordering.</summary>
public class VirtualSlideView : SessionControl
{
    private sealed class Tile(SlidePreview preview, Button button, Border frame, TextBlock label)
    {
        public SlidePreview Preview { get; } = preview;
        public Button Button { get; } = button;
        public Border Frame { get; } = frame;
        public TextBlock Label { get; } = label;
        public int Index;
        public Guid Id;
    }
    private readonly ScrollViewer _scroll = new() { HorizontalScrollMode = ScrollMode.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly Canvas _items = new();
    private readonly Dictionary<int, Tile> _realized = [];
    private readonly Stack<Tile> _pool = new();
    private readonly SlideRenderer _renderer = new() { EnableSceneCache = false, PictureCacheBudget = 8 * 1024 * 1024 };
    private VirtualSlideLayout _layout;
    private int _selected = -1;
    private bool _refreshing;
    public bool IsGrid { get; }
    public int RealizedCount => _realized.Count;
    public int AllocatedTileCount => _realized.Count + _pool.Count;
    public event EventHandler? SlideInvoked;
    public event EventHandler? RealizationChanged;
    public VirtualSlideView(bool grid = false)
    {
        IsGrid = grid; IsTabStop = true;
        Background = OfficePalette.Brush(grid ? "E9E9E9" : "F6F6F6");
        _scroll.Content = _items; Content = _scroll;
        AutomationProperties.SetName(this, grid ? "Slide sorter" : "Slide thumbnails");
        AutomationProperties.SetAutomationId(this, grid ? "slide-sorter" : "slide-filmstrip");
        _scroll.ViewChanged += (_, _) => RefreshTiles(false);
        SizeChanged += (_, _) => RefreshTiles(false);
        RegisterPropertyChangedCallback(VisibilityProperty, (_, _) => RefreshTiles(true));
        Loaded += (_, _) => RefreshTiles(true);
        Unloaded += (_, _) => { foreach (var tile in _realized.Values) tile.Preview.Clear(); _renderer.Dispose(); };
        KeyDown += Navigate;
    }
    protected override void OnSessionChanged(bool preview)
    {
        bool selectionChanged = Session?.SlideIndex != _selected;
        RefreshTiles(selectionChanged, preview);
    }
    private void RefreshTiles(bool reveal, bool preview = false)
    {
        if (_refreshing || Session is not { } session || ActualWidth <= 0 || ActualHeight <= 0 || Visibility != Visibility.Visible) return;
        _refreshing = true;
        try
        {
            _selected = session.SlideIndex;
            _layout = VirtualSlideLayout.Create(session.Document.Slides.Length, Math.Max(96, ActualWidth - 14), session.Document.Height / session.Document.Width, IsGrid);
            _items.Width = Math.Max(96, ActualWidth - 14); _items.Height = _layout.ExtentHeight;
            double offset = Math.Clamp(_scroll.VerticalOffset, 0, Math.Max(0, _layout.ExtentHeight - ActualHeight));
            if (reveal) offset = _layout.RevealOffset(_selected, offset, ActualHeight);
            if (Math.Abs(offset - _scroll.VerticalOffset) > .5) _scroll.ChangeView(null, offset, null, true);
            var (start, end) = _layout.VisibleRange(offset, ActualHeight);
            foreach (int index in _realized.Keys.Where(i => i < start || i >= end).ToArray())
            {
                var tile = _realized[index]; _items.Children.Remove(tile.Button); tile.Preview.Clear(); _pool.Push(tile); _realized.Remove(index);
            }
            for (int index = start; index < end; index++)
            {
                bool created = !_realized.TryGetValue(index, out var tile);
                if (created)
                {
                    tile = _pool.Count > 0 ? _pool.Pop() : CreateTile(); _realized[index] = tile; _items.Children.Add(tile.Button);
                }
                var slide = session.Document.Slides[index];
                bool rebound = tile!.Id != slide.Id;
                tile.Index = index; tile.Id = slide.Id;
                tile.Button.Width = _layout.ItemWidth; tile.Button.Height = _layout.ItemHeight;
                tile.Preview.Width = _layout.ItemWidth - 28; tile.Preview.Height = _layout.ItemHeight - 36;
                tile.Label.MaxWidth = _layout.ItemWidth - 20;
                string label = $"{index + 1}    {slide.Name}";
                if (tile.Label.Text != label) { tile.Label.Text = label; AutomationProperties.SetName(tile.Button, $"Slide {index + 1}: {slide.Name}"); }
                tile.Frame.BorderBrush = index == _selected ? OfficePalette.Accent : OfficePalette.Line;
                tile.Button.Opacity = slide.Hidden ? .45 : 1;
                Canvas.SetLeft(tile.Button, _layout.Left(index)); Canvas.SetTop(tile.Button, _layout.Top(index));
                // Committed thumbnails only while an object is dragged; selection borders remain immediate.
                if (!preview || created || rebound) tile.Preview.SetSlide(session.Document, slide);
            }
            // A resize must not retain an unbounded historical pool.
            while (_pool.Count > Math.Max(2, _realized.Count)) _pool.Pop();
        }
        finally { _refreshing = false; }
        RealizationChanged?.Invoke(this, EventArgs.Empty);
    }
    private Tile CreateTile()
    {
        var preview = new SlidePreview(_renderer);
        var frame = new Border { Child = preview, BorderThickness = new(2), BorderBrush = OfficePalette.Line, Background = OfficePalette.White };
        var label = OfficePalette.Text("", 10); label.TextTrimming = TextTrimming.CharacterEllipsis;
        var content = new StackPanel { Spacing = 4 }; content.Children.Add(frame); content.Children.Add(label);
        var button = new Button { Content = content, Padding = new(8, 2, 8, 2), BorderThickness = new(0), Background = OfficePalette.Brush("00FFFFFF"), AllowDrop = true, CanDrag = true, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        var tile = new Tile(preview, button, frame, label);
        button.Click += (_, _) => { Session?.SelectSlide(tile.Index); if (!IsGrid) SlideInvoked?.Invoke(this, EventArgs.Empty); };
        button.DoubleTapped += (_, e) => { Session?.SelectSlide(tile.Index); SlideInvoked?.Invoke(this, EventArgs.Empty); e.Handled = true; };
        button.DragStarting += (_, e) => { e.Data.SetText("presentationspace-slide:" + tile.Id); e.Data.RequestedOperation = DataPackageOperation.Move; };
        button.DragOver += (_, e) => { if (e.DataView.Contains(StandardDataFormats.Text)) e.AcceptedOperation = DataPackageOperation.Move; };
        button.Drop += async (_, e) =>
        {
            var deferral = e.GetDeferral();
            try
            {
                const string prefix = "presentationspace-slide:";
                var text = await e.DataView.GetTextAsync();
                if (Session is { } session && text.StartsWith(prefix, StringComparison.Ordinal) && Guid.TryParse(text[prefix.Length..], out var id))
                {
                    int from = session.Document.Slides.FindIndex(s => s.Id == id), to = session.Document.Slides.FindIndex(s => s.Id == tile.Id);
                    if (from >= 0 && to >= 0) session.MoveSlide(from, to);
                }
            }
            catch (Exception error) when (error is InvalidOperationException or ArgumentException) { }
            finally { deferral.Complete(); }
        };
        var menu = new MenuFlyout();
        void Add(string title, Action<EditorSession> action)
        {
            var item = new MenuFlyoutItem { Text = title };
            item.Click += (_, _) => { if (Session is { } session) { int i = session.Document.Slides.FindIndex(s => s.Id == tile.Id); if (i >= 0) { session.SelectSlide(i); action(session); } } };
            menu.Items.Add(item);
        }
        Add("New slide", s => s.AddSlide()); Add("Duplicate slide", s => s.DuplicateSlide()); Add("Delete slide", s => s.DeleteSlide());
        Add("Move up", s => s.MoveSlide(s.SlideIndex, s.SlideIndex - 1)); Add("Move down", s => s.MoveSlide(s.SlideIndex, s.SlideIndex + 1));
        Add("Hide / show slide", s => s.EditSlide("Toggle hidden slide", x => x with { Hidden = !x.Hidden })); button.ContextFlyout = menu;
        return tile;
    }
    private void Navigate(object sender, KeyRoutedEventArgs e)
    {
        if (e.Handled || Session is not { } session) return;
        int index = session.SlideIndex;
        switch (e.Key)
        {
            case VirtualKey.Up: index -= Math.Max(1, _layout.Columns); break;
            case VirtualKey.Down: index += Math.Max(1, _layout.Columns); break;
            case VirtualKey.Left: index--; break;
            case VirtualKey.Right: index++; break;
            case VirtualKey.Home: index = 0; break;
            case VirtualKey.End: index = session.Document.Slides.Length - 1; break;
            case VirtualKey.PageUp: index -= Math.Max(1, (int)(ActualHeight / Math.Max(1, _layout.Pitch))) * Math.Max(1, _layout.Columns); break;
            case VirtualKey.PageDown: index += Math.Max(1, (int)(ActualHeight / Math.Max(1, _layout.Pitch))) * Math.Max(1, _layout.Columns); break;
            case VirtualKey.Delete: session.DeleteSlide(); e.Handled = true; return;
            case VirtualKey.Enter: SlideInvoked?.Invoke(this, EventArgs.Empty); e.Handled = true; return;
            default: return;
        }
        session.SelectSlide(Math.Clamp(index, 0, session.Document.Slides.Length - 1));
        if (_realized.TryGetValue(session.SlideIndex, out var tile)) tile.Button.Focus(FocusState.Keyboard);
        e.Handled = true;
    }
}

internal static class ImmutableArraySearch
{
    public static int FindIndex<T>(this System.Collections.Immutable.ImmutableArray<T> source, Func<T, bool> predicate)
    { for (int i = 0; i < source.Length; i++) if (predicate(source[i])) return i; return -1; }
}
