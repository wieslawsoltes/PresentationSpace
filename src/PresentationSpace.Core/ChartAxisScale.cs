namespace PresentationSpace.Core;

/// <summary>Normalized axis geometry avoids underflow for subnormal but finite chart values.</summary>
public readonly record struct ChartAxisScale(double Magnitude, double Minimum, double Maximum, double Step)
{
    public double Fraction(double value) => (value / Magnitude - Minimum) / (Maximum - Minimum);

    public static ChartAxisScale Create(double minimum, double maximum, bool percentage = false)
    {
        if (!double.IsFinite(minimum) || !double.IsFinite(maximum) || minimum > maximum)
            throw new ArgumentOutOfRangeException(nameof(minimum), "Axis bounds must be finite and ordered.");
        if (percentage) return new(1, 0, 1, .25);
        double magnitude = Math.Max(Math.Abs(minimum), Math.Abs(maximum));
        if (magnitude == 0) return new(1, 0, 1, .25);
        double low = Math.Min(0, minimum / magnitude), high = Math.Max(0, maximum / magnitude);
        double span = high - low;
        double step = Math.Pow(10, Math.Floor(Math.Log10(span / 4)));
        double target = span / (4 * step);
        step *= target <= 1 ? 1 : target <= 2 ? 2 : target <= 5 ? 5 : 10;
        return new(magnitude, Math.Floor(low / step) * step, Math.Ceiling(high / step) * step, step);
    }

    public IEnumerable<(double Value, double Fraction)> Ticks()
    {
        // Tick placement stays normalized; tiny labels may round to the same representable double.
        for (int i = 0; i <= 12; i++)
        {
            double value = Minimum + i * Step;
            if (value > Maximum + Step * .01) yield break;
            yield return (value * Magnitude, (value - Minimum) / (Maximum - Minimum));
        }
    }
}
