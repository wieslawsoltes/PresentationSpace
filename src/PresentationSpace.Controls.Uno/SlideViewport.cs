using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PresentationSpace.Core;
using PresentationSpace.Rendering.Skia;
using SkiaSharp;
using Windows.System;
using Windows.UI.Core;
using CPoint = PresentationSpace.Core.PointF;

namespace PresentationSpace.Controls.Uno;

public sealed partial class SlideViewport : UserControl
{
    private readonly PresentationCanvas _surface = new();
    private readonly Canvas _overlay = new();
    private readonly SlideRenderer _renderer = new();
    private EditorSession? _session;
    private CPoint _down, _panStart, _panOrigin;
    private RectF? _marquee;
    private int _handle = -1;
    private bool _dragging, _panning, _additive, _subscribed;
    private float _scale = 1, _ox, _oy, _panX, _panY;
    private TextBox? _editor;
    private Guid? _editingId, _editingSlideId;
    public float Zoom { get; private set; } = 1;
    public bool FitToWindow { get; private set; } = true;
    public bool ShowGrid { get; set; }
    public bool ShowGuides { get; set; }
    public event EventHandler? ZoomChanged;
    public event EventHandler? SelectionContextRequested;
    public EditorSession? Session
    {
        get => _session;
        set { CommitText(); Detach(); _session = value; Attach(); Refresh(); }
    }

    public SlideViewport()
    {
        IsTabStop = true;
        MinHeight = 100;
        var root = new Grid { Background = Ribbon.Uno.OfficePalette.Canvas };
        root.Children.Add(_surface);
        root.Children.Add(_overlay);
        Content = root;
        AutomationProperties.SetName(this, "Slide editing canvas");
        AutomationProperties.SetAutomationId(this, "slide-canvas");
        _surface.Draw += Paint;
        _surface.PointerPressed += Pressed;
        _surface.PointerMoved += Moved;
        _surface.PointerReleased += Released;
        _surface.PointerCanceled += Canceled;
        _surface.PointerCaptureLost += CaptureLost;
        _surface.DoubleTapped += OnDoubleTapped;
        _surface.PointerWheelChanged += Wheel;
        _surface.RightTapped += (_, e) => { SelectionContextRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; };
        GotFocus += RestoreNativeCanvasFocus;
        PreviewKeyDown += HandleTablePreviewKey;
        KeyDown += OnKeyDown;
        SizeChanged += (_, _) => { CommitText(); Refresh(); };
        Loaded += (_, _) => { Attach(); Refresh(); };
        Unloaded += (_, _) => { CommitText(); Detach(); _renderer.Dispose(); };
    }

    private void Attach() { if (!_subscribed && _session is not null) { _session.Changed += OnChanged; _subscribed = true; } }
    private void Detach() { if (_subscribed && _session is not null) _session.Changed -= OnChanged; _subscribed = false; }
    public long DrawCount => _surface.DrawCount;
    public double LastDrawMilliseconds => _surface.LastDrawMilliseconds;
    public RenderCacheStatistics RenderStatistics => _renderer.CacheStatistics;
    public void Refresh() => _surface.Invalidate();
    private void OnChanged(object? sender, EditorChangedEventArgs e)
    {
        if (_textSelection is { } selection && (Session?.CurrentSlide.Id != selection.SlideId || Session?.PrimaryShape?.Id != selection.ShapeId)) _textSelection = null;
        SynchronizeCellSelection();
        var shape = Session?.PrimaryShape;
        AutomationProperties.SetName(this, shape is null ? "Slide editing canvas" : $"Selected {shape.Kind}: {shape.Name}. {shape.AlternativeText}");
        Refresh();
    }
    public void SetZoom(float zoom)
    {
        CommitText();
        Zoom = Math.Clamp(zoom, .1f, 4);
        FitToWindow = false;
        Refresh();
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }
    public void Fit()
    {
        CommitText();
        FitToWindow = true;
        _panX = _panY = 0;
        Refresh();
    }

