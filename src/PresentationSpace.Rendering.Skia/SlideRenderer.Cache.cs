using System.Collections.Immutable;
using PresentationSpace.Core;
using SkiaSharp;

namespace PresentationSpace.Rendering.Skia;

public readonly record struct RenderCacheStatistics(long Hits, long Misses, long SceneHits, int Pictures, long ApproximateBytes);

public sealed partial class SlideRenderer
{
    private sealed record CachedPicture(SlideShape Shape, SKPicture Picture, long Bytes);
    private readonly Dictionary<Guid, LinkedListNode<CachedPicture>> _pictures = [];
    private readonly LinkedList<CachedPicture> _pictureLru = new();
    private SKPicture? _scene;
    private ImmutableArray<SlideShape> _sceneShapes;
    private string? _sceneBackground;
    private GradientFill? _sceneBackgroundGradient;
    private float _sceneWidth, _sceneHeight;
    private ITypefaceResolver? _cacheResolver;
    private long _cacheResolverVersion = -1;
    private long _pictureBytes, _hits, _misses, _sceneHits, _sceneBytes, _recordingSceneBytes;
    private bool _recordingScene;
    private ImmutableArray<SlideShape> _rejectedScene;
    public bool EnablePictureCache { get; set; } = true;
    public bool EnableSceneCache { get; set; } = true;
    public long PictureCacheBudget { get; set; } = 16 * 1024 * 1024;
    public int MaximumCachedPictures { get; set; } = 1024;
    public RenderCacheStatistics CacheStatistics => new(_hits, _misses, _sceneHits, _pictures.Count,
        _pictureBytes + _sceneBytes);

    /// <summary>Call when a resolver's fonts change in place. Replacing the resolver invalidates automatically.</summary>
    public void ClearRenderCache()
    {
        _textLayout.Clear();
        ClearGradientCache();
        ClearPictureCache();
    }
    private void ClearPictureCache()
    {
        _scene?.Dispose(); _scene = null; _sceneShapes = default; _sceneBackground = null; _sceneBackgroundGradient = null; _sceneBytes = 0; _rejectedScene = default;
        foreach (var entry in _pictureLru) entry.Picture.Dispose();
        _pictures.Clear(); _pictureLru.Clear(); _pictureBytes = 0;
    }
    private void PrepareCache()
    {
        var resolver = TypefaceResolver ?? DefaultTypefaceResolver;
        long version = (resolver as IVersionedTypefaceResolver)?.Version ?? 0;
        if (!ReferenceEquals(resolver, _cacheResolver) || version != _cacheResolverVersion) { ClearRenderCache(); _cacheResolver = resolver; _cacheResolverVersion = version; }
        if (!EnablePictureCache) { ClearPictureCache(); return; }
        while (_pictures.Count > Math.Max(0, MaximumCachedPictures) || _pictureBytes > Math.Max(0, PictureCacheBudget)) EvictPicture();
        if (_scene is not null && (!EnableSceneCache || !EnablePictureCache || _sceneShapes.Length > Math.Max(0, MaximumCachedPictures) || _sceneBytes > Math.Max(0, PictureCacheBudget))) { _scene.Dispose(); _scene = null; _sceneShapes = default; _sceneBackground = null; _sceneBackgroundGradient = null; _sceneBytes = 0; }
    }
    private void EvictPicture()
    {
        if (_pictureLru.First is not { } first) return;
        _pictureBytes -= first.Value.Bytes; _pictures.Remove(first.Value.Shape.Id);
        first.Value.Picture.Dispose(); _pictureLru.RemoveFirst();
    }
    private static bool SameDrawing(SlideShape a, SlideShape b) => ReferenceEquals(a, b) ||
        a.Kind == b.Kind && a.Bounds.Width == b.Bounds.Width && a.Bounds.Height == b.Bounds.Height &&
        a.Fill == b.Fill && a.FillGradient == b.FillGradient && a.Rotation == b.Rotation && a.Stroke == b.Stroke && a.StrokeWidth == b.StrokeWidth &&
        a.Text == b.Text && a.TextStyle == b.TextStyle && a.TextBox == b.TextBox && a.TextRanges == b.TextRanges &&
        ReferenceEquals(a.Chart, b.Chart) && ReferenceEquals(a.Table, b.Table) && a.Cells == b.Cells &&
        a.Labels == b.Labels && a.Values == b.Values && a.TableColumns == b.TableColumns;

