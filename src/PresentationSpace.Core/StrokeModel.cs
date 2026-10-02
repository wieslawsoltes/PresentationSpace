using System.Collections.Immutable;
using System.Globalization;

namespace PresentationSpace.Core;

public enum StrokeDash { Solid, Dot, Dash, LongDash, DashDot, LongDashDot, LongDashDotDot, SystemDash, SystemDot, SystemDashDot, SystemDashDotDot }
public enum StrokeCap { Flat, Round, Square }
public enum StrokeJoin { Round, Bevel, Miter }
public enum LineEndKind { None, Triangle, Stealth, Diamond, Oval, OpenArrow }
public enum LineEndSize { Small, Medium, Large }
/// <summary>One dash/gap pair, expressed as multiples of the outline width, not slide units.</summary>
public readonly record struct StrokeDashSegment(float Dash, float Gap);
public sealed record LineEnd
{
    public LineEndKind Kind { get; init; }
    public LineEndSize Width { get; init; } = LineEndSize.Medium;
    public LineEndSize Length { get; init; } = LineEndSize.Medium;
}
/// <summary>Immutable outline properties. Color/width remain on SlideShape for compatibility. An explicit None end overrides the legacy Arrow kind.</summary>
public sealed record StrokeSpec
{
    public StrokeDash Dash { get; init; }
    /// <summary>When nonempty, takes precedence over the preset Dash.</summary>
    public ImmutableArray<StrokeDashSegment> CustomDashes { get; init; } = [];
    public StrokeCap Cap { get; init; } = StrokeCap.Round;
    public StrokeJoin Join { get; init; } = StrokeJoin.Round;
    public float MiterLimit { get; init; } = 4;
    public LineEnd Begin { get; init; } = new();
    public LineEnd End { get; init; } = new();
    public GradientFill? Gradient { get; init; }
}
/// <summary>Local line-axis flips, applied before shape rotation. Only Line and Arrow shapes use these flags.</summary>
public sealed record LineDirection(bool FlipHorizontal = false, bool FlipVertical = false);
public sealed record StrokeSettings(string Color, float Width, StrokeSpec Style);

