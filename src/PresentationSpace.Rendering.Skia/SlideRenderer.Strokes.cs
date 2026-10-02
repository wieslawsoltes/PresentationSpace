using System.Collections.Immutable;
using PresentationSpace.Core;
using SkiaSharp;

namespace PresentationSpace.Rendering.Skia;

public readonly record struct StrokeCacheStatistics(long Hits, long Misses, int Entries, long ApproximateBytes);

public sealed partial class SlideRenderer
{
    private readonly record struct DashKey(StrokeDash Preset, ImmutableArray<StrokeDashSegment> Custom, float Width);
    private sealed record DashEntry(DashKey Key, SKPathEffect Effect, long Bytes);
    private readonly Dictionary<DashKey, LinkedListNode<DashEntry>> _dashes = [];
    private readonly LinkedList<DashEntry> _dashLru = new();
    private long _dashHits, _dashMisses, _dashBytes;
    private SKPaint? _linePaint, _markerPaint;
    private SKPath? _markerPath;
    public int MaximumCachedStrokePatterns { get; set; } = 128;
    public long StrokePatternCacheBudget { get; set; } = 64 * 1024;
    public StrokeCacheStatistics StrokeCacheStatistics => new(_dashHits, _dashMisses, _dashes.Count, _dashBytes);
    public void ClearStrokeCache()
    {
        _linePaint?.Dispose(); _linePaint = null; _markerPaint?.Dispose(); _markerPaint = null;
        _markerPath?.Dispose(); _markerPath = null;
        foreach (var entry in _dashLru) entry.Effect.Dispose();
        _dashLru.Clear(); _dashes.Clear(); _dashBytes = 0;
    }
    private void TrimDashes(int maximum, long budget)
    {
        while (_dashLru.First is { } first && (_dashes.Count > maximum || _dashBytes > budget))
        {
            _dashBytes -= first.Value.Bytes; _dashes.Remove(first.Value.Key);
            first.Value.Effect.Dispose(); _dashLru.RemoveFirst();
        }
    }
    private void ConfigureStroke(SKPaint paint, SlideShape shape, RectF? localBounds = null)
    {
        var style = StrokeModel.Resolve(shape);
        paint.PathEffect = null;
        paint.IsAntialias = true; paint.Style = SKPaintStyle.Stroke; paint.StrokeWidth = shape.StrokeWidth;
        paint.StrokeCap = style.Cap switch { StrokeCap.Flat => SKStrokeCap.Butt, StrokeCap.Square => SKStrokeCap.Square, _ => SKStrokeCap.Round };
        paint.StrokeJoin = style.Join switch { StrokeJoin.Bevel => SKStrokeJoin.Bevel, StrokeJoin.Miter => SKStrokeJoin.Miter, _ => SKStrokeJoin.Round };
        paint.StrokeMiter = style.MiterLimit;
        // Degenerate line extents are valid DrawingML; a gradient still needs a finite fill box.
        var bounds = localBounds ?? shape.Bounds;
        var box = bounds with { Width = Math.Max(1, bounds.Width), Height = Math.Max(1, bounds.Height) };
        ConfigureFill(paint, shape.Stroke, style.Gradient, box, shape.Rotation);
        int maximum = Math.Max(0, MaximumCachedStrokePatterns); long budget = Math.Max(0, StrokePatternCacheBudget);
        TrimDashes(maximum, budget);
        if (shape.StrokeWidth <= 0 || style.Dash == StrokeDash.Solid && style.CustomDashes.IsDefaultOrEmpty) return;
        var key = new DashKey(style.CustomDashes.IsDefaultOrEmpty ? style.Dash : StrokeDash.Solid, style.CustomDashes, shape.StrokeWidth);
        if (_dashes.TryGetValue(key, out var cached))
        {
            _dashHits++; _dashLru.Remove(cached); _dashLru.AddLast(cached); paint.PathEffect = cached.Value.Effect; return;
        }
        StrokeModel.Validate(style); var pattern = StrokeModel.DashPattern(style);
        var intervals = new float[pattern.Length * 2];
        for (int i = 0; i < pattern.Length; i++) { intervals[i * 2] = pattern[i].Dash * shape.StrokeWidth; intervals[i * 2 + 1] = pattern[i].Gap * shape.StrokeWidth; }
        _dashMisses++;
        var effect = SKPathEffect.CreateDash(intervals, 0) ?? throw new InvalidOperationException("Skia rejected the dash pattern.");
        long bytes = 128 + intervals.Length * 8L;
        bool temporary = maximum == 0 || bytes > budget;
        try
        {
            paint.PathEffect = effect; // Paint owns an independent native reference.
            if (!temporary)
            {
                TrimDashes(maximum - 1, budget - bytes);
                _dashes.Add(key, _dashLru.AddLast(new DashEntry(key, effect, bytes))); _dashBytes += bytes;
            }
        }
        finally { if (temporary) effect.Dispose(); }
    }
    /// <summary>Standalone line/arrow drawing in local slide coordinates. Caller owns rotation/opacity; host transform and clip are retained.</summary>
    public void DrawLine(SKCanvas canvas, SlideShape shape)
    {
        ArgumentNullException.ThrowIfNull(canvas); ArgumentNullException.ThrowIfNull(shape);
        if (!StrokeModel.IsLine(shape)) throw new ArgumentException("Expected a line or arrow.", nameof(shape));
        StrokeModel.Validate(StrokeModel.Settings(shape));
        DrawLineCore(canvas, shape);
    }
    private void DrawLineCore(SKCanvas canvas, SlideShape shape, RectF? localBounds = null)
    {
        if (shape.StrokeWidth <= 0) return; // Zero width means no outline, never a Skia hairline.
        var (start, end) = StrokeModel.Endpoints(localBounds ?? shape.Bounds, shape.LineDirection);
        float dx = end.X - start.X, dy = end.Y - start.Y, length = MathF.Sqrt(dx * dx + dy * dy);
        if (!float.IsFinite(length) || !float.IsFinite(start.X) || !float.IsFinite(start.Y) || !float.IsFinite(end.X) || !float.IsFinite(end.Y))
            throw new ArgumentException("Line coordinates must be finite.", nameof(shape));
        var paint = _linePaint ??= new SKPaint(); ConfigureStroke(paint, shape, localBounds);
        var style = StrokeModel.Resolve(shape);
        try
        {
            if (length <= .00001f)
            {
                // There is no direction for an endpoint decoration on a collapsed segment.
                if (style.Cap == StrokeCap.Round) { paint.Style = SKPaintStyle.Fill; paint.PathEffect = null; canvas.DrawCircle(start.X, start.Y, shape.StrokeWidth / 2, paint); }
                else if (style.Cap == StrokeCap.Square) { paint.Style = SKPaintStyle.Fill; paint.PathEffect = null; canvas.DrawRect(start.X - shape.StrokeWidth / 2, start.Y - shape.StrokeWidth / 2, shape.StrokeWidth, shape.StrokeWidth, paint); }
                return;
            }
            dx /= length; dy /= length;
            float MarkerLength(LineEnd marker) => Math.Max(1, shape.StrokeWidth) * StrokeModel.EndScale(marker.Length);
            float Trim(LineEnd marker) => marker.Kind switch { LineEndKind.None or LineEndKind.OpenArrow => 0, LineEndKind.Stealth => MarkerLength(marker) * .6f, _ => MarkerLength(marker) };
            bool legacy = shape.Kind == ShapeKind.Arrow && shape.Outline is null;
            float first = Trim(style.Begin), last = legacy ? 0 : Trim(style.End);
            if (first + last < length)
                canvas.DrawLine(start.X + dx * first, start.Y + dy * first, end.X - dx * last, end.Y - dy * last, paint);
            var markerPaint = _markerPaint ??= new SKPaint { IsAntialias = true };
            // End decorations are solid geometry even when the shaft is dashed.
            ConfigureStroke(markerPaint, shape, localBounds); markerPaint.PathEffect = null;
            try
            {
                DrawLineEnd(canvas, start, -dx, -dy, style.Begin, shape.StrokeWidth, markerPaint);
                DrawLineEnd(canvas, end, dx, dy, style.End, shape.StrokeWidth, markerPaint, legacy);
            }
            finally { markerPaint.Shader = null; markerPaint.PathEffect = null; }
        }
        finally { paint.PathEffect = null; paint.Shader = null; }
    }
    private void DrawLineEnd(SKCanvas canvas, PointF tip, float dx, float dy, LineEnd marker, float strokeWidth, SKPaint paint, bool legacy = false)
    {
        if (marker.Kind == LineEndKind.None) return;
        var path = _markerPath ??= new SKPath(); path.Reset();
        float length = Math.Max(1, strokeWidth) * StrokeModel.EndScale(marker.Length);
        float half = Math.Max(1, strokeWidth) * StrokeModel.EndScale(marker.Width) / 2;
        if (legacy) { float size = Math.Max(12, strokeWidth * 4); length = size * MathF.Cos(.5f); half = size * MathF.Sin(.5f); }
        SKPoint At(float x, float y) => new(tip.X + dx * x - dy * y, tip.Y + dy * x + dx * y);
        paint.Style = marker.Kind == LineEndKind.OpenArrow ? SKPaintStyle.Stroke : SKPaintStyle.Fill;
        switch (marker.Kind)
        {
            case LineEndKind.Oval:
                path.AddOval(new SKRect(-length, -half, 0, half));
                path.Transform(new SKMatrix { ScaleX = dx, SkewX = -dy, TransX = tip.X, SkewY = dy, ScaleY = dx, TransY = tip.Y, Persp2 = 1 });
                break;
            case LineEndKind.Diamond:
                path.MoveTo(At(0, 0)); path.LineTo(At(-length / 2, half)); path.LineTo(At(-length, 0)); path.LineTo(At(-length / 2, -half)); path.Close();
                break;
            case LineEndKind.OpenArrow:
                path.MoveTo(At(-length, half)); path.LineTo(At(0, 0)); path.LineTo(At(-length, -half));
                break;
            default:
                path.MoveTo(At(0, 0)); path.LineTo(At(-length, half));
                if (marker.Kind == LineEndKind.Stealth) path.LineTo(At(-length * .6f, 0));
                path.LineTo(At(-length, -half)); path.Close(); break;
        }
        canvas.DrawPath(path, paint);
    }
}
