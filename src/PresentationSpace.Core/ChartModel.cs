using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace PresentationSpace.Core;

public enum ChartKind { Column, Bar, Line, Area, Pie, Doughnut }
public enum ChartGrouping { Clustered, Stacked, PercentStacked }
public enum ChartBlankMode { Gap, Zero, Span }

public sealed record ChartSeries
{
    public string Name { get; init; } = "Series 1";
    public string Color { get; init; } = "#D35230";
    public ImmutableArray<double?> Values { get; init; } = [];
}

/// <summary>Immutable categorical chart data. Null values are missing data, never implicit zeroes.</summary>
public sealed record ChartSpec
{
    public ChartKind Kind { get; init; }
    public ChartGrouping Grouping { get; init; }
    public ChartBlankMode Blanks { get; init; }
    public string Title { get; init; } = "";
    public string Background { get; init; } = "#FFFFFF";
    public bool ShowLegend { get; init; } = true;
    public bool ShowValues { get; init; }
    public int HoleSize { get; init; } = 55;
    public ImmutableArray<string> Categories { get; init; } = [];
    public ImmutableArray<ChartSeries> Series { get; init; } = [];
}

public readonly record struct ChartInterval(int Series, int Category, double Start, double End);

public static class ChartModel
{
    public const int MaxSeries = 32, MaxCategories = 10000, MaxValues = 100000;
    public static readonly ImmutableArray<string> Palette = ["#D35230", "#4472C4", "#70AD47", "#8064A2", "#FFC000", "#00A6A6", "#C550A0", "#636B75"];
    public static bool IsCircular(ChartKind kind) => kind is ChartKind.Pie or ChartKind.Doughnut;
    public static string PointColor(int index) => Palette[Math.Abs(index % Palette.Length)];

    /// <summary>Adapts 0.1/0.2 chart fields without mutating the source document.</summary>
    public static ChartSpec Get(SlideShape shape)
    {
        if (shape.Chart is { } chart) return chart;
        var values = shape.Values.IsDefaultOrEmpty ? ImmutableArray.Create(42f, 68f, 54f, 89f) : shape.Values;
        return new()
        {
            ShowLegend = false, ShowValues = true, Background = "#00000000",
            Categories = values.Select((_, i) => i < shape.Labels.Length ? shape.Labels[i] : (i + 1).ToString(CultureInfo.InvariantCulture)).ToImmutableArray(),
            Series = [new() { Name = shape.Name, Color = shape.Fill, Values = values.Select(v => (double?)v).ToImmutableArray() }]
        };
    }

    public static SlideShape Apply(SlideShape shape, ChartSpec chart)
    {
        Validate(chart);
        return shape with
        {
            Kind = ShapeKind.Chart, Chart = chart,
            // Compatibility projections are not authoritative; new code consumes Get().
            Values = chart.Series[0].Values.Select(v => (float)(v ?? 0)).ToImmutableArray(),
            Labels = chart.Categories, Fill = chart.Series[0].Color
        };
    }

    public static void Validate(ChartSpec c)
    {
        ArgumentNullException.ThrowIfNull(c);
        if (!Enum.IsDefined(c.Kind) || !Enum.IsDefined(c.Grouping) || !Enum.IsDefined(c.Blanks)) throw new InvalidDataException("Unknown chart type, grouping or blank-data policy.");
        if (c.Title is null || c.Title.Length > 32767 || !ValidColor(c.Background) || c.HoleSize is < 10 or > 90) throw new InvalidDataException("Invalid chart title or doughnut hole size.");
        if (c.Categories.IsDefaultOrEmpty || c.Categories.Length > MaxCategories || c.Categories.Any(x => x is null || x.Length > 32767)) throw new InvalidDataException("Charts require 1–10,000 category labels, each at most 32,767 characters.");
        if (c.Series.IsDefaultOrEmpty || c.Series.Length > MaxSeries || (long)c.Series.Length * c.Categories.Length > MaxValues) throw new InvalidDataException("Charts support at most 32 series and 100,000 values.");
        if (c.Grouping != ChartGrouping.Clustered && c.Kind is not (ChartKind.Column or ChartKind.Bar)) throw new InvalidDataException("Stacking is supported for column and bar charts only.");
        if (IsCircular(c.Kind) && c.Series.Length != 1) throw new InvalidDataException("Pie and doughnut charts require exactly one series. No series was removed.");
        foreach (var series in c.Series)
        {
            if (series is null || series.Name is null || series.Name.Length > 32767 || !ValidColor(series.Color) || series.Values.IsDefault || series.Values.Length != c.Categories.Length) throw new InvalidDataException("Series names, colors or category/value lengths are invalid.");
            foreach (var value in series.Values)
            {
                if (value is not { } v) continue;
                if (!double.IsFinite(v) || Math.Abs(v) > 1e30) throw new InvalidDataException("Chart values must be finite and between -1e30 and 1e30.");
                if ((IsCircular(c.Kind) || c.Grouping == ChartGrouping.PercentStacked) && v < 0) throw new InvalidDataException("Pie, doughnut and 100% stacked charts require non-negative values.");
            }
        }
    }

