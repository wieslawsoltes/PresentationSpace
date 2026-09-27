using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PresentationSpace.Core;
using PresentationSpace.Rendering.Skia;

namespace PresentationSpace.Controls.Uno;

/// <summary>Invalidates only for slide drawing dependencies, not selection, notes, names or comments.</summary>
public sealed class SlidePreview : UserControl
{
    private readonly PresentationCanvas _canvas = new();
    private readonly SlideRenderer _renderer;
    private readonly bool _ownsRenderer;
    private PresentationDocument? _document;
    private Slide? _slide;
    public long DrawCount => _canvas.DrawCount;
    public SlidePreview() : this(new SlideRenderer(), true) { }
    internal SlidePreview(SlideRenderer renderer, bool ownsRenderer = false)
    {
        _renderer = renderer; _ownsRenderer = ownsRenderer; Content = _canvas;
        _canvas.Draw += (_, e) =>
        {
            if (_document is not { } document || _slide is not { } slide) return;
            e.Canvas.Save();
            try { e.Canvas.Scale((float)e.Size.Width / document.Width, (float)e.Size.Height / document.Height); _renderer.Render(e.Canvas, document, slide); }
            finally { e.Canvas.Restore(); }
        };
        SizeChanged += (_, _) => _canvas.Invalidate();
        Unloaded += (_, _) => { if (_ownsRenderer) _renderer.Dispose(); };
    }
    public void SetSlide(PresentationDocument document, Slide slide)
    {
        bool changed = _slide?.Shapes != slide.Shapes || _slide?.Background != slide.Background ||
            _document?.Width != document.Width || _document?.Height != document.Height || !ReferenceEquals(_document?.Assets, document.Assets);
        _document = document; _slide = slide;
        if (changed) _canvas.Invalidate();
    }
    public void Clear() { _document = null; _slide = null; }
}
