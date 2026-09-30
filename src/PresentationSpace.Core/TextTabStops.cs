using System.Collections.Immutable;
using System.Globalization;

namespace PresentationSpace.Core;

public enum TextTabAlignment { Left, Center, Right, Decimal }
public readonly record struct TextTabStop(float Position, TextTabAlignment Alignment = TextTabAlignment.Left);
public readonly record struct TextTabPlacement(float Stop, float Start, TextTabAlignment Alignment, bool Custom, bool Clamped);

/// <summary>Immutable, bounded custom tab definitions and pure placement arithmetic. Coordinates are 96-DPI slide units.</summary>
public static class TextTabStops
{
    public const int MaximumCount = 32;
    public static void Validate(ImmutableArray<TextTabStop> stops)
    {
        if (stops.IsDefault || stops.Length > MaximumCount) throw new InvalidDataException("Use an initialized array of at most 32 tab stops.");
        float previous = -1;
        foreach (var stop in stops)
        {
            if (!float.IsFinite(stop.Position) || stop.Position < 0 || stop.Position > 10000 || stop.Position <= previous || !Enum.IsDefined(stop.Alignment))
                throw new InvalidDataException("Tab positions must be strictly increasing, finite and between 0 and 10,000 slide units.");
            previous = stop.Position;
        }
    }
    public static ImmutableArray<TextTabStop> Scale(ImmutableArray<TextTabStop> stops, float factor)
    {
        Validate(stops);
        if (!float.IsFinite(factor) || factor <= 0) throw new ArgumentOutOfRangeException(nameof(factor));
        if (stops.IsEmpty || factor == 1) return stops;
        var result = stops.Select(s => s with { Position = s.Position * factor }).ToImmutableArray();
        Validate(result); return result;
    }
    /// <summary>First custom stop strictly after the caret, or the next regular interval after all custom stops.</summary>
    public static TextTabPlacement Place(ImmutableArray<TextTabStop> stops, float caret, float interval, float fieldWidth = 0, float? decimalOffset = null)
    {
        Validate(stops);
        if (!float.IsFinite(caret) || !float.IsFinite(interval) || interval <= 0 || !float.IsFinite(fieldWidth) || fieldWidth < 0 ||
            decimalOffset is { } offset && (!float.IsFinite(offset) || offset < 0 || offset > fieldWidth)) throw new ArgumentOutOfRangeException(nameof(caret));
        int low = 0, high = stops.Length;
        while (low < high) { int middle = low + (high - low) / 2; if (stops[middle].Position <= caret) low = middle + 1; else high = middle; }
        if (low == stops.Length)
        {
            // Compute in double and ensure even a very small interval advances a large float caret.
            double target = (Math.Floor((double)caret / interval) + 1) * interval;
            float stop = (float)target;
            if (stop <= caret) stop = MathF.BitIncrement(caret);
            if (!float.IsFinite(stop)) throw new ArgumentOutOfRangeException(nameof(caret));
            return new(stop, stop, TextTabAlignment.Left, false, false);
        }
        var tab = stops[low];
        float desired = tab.Position - (tab.Alignment switch {
            TextTabAlignment.Center => fieldWidth / 2, TextTabAlignment.Right => fieldWidth,
            TextTabAlignment.Decimal => decimalOffset ?? fieldWidth, _ => 0 });
        return new(tab.Position, Math.Max(caret, desired), tab.Alignment, true, desired < caret);
    }
    /// <summary>One "position alignment" per line, with invariant decimal numbers; no partial result on invalid input.</summary>
    public static ImmutableArray<TextTabStop> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > 4096) throw new InvalidDataException("Tab definitions exceed 4,096 characters.");
        var stops = ImmutableArray.CreateBuilder<TextTabStop>();
        // WinUI/Uno Skia can expose CR-only newlines even when the browser input
        // contains LF. ReadLine accepts CR, LF and CRLF without joining definitions.
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
        {
            var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length == 0) continue;
            if (stops.Count == MaximumCount) throw new InvalidDataException("Use at most 32 tab stops.");
            if (fields.Length is < 1 or > 2 || !float.TryParse(fields[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float position))
                throw new InvalidDataException("Each tab needs a position followed by Left, Center, Right or Decimal.");
            var alignment = TextTabAlignment.Left;
            if (fields.Length == 2)
                alignment = fields[1].ToLowerInvariant() switch {
                    "left" => TextTabAlignment.Left, "center" => TextTabAlignment.Center,
                    "right" => TextTabAlignment.Right, "decimal" => TextTabAlignment.Decimal,
                    _ => throw new InvalidDataException("Use Left, Center, Right or Decimal alignment.") };
            stops.Add(new(position, alignment));
        }
        var result = stops.ToImmutable(); Validate(result); return result;
    }
    public static string Format(ImmutableArray<TextTabStop> stops)
    {
        Validate(stops);
        return string.Join("\n", stops.Select(s => s.Position.ToString("R", CultureInfo.InvariantCulture) + " " + s.Alignment));
    }
}
