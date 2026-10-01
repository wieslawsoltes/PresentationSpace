using System.Collections.Immutable;
using System.Globalization;

namespace PresentationSpace.Core;

/// <summary>Offset and opacity are fractions in [0,1]; Color is #RRGGBB. Equal offsets create hard edges.</summary>
public sealed record GradientStop(float Offset, string Color, float Opacity = 1);
/// <summary>Editable linear gradient in the local fill rectangle. Angle is clockwise in degrees.</summary>
public sealed record GradientFill
{
    public ImmutableArray<GradientStop> Stops { get; init; } = [new(0, "#D35230"), new(1, "#F6C6B6")];
    public float Angle { get; init; }
    public bool Scaled { get; init; } = true;
    public bool RotateWithShape { get; init; } = true;
}
public readonly record struct GradientVector(PointF Start, PointF End);

/// <summary>UI/renderer-independent validation, authoring and linear-fill geometry.</summary>
public static class GradientModel
{
    public const int MaximumStops = 64, MaximumDefinitionLength = 8192;
    public static void Validate(GradientFill gradient)
    {
        if (gradient is null || !float.IsFinite(gradient.Angle) || gradient.Angle < 0 || gradient.Angle >= 360 ||
            gradient.Stops.IsDefault || gradient.Stops.Length is < 2 or > MaximumStops)
            throw new InvalidDataException("A linear gradient requires 2–64 stops and an angle from 0 up to, but not including, 360 degrees.");
        float previous = -1;
        foreach (var stop in gradient.Stops)
        {
            if (stop is null || !float.IsFinite(stop.Offset) || stop.Offset < previous || stop.Offset is < 0 or > 1 ||
                !float.IsFinite(stop.Opacity) || stop.Opacity is < 0 or > 1 || !IsRgb(stop.Color))
                throw new InvalidDataException("Gradient stops must have ascending positions, #RRGGBB colors and opacity between 0 and 100%.");
            previous = stop.Offset;
        }
    }
    public static bool IsRgb(string? color) => color is { Length: 7 } && color[0] == '#' &&
        uint.TryParse(color.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out _);
    public static bool Supports(SlideShape shape) => shape.Kind is ShapeKind.Text or ShapeKind.Rectangle or ShapeKind.RoundRectangle or
        ShapeKind.Ellipse or ShapeKind.Triangle or ShapeKind.Diamond or ShapeKind.Image;
    public static SlideShape Apply(SlideShape shape, GradientFill? gradient)
    {
        ArgumentNullException.ThrowIfNull(shape);
        if (!Supports(shape)) throw new InvalidOperationException("Select a filled shape or picture. Table cells have independent fills.");
        if (gradient is not null) Validate(gradient);
        return shape with { FillGradient = gradient };
    }
    public static bool Equivalent(GradientFill? a, GradientFill? b) => ReferenceEquals(a, b) || a is not null && b is not null &&
        a.Angle == b.Angle && a.Scaled == b.Scaled && a.RotateWithShape == b.RotateWithShape && a.Stops.SequenceEqual(b.Stops);
    public static GradientFill Reverse(GradientFill gradient)
    {
        Validate(gradient);
        return gradient with { Stops = gradient.Stops.Reverse().Select(s => s with { Offset = 1 - s.Offset }).ToImmutableArray() };
    }
    public static GradientVector Vector(GradientFill gradient, RectF bounds, float shapeRotation = 0)
    {
        Validate(gradient);
        return VectorCore(gradient, bounds, shapeRotation);
    }
    // Geometry-only path is safe to reuse after snapshot validation.
    private static GradientVector VectorCore(GradientFill gradient, RectF bounds, float shapeRotation = 0)
    {
        if (!float.IsFinite(bounds.X) || !float.IsFinite(bounds.Y) || !float.IsFinite(bounds.Right) || !float.IsFinite(bounds.Bottom) ||
            bounds.Width <= 0 || bounds.Height <= 0 || bounds.Width > 100000 || bounds.Height > 100000 || !float.IsFinite(shapeRotation))
            throw new ArgumentOutOfRangeException(nameof(bounds));
        double angle = gradient.Angle * Math.PI / 180;
        double x = Math.Cos(angle) * (gradient.Scaled ? bounds.Width : 1), y = Math.Sin(angle) * (gradient.Scaled ? bounds.Height : 1);
        if (!gradient.RotateWithShape)
        {
            double r = -shapeRotation * Math.PI / 180, c = Math.Cos(r), s = Math.Sin(r);
            (x, y) = (x * c - y * s, x * s + y * c);
        }
        double length = Math.Sqrt(x * x + y * y); x /= length; y /= length;
        double half = (Math.Abs(x) * bounds.Width + Math.Abs(y) * bounds.Height) / 2;
        var center = bounds.Center;
        return new(new((float)(center.X - half * x), (float)(center.Y - half * y)), new((float)(center.X + half * x), (float)(center.Y + half * y)));
    }
    /// <summary>One 'position% #RRGGBB opacity%' per line, invariant culture. CR/LF/CRLF accepted.</summary>
    public static ImmutableArray<GradientStop> ParseStops(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > MaximumDefinitionLength) throw new InvalidDataException("Gradient definition is too long.");
        var stops = ImmutableArray.CreateBuilder<GradientStop>();
        foreach (string line in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (stops.Count >= MaximumStops) throw new InvalidDataException("At most 64 gradient stops are supported.");
            var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            float Percent(string value)
            {
                if (!float.TryParse(value.EndsWith('%') ? value[..^1] : value, NumberStyles.Float, CultureInfo.InvariantCulture, out float n) || !float.IsFinite(n))
                    throw new InvalidDataException("Use finite, invariant percentages for gradient stops.");
                return n / 100;
            }
            if (fields.Length is < 2 or > 3) throw new InvalidDataException("Each stop is: position% #RRGGBB [opacity%].");
            stops.Add(new(Percent(fields[0]), fields[1].ToUpperInvariant(), fields.Length == 3 ? Percent(fields[2]) : 1));
        }
        var result = stops.ToImmutable(); Validate(new() { Stops = result }); return result;
    }
    public static string FormatStops(ImmutableArray<GradientStop> stops) => string.Join('\n', stops.Select(s =>
        FormattableString.Invariant($"{s.Offset * 100:0.#####} {s.Color} {s.Opacity * 100:0.#####}")));
}
