using System.Diagnostics;
using Microsoft.UI.Xaml;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.Foundation;

namespace PresentationSpace.Controls.Uno;

public sealed class PresentationDrawEventArgs(SKCanvas canvas, Size size) : EventArgs
{
    public SKCanvas Canvas { get; } = canvas;
    public Size Size { get; } = size;
}

/// <summary>Direct Skia compositor drawing in logical coordinates, with caller transform/clip preservation.</summary>
public sealed class PresentationCanvas : SKCanvasElement
{
    public event EventHandler<PresentationDrawEventArgs>? Draw;
    public long DrawCount { get; private set; }
    public double LastDrawMilliseconds { get; private set; }
    protected override void RenderOverride(SKCanvas canvas, Size area)
    {
        long start = Stopwatch.GetTimestamp(); int count = canvas.Save();
        try { Draw?.Invoke(this, new(canvas, area)); }
        finally
        {
            canvas.RestoreToCount(count); DrawCount++;
            LastDrawMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }
    }
}
