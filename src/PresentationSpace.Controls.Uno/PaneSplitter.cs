using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PresentationSpace.Ribbon.Uno;
namespace PresentationSpace.Controls.Uno;

/// <summary>Captured-pointer column resizing. Resized reports user changes, not adaptive layout changes.</summary>
public sealed class PaneSplitter : Border
{
    private bool _dragging;
    private uint _pointerId;
    private double _start, _width;
    public ColumnDefinition? TargetColumn { get; set; }
    public bool Reverse { get; set; }
    public double Minimum { get; set; } = 140;
    public double Maximum { get; set; } = 480;
    public event EventHandler<double>? Resized;

    public PaneSplitter()
    {
        Width = 5; Background = OfficePalette.Brush("00FFFFFF");
        PointerEntered += (_, _) => Background = OfficePalette.Brush("D8D8D8");
        PointerExited += (_, _) => { if (!_dragging) ClearHighlight(); };
        PointerPressed += (_, e) =>
        {
            var point = e.GetCurrentPoint(null);
            if (_dragging || TargetColumn is null || !point.Properties.IsLeftButtonPressed || !CapturePointer(e.Pointer)) return;
            _dragging = true; _pointerId = e.Pointer.PointerId;
            _start = point.Position.X; _width = TargetColumn.ActualWidth;
            e.Handled = true;
        };
        PointerMoved += (_, e) =>
        {
            if (!_dragging || e.Pointer.PointerId != _pointerId || TargetColumn is null) return;
            double dx = e.GetCurrentPoint(null).Position.X - _start;
            double width = Math.Clamp(_width + (Reverse ? -dx : dx), Minimum, Maximum);
            if (Math.Abs(TargetColumn.Width.Value - width) > .01)
            {
                TargetColumn.Width = new GridLength(width);
                Resized?.Invoke(this, width);
            }
            e.Handled = true;
        };
        PointerReleased += (_, e) =>
        {
            if (!_dragging || e.Pointer.PointerId != _pointerId) return;
            _dragging = false; ReleasePointerCapture(e.Pointer); ClearHighlight(); e.Handled = true;
        };
        PointerCanceled += (_, _) => { _dragging = false; ClearHighlight(); };
        PointerCaptureLost += (_, _) => { _dragging = false; ClearHighlight(); };
    }
    private void ClearHighlight() => Background = OfficePalette.Brush("00FFFFFF");
}