    private void Paint(object? sender, PresentationDrawEventArgs e)
    {
        var canvas = e.Canvas;
        using (var background = new SKPaint { Color = SlideRenderer.Color("#E9E9E9") })
            canvas.DrawRect(0, 0, (float)e.Size.Width, (float)e.Size.Height, background);
        if (Session is not { } session) return;
        var document = session.Document;
        float availableWidth = Math.Max(1, (float)ActualWidth - 88);
        float availableHeight = Math.Max(1, (float)ActualHeight - 66);
        float previousZoom = Zoom;
        _scale = Math.Max(.02f, FitToWindow ? Math.Min(availableWidth / document.Width, availableHeight / document.Height) : Zoom);
        Zoom = _scale;
        _ox = ((float)ActualWidth - document.Width * _scale) / 2 + _panX;
        _oy = ((float)ActualHeight - document.Height * _scale) / 2 + _panY;
        canvas.Save();
        try
        {
            using (var shadow = new SKPaint { Color = SlideRenderer.Color("#25000000"), IsAntialias = true })
                canvas.DrawRect(_ox + 3, _oy + 4, document.Width * _scale, document.Height * _scale, shadow);
            canvas.Translate(_ox, _oy);
            canvas.Scale(_scale);
            _renderer.Render(canvas, document, session.CurrentSlide);
            if (ShowGrid)
            {
                using var grid = new SKPaint { Color = SlideRenderer.Color("#30939AA5"), StrokeWidth = 1 / _scale };
                float step = Math.Max(8, session.GridSize);
                // Avoid millions of invisible grid points on very large slides at low zoom.
                while (step * _scale < 6) step *= 2;
                for (float x = 0; x <= document.Width; x += step)
                    for (float y = 0; y <= document.Height; y += step) canvas.DrawPoint(x, y, grid);
            }
            if (ShowGuides)
            {
                using var dash = SKPathEffect.CreateDash([6 / _scale, 5 / _scale], 0);
                using var guide = new SKPaint { Color = SlideRenderer.Color("#D35230"), StrokeWidth = 1 / _scale, PathEffect = dash };
                canvas.DrawLine(document.Width / 2, 0, document.Width / 2, document.Height, guide);
                canvas.DrawLine(0, document.Height / 2, document.Width, document.Height / 2, guide);
            }
            _renderer.DrawSelection(canvas, session.SelectedShapes, _scale);
            DrawTableSelection(canvas);
            if (_marquee is { } marquee)
            {
                using var fill = new SKPaint { Color = SlideRenderer.Color("#20D35230") };
                using var border = new SKPaint { Color = SlideRenderer.Color("#D35230"), StrokeWidth = 1 / _scale, Style = SKPaintStyle.Stroke };
                canvas.DrawRect(marquee.X, marquee.Y, marquee.Width, marquee.Height, fill);
                canvas.DrawRect(marquee.X, marquee.Y, marquee.Width, marquee.Height, border);
            }
        }
        finally { canvas.Restore(); }
        if (Math.Abs(previousZoom - Zoom) > .0001f)
            DispatcherQueue.TryEnqueue(() => ZoomChanged?.Invoke(this, EventArgs.Empty));
    }