    private void DrawRetainedShape(SKCanvas canvas, PresentationDocument document, SlideShape shape)
    {
        // Picture content is drawn directly in slide coordinates, avoiding a shape clone
        // and translation on each frame. Retained scenes never pin decoded images twice.
        if (shape.Kind == ShapeKind.Image) { DrawShape(canvas, document, shape); return; }
        // Images are already cheap cached-image draws; do not pin decoded images in retained pictures.
        if (!EnablePictureCache || MaximumCachedPictures <= 0 || PictureCacheBudget <= 0 || shape.Kind == ShapeKind.Image)
        {
            canvas.Save();
            try
            {
                canvas.Translate(shape.Bounds.X, shape.Bounds.Y);
                DrawShape(canvas, document, shape with { Bounds = new(0, 0, shape.Bounds.Width, shape.Bounds.Height) });
            }
            finally { canvas.Restore(); }
            return;
        }
        SKPicture? picture = null;
        if (_pictures.TryGetValue(shape.Id, out var node))
        {
            if (SameDrawing(node.Value.Shape, shape))
            { _hits++; _pictureLru.Remove(node); _pictureLru.AddLast(node); picture = node.Value.Picture; }
            else
            { _pictureBytes -= node.Value.Bytes; node.Value.Picture.Dispose(); _pictureLru.Remove(node); _pictures.Remove(shape.Id); }
        }
        bool temporary = false;
        if (picture is null)
        {
            _misses++;
            using var recorder = new SKPictureRecorder();
            float bleed = Math.Max(2, shape.StrokeWidth * 4 + (shape.Kind == ShapeKind.Arrow ? 20 : 0));
            var recording = recorder.BeginRecording(new(-bleed, -bleed, shape.Bounds.Width + bleed, shape.Bounds.Height + bleed));
            DrawShape(recording, document, shape with { Bounds = new(0, 0, shape.Bounds.Width, shape.Bounds.Height) });
            picture = recorder.EndRecording();
            long bytes = (long)picture.ApproximateBytesUsed;
            if (bytes > PictureCacheBudget) temporary = true;
            else
            {
                while (_pictures.Count > 0 && (_pictures.Count >= MaximumCachedPictures || _pictureBytes + bytes > PictureCacheBudget)) EvictPicture();
                _pictures[shape.Id] = _pictureLru.AddLast(new CachedPicture(shape, picture, bytes)); _pictureBytes += bytes;
            }
        }
        if (_recordingScene) _recordingSceneBytes += (long)picture.ApproximateBytesUsed;
        canvas.Save();
        try { canvas.Translate(shape.Bounds.X, shape.Bounds.Y); canvas.DrawPicture(picture); }
        finally { canvas.Restore(); if (temporary) picture.Dispose(); }
    }

    private bool DrawRetainedScene(SKCanvas canvas, PresentationDocument document, Slide slide)
    {
        if (!EnableSceneCache || !EnablePictureCache || PictureCacheBudget <= 0) return false;
        if (_scene is not null && _sceneShapes == slide.Shapes && _sceneBackground == slide.Background && _sceneBackgroundGradient == slide.BackgroundGradient &&
            _sceneWidth == document.Width && _sceneHeight == document.Height)
        { _sceneHits++; canvas.DrawPicture(_scene); return true; }
        _scene?.Dispose(); _scene = null; _sceneShapes = default; _sceneBackground = null; _sceneBackgroundGradient = null; _sceneBytes = 0;
        if (_rejectedScene == slide.Shapes || slide.Shapes.Length > MaximumCachedPictures) return false;
        // Scenes with images rely on the image budget instead of retaining image references twice.
        if (slide.Shapes.Any(s => s.Kind == ShapeKind.Image)) return false;
        using var recorder = new SKPictureRecorder();
        var recording = recorder.BeginRecording(new(0, 0, document.Width, document.Height));
        _recordingScene = true; _recordingSceneBytes = 0;
        try { RenderContents(recording, document, slide, float.PositiveInfinity); }
        finally { _recordingScene = false; }
        var picture = recorder.EndRecording();
        long retainedBytes = (long)picture.ApproximateBytesUsed + _recordingSceneBytes;
        if (retainedBytes > PictureCacheBudget) { _rejectedScene = slide.Shapes; picture.Dispose(); return false; }
        _sceneBytes = retainedBytes;
        _scene = picture; _sceneShapes = slide.Shapes; _sceneBackground = slide.Background; _sceneBackgroundGradient = slide.BackgroundGradient; _sceneWidth = document.Width; _sceneHeight = document.Height; canvas.DrawPicture(picture); return true;
    }
}
