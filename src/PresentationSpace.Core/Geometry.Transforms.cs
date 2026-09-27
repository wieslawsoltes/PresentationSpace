namespace PresentationSpace.Core;

public static partial class Geometry
{
    /// <summary>Resize in local coordinates while keeping the opposite handle stationary in slide coordinates.</summary>
    public static RectF ResizeRotated(RectF original, float rotation, int handle, PointF localDelta, bool keepAspect)
    {
        if (handle is < 0 or > 7) throw new ArgumentOutOfRangeException(nameof(handle));
        var resized = Resize(original, handle, localDelta, keepAspect);
        int opposite = (handle + 4) % 8;
        var fixedBefore = Rotate(Handles(original)[opposite], original.Center, rotation);
        var fixedAfter = Rotate(Handles(resized)[opposite], resized.Center, rotation);
        return resized with { X = resized.X + fixedBefore.X - fixedAfter.X, Y = resized.Y + fixedBefore.Y - fixedAfter.Y };
    }

    public static RectF VisualBounds(SlideShape shape)
    {
        var points = Handles(shape.Bounds).Where((_, index) => index % 2 == 0)
            .Select(point => Rotate(point, shape.Bounds.Center, shape.Rotation)).ToArray();
        float x = points.Min(p => p.X), y = points.Min(p => p.Y);
        return new(x, y, points.Max(p => p.X) - x, points.Max(p => p.Y) - y);
    }
}