    private CPoint Position(PointerRoutedEventArgs e)
    {
        var position = e.GetCurrentPoint(_surface).Position;
        return new(((float)position.X - _ox) / _scale, ((float)position.Y - _oy) / _scale);
    }
    private static bool Key(VirtualKey key) => (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key) & CoreVirtualKeyStates.Down) != 0;
    private void Pressed(object sender, PointerRoutedEventArgs e)
    {
        if (Session is not { } session) return;
        var pointer = e.GetCurrentPoint(_surface);
        if (pointer.Properties.IsMiddleButtonPressed || (pointer.Properties.IsLeftButtonPressed && Key(VirtualKey.Space)))
        {
            CommitText();
            Focus(FocusState.Pointer);
            _panning = true;
            _panStart = new((float)pointer.Position.X, (float)pointer.Position.Y);
            _panOrigin = new(_panX, _panY);
            _surface.CapturePointer(e.Pointer);
            e.Handled = true;
            return;
        }
        if (!pointer.Properties.IsLeftButtonPressed) return;
        CommitText();
        _textSelection = null;
        Focus(FocusState.Pointer);
        _down = Position(e);
        _handle = -1;
        _additive = Key(VirtualKey.Shift) || Key(VirtualKey.Control);
        if (session.Selection.Count == 1 && session.PrimaryShape is { Locked: false } primary)
        {
            var local = Geometry.Rotate(_down, primary.Bounds.Center, -primary.Rotation);
            var handles = Geometry.Handles(primary.Bounds);
            for (int i = 0; i < handles.Length; i++)
                if (Math.Abs(local.X - handles[i].X) < 8 / _scale && Math.Abs(local.Y - handles[i].Y) < 8 / _scale) { _handle = i; break; }
            if (Math.Abs(local.X - primary.Bounds.Center.X) < 10 / _scale && Math.Abs(local.Y - (primary.Bounds.Y - 31 / _scale)) < 10 / _scale) _handle = 8;
        }
        if (_handle < 0 && ActiveTable is { } active)
        {
            var local = Geometry.Rotate(_down, active.Bounds.Center, -active.Rotation);
            var b = active.Bounds; float inset = 6 / _scale;
            if (local.X > b.X + inset && local.X < b.Right - inset && local.Y > b.Y + inset && local.Y < b.Bottom - inset && SelectTableAt(_down, _additive, false))
            { e.Handled = true; return; }
            _cellSelection = null;
        }
        if (_handle < 0)
        {
            var hit = session.CurrentSlide.Shapes.Reverse().FirstOrDefault(shape => Geometry.HitTest(shape, _down, 5 / _scale));
            if (hit is null) { if (!_additive) session.Select(null); _marquee = new(_down.X, _down.Y, 0, 0); }
            else if (!session.Selection.Contains(hit.Id) || _additive) session.Select(hit.Id, _additive);
        }
        _dragging = true;
        session.BeginGesture();
        _surface.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void Moved(object sender, PointerRoutedEventArgs e)
    {
        if (_panning)
        {
            var point = e.GetCurrentPoint(_surface).Position;
            _panX = _panOrigin.X + (float)point.X - _panStart.X;
            _panY = _panOrigin.Y + (float)point.Y - _panStart.Y;
            Refresh(); e.Handled = true; return;
        }
        if (!_dragging || Session is not { } session) return;
        var position = Position(e);
        var delta = new CPoint(position.X - _down.X, position.Y - _down.Y);
        if (_marquee is not null) { _marquee = RectF.Between(_down, position); Refresh(); return; }
        if (Math.Abs(delta.X) + Math.Abs(delta.Y) < .4f) return;
        bool shift = Key(VirtualKey.Shift), snap = session.SnapToGrid && !Key(VirtualKey.Menu);
        if (_handle == 8)
        {
            var bounds = session.PrimaryShape!.Bounds;
            float angle = MathF.Atan2(position.Y - bounds.Center.Y, position.X - bounds.Center.X) * 180 / MathF.PI + 90;
            if (shift) angle = Geometry.Snap(angle, 15);
            session.PreviewShapes(shape => shape with { Rotation = angle });
        }
        else if (_handle >= 0)
        {
            session.PreviewShapes(shape =>
            {
                var start = Geometry.Rotate(_down, shape.Bounds.Center, -shape.Rotation);
                var end = Geometry.Rotate(position, shape.Bounds.Center, -shape.Rotation);
                return shape with { Bounds = Geometry.ResizeRotated(shape.Bounds, shape.Rotation, _handle, new(end.X - start.X, end.Y - start.Y), shift) };
            });
        }
        else
        {
            if (shift) delta = Math.Abs(delta.X) > Math.Abs(delta.Y) ? delta with { Y = 0 } : delta with { X = 0 };
            if (snap) delta = new(Geometry.Snap(delta.X, session.GridSize), Geometry.Snap(delta.Y, session.GridSize));
            session.PreviewShapes(shape => shape with { Bounds = shape.Bounds with { X = shape.Bounds.X + delta.X, Y = shape.Bounds.Y + delta.Y } });
        }
        e.Handled = true;
    }

    private void Released(object sender, PointerRoutedEventArgs e)
    {
        if (_panning) { _panning = false; _surface.ReleasePointerCapture(e.Pointer); e.Handled = true; return; }
        if (!_dragging) return;
        _dragging = false;
        if (_marquee is { } marquee) { Session?.CancelGesture(); Session?.SelectRect(marquee, _additive); _marquee = null; }
        else Session?.CommitGesture();
        _surface.ReleasePointerCapture(e.Pointer);
        Refresh(); e.Handled = true;
    }
    private void Canceled(object sender, PointerRoutedEventArgs e)
    {
        _panning = _dragging = false;
        _marquee = null;
        Session?.CancelGesture();
        Refresh();
    }
    private void CaptureLost(object sender, PointerRoutedEventArgs e) { if (_dragging || _panning) Canceled(sender, e); }
    private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (Session is not { } session) return;
        var point = e.GetPosition(_surface);
        var hit = session.CurrentSlide.Shapes.Reverse().FirstOrDefault(shape => Geometry.HitTest(shape, new(((float)point.X - _ox) / _scale, ((float)point.Y - _oy) / _scale), 5 / _scale));
        if (hit is { Kind: ShapeKind.Table, Locked: false })
        {
            session.Select(hit.Id);
            SelectTableAt(new(((float)point.X - _ox) / _scale, ((float)point.Y - _oy) / _scale), false, true);
        }
        else if (hit is not null && hit.Kind is not (ShapeKind.Image or ShapeKind.Chart or ShapeKind.Table)) { session.Select(hit.Id); EditText(); }
        e.Handled = true;
    }

    public void EditText()
    {
        CommitText();
        if (Session?.PrimaryShape is { Kind: ShapeKind.Table }) { EditTableCell(); return; }
        if (Session?.PrimaryShape is not { Locked: false } shape || shape.Kind is ShapeKind.Image or ShapeKind.Chart or ShapeKind.Table) return;
        _textDraft = shape;
        _editingId = shape.Id;
        _editingSlideId = Session.CurrentSlide.Id;
        var bounds = shape.Bounds;
        // Enable multiline before assigning text; single-line coercion discards
        // everything after the first paragraph when a formatting command reopens input.
        _editor = new TextBox
        {
            AcceptsReturn = true, Text = shape.Text, TextWrapping = TextBoxModel.Resolve(shape).Wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            FontFamily = new FontFamily(shape.TextStyle.FontFamily), FontSize = Math.Max(8, shape.TextStyle.FontSize * _scale),
            Width = Math.Max(50, bounds.Width * _scale), Height = Math.Max(40, bounds.Height * _scale),
            Padding = new(TextBoxModel.Resolve(shape).MarginLeft * _scale, TextBoxModel.Resolve(shape).MarginTop * _scale, TextBoxModel.Resolve(shape).MarginRight * _scale, TextBoxModel.Resolve(shape).MarginBottom * _scale), BorderThickness = new(1), BorderBrush = Ribbon.Uno.OfficePalette.Accent,
            Background = Ribbon.Uno.OfficePalette.Brush("F7FFFFFF"), Foreground = Ribbon.Uno.OfficePalette.Brush(shape.TextStyle.Color),
            FontWeight = shape.TextStyle.Bold ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal,
            FontStyle = shape.TextStyle.Italic ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal,
            TextAlignment = shape.TextStyle.Alignment switch { ParagraphAlignment.Center => TextAlignment.Center, ParagraphAlignment.Right => TextAlignment.Right, _ => TextAlignment.Left },
            RenderTransform = new RotateTransform { Angle = shape.Rotation, CenterX = bounds.Width * _scale / 2, CenterY = bounds.Height * _scale / 2 }
        };
        Canvas.SetLeft(_editor, _ox + bounds.X * _scale);
        Canvas.SetTop(_editor, _oy + bounds.Y * _scale);
        _overlay.Children.Add(_editor);
        _editor.KeyDown += (sender, e) =>
        {
            if (!ReferenceEquals(sender, _editor)) return;
            if (e.Key == VirtualKey.Escape) { CancelText(); e.Handled = true; }
            else if (e.Key == VirtualKey.Enter && Key(VirtualKey.Control))
            { CommitText(); Focus(FocusState.Programmatic); e.Handled = true; }
        };
        _editor.TextChanged += (sender, _) => { if (ReferenceEquals(sender, _editor)) UpdateTextDraft(); };
        _editor.KeyDown += HandleFormattingKey;
        _editor.SelectionChanged += (sender, _) => { if (ReferenceEquals(sender, _editor)) CaptureTextSelection(); };
        // A removed native editor must not commit or detach its successor.
        _editor.LostFocus += (sender, _) => { if (ReferenceEquals(sender, _editor)) CommitText(); };
        _editor.Focus(FocusState.Programmatic);
        _editor.SelectAll();
    }

    public void CommitText()
    {
        CommitCellText();
        if (_editor is not { } editor || Session is not { } session) return;
        var id = _editingId;
        var slideId = _editingSlideId;
        CaptureTextSelection();
        string text = editor.Text;
        var draft = _textDraft;
        _textDraft = null;
        _editor = null; _editingId = _editingSlideId = null;
        _overlay.Children.Remove(editor);
        int index = session.Document.Slides.FindIndex(slide => slide.Id == slideId);
        if (index < 0) return;
        var slide = session.Document.Slides[index];
        var shape = slide.Shapes.FirstOrDefault(item => item.Id == id);
        if (shape is not null && shape.Text != text)
            session.EditDocument("Edit text", document => document with { Slides = document.Slides.SetItem(index, slide with { Shapes = slide.Shapes.SetItem(slide.Shapes.IndexOf(shape), shape with { Text = text, TextRanges = draft?.TextRanges ?? shape.TextRanges }) }) });
    }
    private void CancelText()
    {
        var editor = _editor;
        _textSelection = null;
        _textDraft = null;
        _editor = null; _editingId = _editingSlideId = null;
        if (editor is not null) _overlay.Children.Remove(editor);
        Focus(FocusState.Programmatic);
    }
    private void Wheel(object sender, PointerRoutedEventArgs e)
    {
        int delta = e.GetCurrentPoint(_surface).Properties.MouseWheelDelta;
        if (Key(VirtualKey.Control)) SetZoom(Zoom * (delta > 0 ? 1.1f : 1 / 1.1f));
        else
        {
            CommitText();
            if (Key(VirtualKey.Shift)) _panX += delta / 3f; else _panY += delta / 3f;
            Refresh();
        }
        e.Handled = true;
    }
    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Handled || HandleTableKey(e)) return;
        HandleFormattingKey(sender, e);
        if (e.Handled || _editor is not null || Session is not { } session) return;
        bool ctrl = Key(VirtualKey.Control), shift = Key(VirtualKey.Shift);
        float step = shift ? 10 : 1;
        if (ctrl)
        {
            switch (e.Key)
            {
                case VirtualKey.A: session.SelectAll(); break;
                case VirtualKey.C: session.Copy(); break;
                case VirtualKey.X: session.Cut(); break;
                case VirtualKey.V: session.Paste(); break;
                case VirtualKey.D: session.DuplicateSelection(); break;
                case VirtualKey.Z: if (shift) session.Redo(); else session.Undo(); break;
                case VirtualKey.Y: session.Redo(); break;
                case VirtualKey.G: if (shift) session.Ungroup(); else session.Group(); break;
                default: return;
            }
        }
        else
        {
            switch (e.Key)
            {
                case VirtualKey.Delete: case VirtualKey.Back: session.DeleteSelection(); break;
                case VirtualKey.Escape: session.CancelGesture(); session.Select(null); break;
                case VirtualKey.Left: session.Nudge(-step, 0); break;
                case VirtualKey.Right: session.Nudge(step, 0); break;
                case VirtualKey.Up: session.Nudge(0, -step); break;
                case VirtualKey.Down: session.Nudge(0, step); break;
                case VirtualKey.F2: case VirtualKey.Enter: EditText(); break;
                default: return;
            }
        }
        e.Handled = true;
    }
}
