using PresentationSpace.Core;
using SkiaSharp;

namespace PresentationSpace.Rendering.Skia;

public readonly record struct GradientCacheStatistics(long Hits, long Misses, int Entries, long ApproximateBytes);

public sealed partial class SlideRenderer
{
    private readonly record struct GradientKey(GradientFill Fill, RectF Bounds, float Rotation);
    private sealed record CachedGradient(SKShader Shader, long Bytes);
    private readonly Dictionary<GradientKey, LinkedListNode<(GradientKey Key, CachedGradient Value)>> _gradients = [];
    private readonly LinkedList<(GradientKey Key, CachedGradient Value)> _gradientLru = new();
    private long _gradientBytes, _gradientHits, _gradientMisses;
    private SKPaint? _gradientPaint;
    public int MaximumCachedGradients { get; set; } = 128;
    /// <summary>Estimated managed/native gradient data, not a process or GPU memory limit.</summary>
    public long GradientCacheBudget { get; set; } = 256 * 1024;
    public GradientCacheStatistics GradientCacheStatistics => new(_gradientHits, _gradientMisses, _gradients.Count, _gradientBytes);
    public void ClearGradientCache()
    {
        _gradientPaint?.Dispose(); _gradientPaint = null;
        foreach (var item in _gradientLru) item.Value.Shader.Dispose();
        _gradientLru.Clear(); _gradients.Clear(); _gradientBytes = 0;
    }
    private void TrimGradients(int maximum, long budget)
    {
        while (_gradientLru.First is { } first && (_gradients.Count > maximum || _gradientBytes > budget))
        {
            _gradientBytes -= first.Value.Value.Bytes; _gradients.Remove(first.Value.Key);
            first.Value.Value.Shader.Dispose(); _gradientLru.RemoveFirst();
        }
    }
    private SKShader GradientShader(GradientFill fill, RectF bounds, float rotation, out bool temporary)
    {
        int maximum = Math.Max(0, MaximumCachedGradients); long budget = Math.Max(0, GradientCacheBudget);
        TrimGradients(maximum, budget);
        var key = new GradientKey(fill, bounds, fill.RotateWithShape ? 0 : rotation);
        if (_gradients.TryGetValue(key, out var node))
        {
            _gradientHits++; _gradientLru.Remove(node); _gradientLru.AddLast(node); temporary = false; return node.Value.Value.Shader;
        }
        var vector = GradientModel.Vector(fill, bounds, rotation); _gradientMisses++;
        var colors = new SKColor[fill.Stops.Length]; var positions = new float[colors.Length];
        for (int i = 0; i < colors.Length; i++)
        {
            var stop = fill.Stops[i]; positions[i] = stop.Offset;
            colors[i] = Color(stop.Color).WithAlpha((byte)Math.Clamp((int)Math.Round(stop.Opacity * 255), 0, 255));
        }
        var shader = SKShader.CreateLinearGradient(new(vector.Start.X, vector.Start.Y), new(vector.End.X, vector.End.Y), colors, positions, SKShaderTileMode.Clamp) ?? throw new InvalidOperationException("Skia could not create the linear gradient.");
        long bytes = 256 + fill.Stops.Length * 64L;
        temporary = maximum == 0 || bytes > budget;
        if (!temporary)
        {
            TrimGradients(maximum - 1, budget - bytes);
            var value = new CachedGradient(shader, bytes);
            _gradients.Add(key, _gradientLru.AddLast((key, value))); _gradientBytes += bytes;
        }
        return shader;
    }
    private void ConfigureFill(SKPaint paint, string solid, GradientFill? gradient, RectF bounds, float rotation = 0)
    {
        paint.Shader = null;
        paint.Color = gradient is null ? Color(solid) : SKColors.White;
        if (gradient is null) return;
        var shader = GradientShader(gradient, bounds, rotation, out bool temporary);
        try { paint.Shader = shader; } // SKPaint retains its own native shader reference.
        finally { if (temporary) shader.Dispose(); }
    }
    /// <summary>Standalone rectangular gradient drawing. Preserves the host transform/clip. Rotation describes the model, not an additional canvas transform.</summary>
    public void DrawGradient(SKCanvas canvas, GradientFill gradient, RectF bounds, float rotation = 0)
    {
        ArgumentNullException.ThrowIfNull(canvas); ArgumentNullException.ThrowIfNull(gradient);
        var paint = _gradientPaint ??= new SKPaint { IsAntialias = true };
        ConfigureFill(paint, "#00000000", gradient, bounds, rotation);
        try { canvas.DrawRect(new(bounds.X, bounds.Y, bounds.Right, bounds.Bottom), paint); }
        finally { paint.Shader = null; }
    }
}
