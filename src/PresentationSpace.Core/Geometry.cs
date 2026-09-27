namespace PresentationSpace.Core;

public static class Geometry
{
    public static PointF Rotate(PointF p, PointF center, float degrees)
    {
        double a = degrees * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
        return new((float)(center.X + (p.X - center.X) * c - (p.Y - center.Y) * s), (float)(center.Y + (p.X - center.X) * s + (p.Y - center.Y) * c));
    }
    public static bool HitTest(SlideShape shape, PointF point, float tolerance = 5)
    {
        if (shape.Hidden || shape.Locked) return false;
        var p = Rotate(point, shape.Bounds.Center, -shape.Rotation);
        var b = shape.Bounds;
        if (shape.Kind is ShapeKind.Line or ShapeKind.Arrow)
        {
            float dx = b.Width, dy = b.Height, t = Math.Clamp(((p.X - b.X) * dx + (p.Y - b.Y) * dy) / (dx * dx + dy * dy), 0, 1);
            return MathF.Sqrt(MathF.Pow(p.X - b.X - t * dx, 2) + MathF.Pow(p.Y - b.Y - t * dy, 2)) <= tolerance + shape.StrokeWidth;
        }
        if (!b.Contains(p)) return false;
        if (shape.Kind == ShapeKind.Ellipse)
        {
            float x = (p.X - b.Center.X) / (b.Width / 2), y = (p.Y - b.Center.Y) / (b.Height / 2);
            return x * x + y * y <= 1;
        }
        if (shape.Kind == ShapeKind.Diamond) return Math.Abs((p.X - b.Center.X) / (b.Width / 2)) + Math.Abs((p.Y - b.Center.Y) / (b.Height / 2)) <= 1;
        return true;
    }
    public static RectF Union(IEnumerable<SlideShape> shapes)
    {
        var items = shapes.ToArray();
        if (items.Length == 0) return default;
        float x = items.Min(s => s.Bounds.X), y = items.Min(s => s.Bounds.Y);
        return new(x, y, items.Max(s => s.Bounds.Right) - x, items.Max(s => s.Bounds.Bottom) - y);
    }
    public static float Snap(float value, float grid) => grid <= 0 ? value : MathF.Round(value / grid) * grid;
    public static RectF Resize(RectF original, int handle, PointF delta, bool keepAspect)
    {
        float x = original.X, y = original.Y, w = original.Width, h = original.Height;
        if (handle is 0 or 6 or 7) { x += delta.X; w -= delta.X; }
        if (handle is 2 or 3 or 4) w += delta.X;
        if (handle is 0 or 1 or 2) { y += delta.Y; h -= delta.Y; }
        if (handle is 4 or 5 or 6) h += delta.Y;
        w = Math.Max(8, w); h = Math.Max(8, h);
        if (keepAspect) { h = w * original.Height / original.Width; if (handle is 0 or 1 or 2) y = original.Bottom - h; }
        if (handle is 0 or 6 or 7) x = original.Right - w;
        if (handle is 0 or 1 or 2) y = original.Bottom - h;
        return new(x, y, w, h);
    }
    public static PointF[] Handles(RectF b) => [new(b.X,b.Y),new(b.Center.X,b.Y),new(b.Right,b.Y),new(b.Right,b.Center.Y),new(b.Right,b.Bottom),new(b.Center.X,b.Bottom),new(b.X,b.Bottom),new(b.X,b.Center.Y)];
}
