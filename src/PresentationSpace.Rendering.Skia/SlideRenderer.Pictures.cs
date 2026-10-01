using PresentationSpace.Core;
using SkiaSharp;

namespace PresentationSpace.Rendering.Skia;

public readonly record struct ImageCacheStatistics(long Hits, long Misses, int Entries, long DecodedPixelBytes);

public sealed partial class SlideRenderer
{
    private sealed class CachedImage(SKImage image, string source, long bytes)
    {
        public SKImage Image { get; } = image;
        public string Source { get; } = source;
        public long Bytes { get; } = bytes;
        public LinkedListNode<string>? Node { get; set; }
    }
    private readonly Dictionary<string, CachedImage> _images = [];
    private readonly LinkedList<string> _imageLru = new();
    private long _imageBytes, _imageHits, _imageMisses;
    private SKPaint? _picturePaint, _pictureStroke;
    public long ImageCacheBudget { get; set; } = 64 * 1024 * 1024;
    public int MaximumCachedImages { get; set; } = 128;
    public ImageCacheStatistics ImageCacheStatistics => new(_imageHits, _imageMisses, _images.Count, _imageBytes);

    public void ClearImageCache()
    {
        foreach (var image in _images.Values) image.Image.Dispose();
        _images.Clear(); _imageLru.Clear(); _imageBytes = 0;
        _picturePaint?.Dispose(); _picturePaint = null; _pictureStroke?.Dispose(); _pictureStroke = null;
    }
    private void RemoveImage(string id)
    {
        if (!_images.Remove(id, out var entry)) return;
        _imageBytes -= entry.Bytes; _imageLru.Remove(entry.Node!); entry.Image.Dispose();
    }
    private SKImage? GetImage(PresentationDocument document, string? id, out bool temporary)
    {
        temporary = false;
        long budget = Math.Max(0, ImageCacheBudget); int maximum = Math.Max(0, MaximumCachedImages);
        while (_images.Count > 0 && (_images.Count > maximum || _imageBytes > budget)) RemoveImage(_imageLru.First!.Value);
        if (id is null || !document.Assets.TryGetValue(id, out var asset)) return null;
        if (_images.TryGetValue(id, out var cached))
        {
            if (cached.Source == asset.Base64)
            {
                _imageHits++; _imageLru.Remove(cached.Node!); _imageLru.AddLast(cached.Node!); return cached.Image;
            }
            RemoveImage(id);
        }
        _imageMisses++;
        try
        {
            using var data = SKData.CreateCopy(Convert.FromBase64String(asset.Base64)); using var codec = SKCodec.Create(data);
            if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0 || (long)codec.Info.Width * codec.Info.Height > 16_000_000) return null;
            var image = SKImage.FromEncodedData(data); if (image is null) return null;
            long bytes = (long)image.Width * image.Height * 4;
            // Oversized/disabled entries are used for this draw only; do not evict useful small images to retain them.
            if (bytes > budget || maximum == 0) { temporary = true; return image; }
            while (_images.Count > 0 && (_images.Count >= maximum || _imageBytes + bytes > budget)) RemoveImage(_imageLru.First!.Value);
            _images[id] = new(image, asset.Base64, bytes) { Node = _imageLru.AddLast(id) }; _imageBytes += bytes; return image;
        }
        catch (FormatException) { return null; }
    }
    private void DrawPicture(SKCanvas canvas, PresentationDocument document, SlideShape shape)
    {
        var picture = PictureModel.Resolve(shape); PictureModel.Validate(picture);
        var b = shape.Bounds; var rectangle = new SKRect(b.X, b.Y, b.Right, b.Bottom);
        var image = GetImage(document, shape.AssetId, out bool temporary);
        canvas.Save();
        try
        {
            if (picture.FlipHorizontal || picture.FlipVertical)
            {
                canvas.Translate(b.Center.X, b.Center.Y);
                canvas.Scale(picture.FlipHorizontal ? -1 : 1, picture.FlipVertical ? -1 : 1);
                canvas.Translate(-b.Center.X, -b.Center.Y);
            }
            using var path = picture.Mask == PictureMask.Ellipse ? new SKPath() : null;
            path?.AddOval(rectangle);
            canvas.Save();
            try
            {
                if (path is not null) canvas.ClipPath(path, SKClipOperation.Intersect, true); else canvas.ClipRect(rectangle);
                var paint = _picturePaint ??= new SKPaint { IsAntialias = true };
                // Legacy picture shapes ignored their unused Fill property; preserve that rendering contract.
                if (shape.Picture is not null) { paint.Color = Color(shape.Fill); canvas.DrawRect(rectangle, paint); }
                if (image is not null)
                {
                    var placement = PictureModel.Place(picture, b, image.Width, image.Height);
                    if (!placement.Empty && picture.Opacity > 0)
                    {
                        var s = placement.Source; var d = placement.Destination;
                        paint.Color = SKColors.White.WithAlpha((byte)Math.Clamp((int)Math.Round(picture.Opacity * 255), 0, 255));
                        canvas.DrawImage(image, new SKRect(s.X,s.Y,s.Right,s.Bottom), new SKRect(d.X,d.Y,d.Right,d.Bottom),
                            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None), paint);
                    }
                }
                else
                {
                    paint.Color = Color("#F0F1F4"); canvas.DrawRect(rectangle, paint);
                    DrawText(canvas, "Picture unavailable", b, new() { FontSize = 20, Alignment = ParagraphAlignment.Center, VerticalAlignment = Core.VerticalAlignment.Middle });
                }
            }
            finally { canvas.Restore(); }
            if (shape.StrokeWidth > 0)
            {
                var line = _pictureStroke ??= new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke };
                line.Color = Color(shape.Stroke); line.StrokeWidth = shape.StrokeWidth;
                if (path is not null) canvas.DrawPath(path, line); else canvas.DrawRect(rectangle, line);
            }
        }
        finally { canvas.Restore(); if (temporary) image?.Dispose(); }
    }
}