    private static bool ValidColor(string? value) => value is { Length: 7 or 9 } && value[0] == '#' && value.AsSpan(1).IndexOfAnyExcept("0123456789abcdefABCDEF") < 0;

    /// <summary>Shared numeric stacking rules for renderers, exports and independent tests.</summary>
    public static ImmutableArray<ChartInterval> Intervals(ChartSpec chart)
    {
        Validate(chart);
        var output = ImmutableArray.CreateBuilder<ChartInterval>();
        for (int category = 0; category < chart.Categories.Length; category++)
        {
            double positive = 0, negative = 0;
            double total = chart.Grouping == ChartGrouping.PercentStacked ? chart.Series.Sum(s => s.Values[category] ?? 0) : 1;
            for (int series = 0; series < chart.Series.Length; series++)
            {
                var original = chart.Series[series].Values[category];
                if (original is null && chart.Blanks != ChartBlankMode.Zero) continue;
                double value = original ?? 0, start = 0;
                if (chart.Grouping == ChartGrouping.PercentStacked) value = total > 0 ? value / total : 0;
                if (chart.Grouping != ChartGrouping.Clustered)
                {
                    start = value < 0 ? negative : positive;
                    if (value < 0) negative += value; else positive += value;
                }
                output.Add(new(series, category, start, start + value));
            }
        }
        return output.ToImmutable();
    }
}

/// <summary>Quoted tab-separated data compatible with spreadsheet copy/paste. No formulas execute.</summary>
public static class ChartTabularData
{
    public const int MaxCharacters = 2 * 1024 * 1024;
    public static string Format(ChartSpec chart)
    {
        ChartModel.Validate(chart);
        static string Cell(string text) => text.IndexOfAny(['\t', '\r', '\n', '"']) < 0 ? text : "\"" + text.Replace("\"", "\"\"") + "\"";
        var builder = new StringBuilder();
        builder.Append("Category");
        foreach (var series in chart.Series) builder.Append('\t').Append(Cell(series.Name));
        for (int i = 0; i < chart.Categories.Length; i++)
        {
            builder.Append('\n').Append(Cell(chart.Categories[i]));
            foreach (var series in chart.Series) builder.Append('\t').Append(series.Values[i]?.ToString("R", CultureInfo.InvariantCulture) ?? "");
        }
        return builder.ToString();
    }

    public static ChartSpec Parse(string text, ChartSpec template)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > MaxCharacters) throw new InvalidDataException("Chart data input is limited to 2 MB of characters.");
        var rows = new List<string[]>(); var cells = new List<string>(); var cell = new StringBuilder();
        bool quoted = false, closed = false, started = false;
        void EndCell()
        {
            cells.Add(cell.ToString()); cell.Clear(); closed = false; started = false;
            if (cells.Count > ChartModel.MaxSeries + 1) throw new InvalidDataException("At most 32 series are supported.");
        }
        void EndRow()
        {
            EndCell(); rows.Add(cells.ToArray()); cells.Clear();
            if (rows.Count > ChartModel.MaxCategories + 1) throw new InvalidDataException("At most 10,000 category rows are supported.");
        }
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (quoted)
            {
                if (ch == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; } else { quoted = false; closed = true; } }
                else cell.Append(ch);
            }
            else if (ch == '\t') EndCell();
            else if (ch is '\r' or '\n') { if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++; EndRow(); }
            else if (closed) throw new InvalidDataException("Unexpected text after a closing quote in chart data.");
            else if (ch == '"' && !started) { quoted = true; started = true; }
            else { cell.Append(ch); started = true; }
            if (cell.Length > 32767) throw new InvalidDataException("Chart cells are limited to 32,767 characters.");
        }
        if (quoted) throw new InvalidDataException("Unclosed quote in chart data.");
        if (started || closed || cells.Count > 0 || cell.Length > 0) EndRow();
        if (rows.Count < 2 || rows[0].Length < 2) throw new InvalidDataException("Include a header (Category and series names) and at least one data row, separated by tabs.");
        int columns = rows[0].Length;
        if (rows.Any(r => r.Length != columns)) throw new InvalidDataException("Every row must have the same number of tab-separated cells. Empty numeric cells represent missing data.");
        var seriesList = ImmutableArray.CreateBuilder<ChartSeries>();
        for (int col = 1; col < columns; col++)
        {
            var values = ImmutableArray.CreateBuilder<double?>();
            for (int row = 1; row < rows.Count; row++)
            {
                string value = rows[row][col].Trim();
                if (value.Length == 0) values.Add(null);
                else if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) && double.IsFinite(number)) values.Add(number);
                else throw new InvalidDataException($"Invalid number at row {row + 1}, column {col + 1}. Use a dot for decimals.");
            }
            seriesList.Add(new() { Name = rows[0][col], Color = col <= template.Series.Length ? template.Series[col - 1].Color : ChartModel.PointColor(col - 1), Values = values.ToImmutable() });
        }
        var result = template with { Categories = rows.Skip(1).Select(r => r[0]).ToImmutableArray(), Series = seriesList.ToImmutable() };
        ChartModel.Validate(result); return result;
    }
}
