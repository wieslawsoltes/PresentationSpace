using PresentationSpace.Core;
using SkiaSharp;

namespace PresentationSpace.Rendering.Skia;

public sealed partial class SlideRenderer
{
    private readonly TextLayoutEngine _textLayout = new();
    private TextLayoutEngine TextLayout
    {
        get { _textLayout.TypefaceResolver = TypefaceResolver ?? DefaultTypefaceResolver; return _textLayout; }
    }
    public TextLayoutCacheStatistics TextCacheStatistics => _textLayout.CacheStatistics;
    public long TextCacheBudget { get => _textLayout.CacheBudget; set => _textLayout.CacheBudget = value; }
    public int MaximumCachedTextLayouts { get => _textLayout.MaximumCachedLayouts; set => _textLayout.MaximumCachedLayouts = value; }

    /// <summary>Immutable line/advance metrics from the exact layout used for drawing. The supplied width includes padding; metrics report the content area.</summary>
    public TextLayoutMetrics LayoutRichText(SlideShape shape, float width, float padding = 0)
    {
        ArgumentNullException.ThrowIfNull(shape);
        if (!float.IsFinite(width) || width <= 0 || !float.IsFinite(padding) || padding < 0) throw new ArgumentOutOfRangeException(nameof(width));
        return TextLayout.Measure(shape.Text, shape.TextStyle, Math.Max(1, width - 2 * padding), shape.TextRanges);
    }

    public float MeasureRichTextHeight(SlideShape shape, float width, float padding = 0) => LayoutRichText(shape, width, padding).Height + 2 * padding;

    public void DrawRichText(SKCanvas canvas, SlideShape shape, float padding = 3)
    {
        ArgumentNullException.ThrowIfNull(shape);
        TextLayout.Draw(canvas, shape.Text, shape.TextStyle, shape.Bounds, padding, shape.TextRanges);
    }
}
