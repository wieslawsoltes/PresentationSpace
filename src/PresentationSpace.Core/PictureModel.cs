namespace PresentationSpace.Core;

public enum PictureFit { Stretch, Contain, Cover }
public enum PictureMask { Rectangle, Ellipse }

/// <summary>Fractions from the corresponding edges: .25 is a 25% inset; negative values extend outside.</summary>
public sealed record PictureInsets(float Left = 0, float Top = 0, float Right = 0, float Bottom = 0)
{
    public static PictureInsets Empty { get; } = new();
}

/// <summary>Non-destructive picture framing. Null SlideShape.Picture retains the pre-0.10 Contain behavior.</summary>
public sealed record PictureSpec
{
    public PictureInsets Source { get; init; } = PictureInsets.Empty;
    public PictureInsets Destination { get; init; } = PictureInsets.Empty;
    public PictureFit Fit { get; init; }
    public PictureMask Mask { get; init; }
    public bool FlipHorizontal { get; init; }
    public bool FlipVertical { get; init; }
    /// <summary>Picture pixels only; the enclosing shape opacity additionally affects fill and outline.</summary>
    public float Opacity { get; init; } = 1;
}

/// <summary>Normalized source window and destination frame before clipping the source to actual raster pixels.</summary>
public readonly record struct PictureFrame(RectF Source, RectF Destination);
/// <summary>Clipped pixel source and corresponding slide-space destination. Empty means no source pixels are visible.</summary>
public readonly record struct PicturePlacement(RectF Source, RectF Destination, bool Empty);

/// <summary>Allocation-free image framing shared by native export and the renderer; no raster decoding or UI dependency.</summary>
public static class PictureModel
{
    public static PictureSpec Legacy { get; } = new() { Fit = PictureFit.Contain };
    public static PictureSpec Resolve(SlideShape shape) => shape.Picture ?? Legacy;
    public static void Validate(PictureSpec picture)
    {
        ArgumentNullException.ThrowIfNull(picture);
        if (!Enum.IsDefined(picture.Fit) || !Enum.IsDefined(picture.Mask) || !float.IsFinite(picture.Opacity) || picture.Opacity is < 0 or > 1)
            throw new InvalidDataException("Invalid picture fit, mask or opacity.");
        ValidateInsets(picture.Source); ValidateInsets(picture.Destination);
    }
    public static void ValidateInsets(PictureInsets insets)
    {
        if (insets is null || !Valid(insets.Left) || !Valid(insets.Top) || !Valid(insets.Right) || !Valid(insets.Bottom) ||
            1d - insets.Left - insets.Right < .00001 || 1d - insets.Top - insets.Bottom < .00001)
            throw new InvalidDataException("Picture offsets must be finite, between -1000% and 1000%, and leave a positive rectangle.");
        static bool Valid(float value) => float.IsFinite(value) && value is >= -10 and <= 10;
    }
    public static SlideShape Apply(SlideShape shape, PictureSpec picture)
    {
        ArgumentNullException.ThrowIfNull(shape); Validate(picture);
        if (shape.Kind != ShapeKind.Image) throw new InvalidOperationException("Select a picture to change its framing.");
        return Resolve(shape) == picture ? shape : shape with { Picture = picture };
    }
    public static PictureFrame Frame(PictureSpec picture, float frameWidth, float frameHeight, int pixelWidth, int pixelHeight)
    {
        Validate(picture);
        if (!float.IsFinite(frameWidth) || !float.IsFinite(frameHeight) || frameWidth is <= 0 or > 100000 || frameHeight is <= 0 or > 100000 || pixelWidth <= 0 || pixelHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(frameWidth));
        var source = Rectangle(picture.Source); var target = Rectangle(picture.Destination);
        double sw = source.Width * (double)pixelWidth, sh = source.Height * (double)pixelHeight;
        double tw = target.Width * (double)frameWidth, th = target.Height * (double)frameHeight;
        if (picture.Fit == PictureFit.Contain)
        {
            double scale = Math.Min(tw / sw, th / sh);
            float w = (float)(sw * scale / frameWidth), h = (float)(sh * scale / frameHeight);
            target = new(target.X + (target.Width - w) / 2, target.Y + (target.Height - h) / 2, w, h);
        }
        else if (picture.Fit == PictureFit.Cover)
        {
            double scale = Math.Max(tw / sw, th / sh);
            float w = (float)(tw / scale / pixelWidth), h = (float)(th / scale / pixelHeight);
            source = new(source.X + (source.Width - w) / 2, source.Y + (source.Height - h) / 2, w, h);
        }
        return new(source, target);
    }
    public static PicturePlacement Place(PictureSpec picture, RectF bounds, int pixelWidth, int pixelHeight)
    {
        if (!float.IsFinite(bounds.X) || !float.IsFinite(bounds.Y) || !float.IsFinite(bounds.Right) || !float.IsFinite(bounds.Bottom))
            throw new ArgumentOutOfRangeException(nameof(bounds));
        var frame = Frame(picture, bounds.Width, bounds.Height, pixelWidth, pixelHeight);
        var s = frame.Source; var d = frame.Destination;
        float left = Math.Max(0, s.X), top = Math.Max(0, s.Y), right = Math.Min(1, s.Right), bottom = Math.Min(1, s.Bottom);
        if (left >= right || top >= bottom) return new(default, default, true);
        // Outsets are transparent, not clamped/stretched copies of the edge pixel.
        var destination = new RectF(bounds.X + (d.X + (left - s.X) / s.Width * d.Width) * bounds.Width,
            bounds.Y + (d.Y + (top - s.Y) / s.Height * d.Height) * bounds.Height,
            (right - left) / s.Width * d.Width * bounds.Width, (bottom - top) / s.Height * d.Height * bounds.Height);
        return new(new(left * pixelWidth, top * pixelHeight, (right - left) * pixelWidth, (bottom - top) * pixelHeight), destination, false);
    }
    public static PictureInsets Insets(RectF rectangle) => new(rectangle.X, rectangle.Y, 1 - rectangle.Right, 1 - rectangle.Bottom);
    private static RectF Rectangle(PictureInsets i) => new(i.Left, i.Top, 1 - i.Left - i.Right, 1 - i.Top - i.Bottom);
}