/// <summary>Framework-independent outline validation, authoring and line geometry. Units are 96-DPI slide coordinates unless noted.</summary>
public static class StrokeModel
{
    public const int MaximumDashSegments = 32;
    public static StrokeSpec Default { get; } = new();
    public static StrokeSpec DefaultArrow { get; } = new() { End = new() { Kind = LineEndKind.Triangle } };
    public static StrokeSpec DefaultPicture { get; } = new() { Cap = StrokeCap.Flat, Join = StrokeJoin.Miter };
    public static bool IsLine(SlideShape shape) => shape.Kind is ShapeKind.Line or ShapeKind.Arrow;
    public static bool Supports(SlideShape shape) => shape.Kind is not (ShapeKind.Table or ShapeKind.Chart);
    public static StrokeSpec Resolve(SlideShape shape) => shape.Outline ?? (shape.Kind switch
    { ShapeKind.Arrow => DefaultArrow, ShapeKind.Image => DefaultPicture, _ => Default });
    public static StrokeSettings Settings(SlideShape shape) => new(shape.Stroke, shape.StrokeWidth, Resolve(shape));
    public static SlideShape SolidColor(SlideShape shape, string color) => shape with { Stroke = color, Outline = shape.Outline is { Gradient: not null } style ? style with { Gradient = null } : shape.Outline };
    public static bool Equivalent(StrokeSpec a, StrokeSpec b) => ReferenceEquals(a, b) ||
        a.Dash == b.Dash && a.Cap == b.Cap && a.Join == b.Join && a.MiterLimit == b.MiterLimit &&
        a.Begin == b.Begin && a.End == b.End && a.CustomDashes.AsSpan().SequenceEqual(b.CustomDashes.AsSpan()) &&
        GradientModel.Equivalent(a.Gradient, b.Gradient);
    public static void Validate(StrokeSpec value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!Enum.IsDefined(value.Dash) || !Enum.IsDefined(value.Cap) || !Enum.IsDefined(value.Join) ||
            !float.IsFinite(value.MiterLimit) || value.MiterLimit is < 1 or > 100 ||
            value.CustomDashes.IsDefault || value.CustomDashes.Length > MaximumDashSegments)
            throw new InvalidDataException("Invalid outline dash, cap, join or miter limit (1–100).");
        foreach (var end in new[] { value.Begin, value.End })
            if (end is null || !Enum.IsDefined(end.Kind) || !Enum.IsDefined(end.Width) || !Enum.IsDefined(end.Length))
                throw new InvalidDataException("Invalid line-end kind or size.");
        foreach (var pair in value.CustomDashes)
            if (!float.IsFinite(pair.Dash) || !float.IsFinite(pair.Gap) || pair.Dash is < 0 or > 1000 || pair.Gap is < 0 or > 1000 || pair.Dash + pair.Gap <= 0)
                throw new InvalidDataException("Each custom dash/gap pair must be finite, between 0 and 1000 widths, and have positive total length.");
        if (value.Gradient is { } gradient) GradientModel.Validate(gradient);
    }
    public static void Validate(StrokeSettings value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Validate(value.Style);
        if (!float.IsFinite(value.Width) || value.Width is < 0 or > 1000) throw new InvalidDataException("Outline width must be 0–1000 slide units.");
        var color = value.Color;
        if (color is null || color.Length is not (7 or 9) || color[0] != '#' ||
            !uint.TryParse(color.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out _))
            throw new InvalidDataException("Use #RRGGBB or #AARRGGBB for the outline color.");
    }
    public static SlideShape Apply(SlideShape shape, StrokeSettings value)
    {
        if (!Supports(shape)) throw new InvalidOperationException("Use the table or chart editor for this object's outlines.");
        Validate(value);
        if (shape.Stroke == value.Color && shape.StrokeWidth == value.Width && Equivalent(Resolve(shape), value.Style)) return shape;
        return shape with { Stroke = value.Color, StrokeWidth = value.Width, Outline = value.Style };
    }
    public static ImmutableArray<StrokeDashSegment> ParseDashes(string text)
    {
        if (text is null || text.Length > 8192) throw new InvalidDataException("Custom dash input is too long.");
        var result = ImmutableArray.CreateBuilder<StrokeDashSegment>();
        foreach (var line in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (result.Count >= MaximumDashSegments || parts.Length != 2 ||
                !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var dash) ||
                !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var gap) ||
                !double.IsFinite(dash) || !double.IsFinite(gap) || dash is < 0 or > 1000 || gap is < 0 or > 1000 || dash + gap <= 0)
                throw new InvalidDataException("Enter up to 32 lines of dash gap, in outline-width multiples from 0–1000.");
            result.Add(new((float)dash, (float)gap));
        }
        var array = result.ToImmutable(); Validate(Default with { CustomDashes = array }); return array;
    }
    public static string FormatDashes(ImmutableArray<StrokeDashSegment> values) => string.Join("\n", values.Select(p =>
        p.Dash.ToString("R", CultureInfo.InvariantCulture) + " " + p.Gap.ToString("R", CultureInfo.InvariantCulture)));
    public static ImmutableArray<StrokeDashSegment> DashPattern(StrokeSpec value) => !value.CustomDashes.IsDefaultOrEmpty ? value.CustomDashes : value.Dash switch
    {
        StrokeDash.Dot => [new(1, 3)], StrokeDash.Dash => [new(4, 3)], StrokeDash.LongDash => [new(8, 3)],
        StrokeDash.DashDot => [new(4, 3), new(1, 3)], StrokeDash.LongDashDot => [new(8, 3), new(1, 3)],
        StrokeDash.LongDashDotDot => [new(8, 3), new(1, 3), new(1, 3)], StrokeDash.SystemDash => [new(3, 1)],
        StrokeDash.SystemDot => [new(1, 1)], StrokeDash.SystemDashDot => [new(3, 1), new(1, 1)],
        StrokeDash.SystemDashDotDot => [new(3, 1), new(1, 1), new(1, 1)], _ => []
    };
    public static (PointF Start, PointF End) Endpoints(SlideShape shape)
    {
        if (!IsLine(shape)) throw new ArgumentException("Line geometry requires Line or Arrow.", nameof(shape));
        var b = shape.Bounds; var direction = shape.LineDirection;
        return (new(direction?.FlipHorizontal == true ? b.Right : b.X, direction?.FlipVertical == true ? b.Bottom : b.Y),
            new(direction?.FlipHorizontal == true ? b.X : b.Right, direction?.FlipVertical == true ? b.Y : b.Bottom));
    }
    public static SlideShape Line(PointF start, PointF end, bool arrow = false) => new()
    {
        Kind = arrow ? ShapeKind.Arrow : ShapeKind.Line, Name = arrow ? "Arrow" : "Line", Bounds = RectF.Between(start, end),
        LineDirection = new(end.X < start.X, end.Y < start.Y), Fill = "#00000000", Stroke = "#243247", StrokeWidth = 2
    };
    public static float EndScale(LineEndSize size) => size switch { LineEndSize.Small => 2, LineEndSize.Large => 5, _ => 3 };
    /// <summary>Conservative recording/hit-test bleed, including marker size and miter spikes.</summary>
    public static float Outset(SlideShape shape)
    {
        var style = Resolve(shape); float width = shape.StrokeWidth;
        float result = Math.Max(2, width * (style.Join == StrokeJoin.Miter ? style.MiterLimit : 1));
        if (IsLine(shape))
        {
            float Marker(LineEnd end) => end.Kind == LineEndKind.None ? 0 : Math.Max(1, width) * Math.Max(EndScale(end.Width), EndScale(end.Length));
            result = Math.Max(result, Math.Max(Marker(style.Begin), Marker(style.End)));
            if (shape.Outline is null && shape.Kind == ShapeKind.Arrow) result = Math.Max(result, Math.Max(12, width * 4));
        }
        return result;
    }
    /// <summary>Hit testing includes the marker envelope. It intentionally does not pick individual gaps in dashed strokes.</summary>
    public static bool HitLine(SlideShape shape, PointF localPoint, float tolerance)
    {
        var (a, b) = Endpoints(shape); float dx = b.X - a.X, dy = b.Y - a.Y;
        float length = MathF.Sqrt(dx * dx + dy * dy);
        float distance = Math.Max(0, tolerance) + shape.StrokeWidth / 2;
        if (length <= .00001f) return MathF.Sqrt(MathF.Pow(localPoint.X - a.X, 2) + MathF.Pow(localPoint.Y - a.Y, 2)) <= distance;
        float x = ((localPoint.X - a.X) * dx + (localPoint.Y - a.Y) * dy) / length;
        float y = Math.Abs((-(localPoint.X - a.X) * dy + (localPoint.Y - a.Y) * dx) / length);
        if (x >= -distance && x <= length + distance && y <= distance) return true;
        var style = Resolve(shape);
        bool Marker(LineEnd end, float fromTip) => end.Kind != LineEndKind.None && fromTip >= -distance &&
            fromTip <= Math.Max(1, shape.StrokeWidth) * EndScale(end.Length) + distance &&
            y <= Math.Max(1, shape.StrokeWidth) * EndScale(end.Width) / 2 + distance;
        return Marker(style.Begin, x) || Marker(style.End, length - x);
    }
}
