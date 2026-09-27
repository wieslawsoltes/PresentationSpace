using System.Globalization;
using PresentationSpace.Core;
using SkiaSharp;

namespace PresentationSpace.Rendering.Skia;

public sealed partial class SlideRenderer
{
    private void DrawChart(SKCanvas canvas, SlideShape shape) => RenderChart(canvas, ChartModel.Get(shape), shape.Bounds, shape.TextStyle);

    /// <summary>Draw a categorical chart independently of a presentation or editor.</summary>
    public void RenderChart(SKCanvas canvas, ChartSpec chart, RectF bounds, TextStyle textStyle)
    {
        ChartModel.Validate(chart);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        int saveCount = canvas.SaveCount;
        canvas.Save();
        try
        {
            canvas.ClipRect(new(bounds.X, bounds.Y, bounds.Right, bounds.Bottom));
            canvas.Translate(bounds.X, bounds.Y); canvas.Scale(bounds.Width / 640, bounds.Height / 360);
            using (var background = new SKPaint { Color = Color(chart.Background) }) canvas.DrawRect(0, 0, 640, 360, background);
            var style = textStyle with { FontSize = 12, Bold = false, Bullets = false, Alignment = ParagraphAlignment.Center, VerticalAlignment = Core.VerticalAlignment.Middle };
            void Text(string value, RectF rectangle, ParagraphAlignment alignment = ParagraphAlignment.Center, bool bold = false, float size = 12) => DrawText(canvas, value, rectangle, style with { Alignment = alignment, Bold = bold, FontSize = size }, 2);
            float top = string.IsNullOrEmpty(chart.Title) ? 12 : 42;
            if (top > 12) Text(chart.Title, new(12, 3, 616, 36), bold: true, size: 18);
            bool circular = ChartModel.IsCircular(chart.Kind);
            int legendCount = circular ? chart.Categories.Length : chart.Series.Length;
            int shown = Math.Min(legendCount, 16), legendRows = (shown + 3) / 4;
            float legendHeight = chart.ShowLegend ? legendRows * 19 + (legendCount > shown ? 18 : 0) + 8 : 0;
            var area = new RectF(circular ? 16 : chart.Kind == ChartKind.Bar ? 100 : 66, top,
                circular ? 608 : chart.Kind == ChartKind.Bar ? 508 : 550, Math.Max(110, 360 - top - legendHeight - (circular ? 8 : 42)));
            using var paint = new SKPaint { IsAntialias = true };
            if (circular) DrawCircular(); else DrawCartesian();
            if (chart.ShowLegend)
            {
                float y = 360 - legendHeight + 4;
                for (int i = 0; i < shown; i++)
                {
                    float x = 12 + i % 4 * 157, row = y + i / 4 * 19;
                    paint.Color = Color(circular ? ChartModel.PointColor(i) : chart.Series[i].Color);
                    canvas.DrawRect(x, row + 5, 9, 9, paint);
                    Text(circular ? chart.Categories[i] : chart.Series[i].Name, new(x + 12, row, 138, 19), ParagraphAlignment.Left, size: 11);
                }
                if (legendCount > shown) Text($"+{legendCount - shown} more {(circular ? "categories" : "series")}", new(12, y + legendRows * 19, 616, 18), size: 10);
            }

            void DrawCircular()
            {
                var values = chart.Series[0].Values; double total = values.Sum(v => v ?? 0);
                float radius = Math.Min(area.Width, area.Height) * .46f, cx = area.Center.X, cy = area.Center.Y;
                var circle = new SKRect(cx - radius, cy - radius, cx + radius, cy + radius);
                if (total <= 0) { paint.Color = Color("#E4E7EC"); paint.Style = SKPaintStyle.Stroke; paint.StrokeWidth = 2; canvas.DrawCircle(cx, cy, radius, paint); paint.Style = SKPaintStyle.Fill; Text("No positive data", new(cx - 100, cy - 15, 200, 30)); return; }
                float start = -90;
                for (int i = 0; i < values.Length; i++)
                {
                    double value = values[i] ?? 0; if (value <= 0) continue;
                    float sweep = (float)(value / total * 360);
                    using var wedge = new SKPath();
                    if (sweep >= 359.999f)
                    {
                        wedge.AddOval(circle);
                        if (chart.Kind == ChartKind.Doughnut)
                        {
                            float inner = radius * chart.HoleSize / 100;
                            wedge.FillType = SKPathFillType.EvenOdd;
                            wedge.AddOval(new(cx - inner, cy - inner, cx + inner, cy + inner));
                        }
                    }
                    else if (chart.Kind == ChartKind.Pie)
                    { wedge.MoveTo(cx, cy); wedge.ArcTo(circle, start, sweep, false); wedge.Close(); }
                    else
                    {
                        float inner = radius * chart.HoleSize / 100;
                        var inside = new SKRect(cx - inner, cy - inner, cx + inner, cy + inner);
                        double angle = start * Math.PI / 180;
                        wedge.MoveTo(cx + radius * (float)Math.Cos(angle), cy + radius * (float)Math.Sin(angle));
                        wedge.ArcTo(circle, start, sweep, false);
                        angle = (start + sweep) * Math.PI / 180;
                        wedge.LineTo(cx + inner * (float)Math.Cos(angle), cy + inner * (float)Math.Sin(angle));
                        wedge.ArcTo(inside, start + sweep, -sweep, false); wedge.Close();
                    }
                    paint.Color = Color(ChartModel.PointColor(i)); canvas.DrawPath(wedge, paint);
                    if (chart.ShowValues && sweep >= 10 && values.Length <= 50)
                    {
                        double a = (start + sweep / 2) * Math.PI / 180;
                        float r = chart.Kind == ChartKind.Pie ? radius * .68f : radius * (1 + chart.HoleSize / 100f) / 2;
                        DrawText(canvas, (value / total).ToString("0%", CultureInfo.InvariantCulture), new(cx + r * (float)Math.Cos(a) - 26, cy + r * (float)Math.Sin(a) - 12, 52, 24), style with { Color = "#FFFFFF", Bold = true, FontSize = 12 }, 1);
                    }
                    start += sweep;
                }
            }

            void DrawCartesian()
            {
                var intervals = ChartModel.Intervals(chart);
                double low = Math.Min(0, intervals.IsEmpty ? 0 : intervals.Min(v => Math.Min(v.Start, v.End)));
                double high = Math.Max(0, intervals.IsEmpty ? 1 : intervals.Max(v => Math.Max(v.Start, v.End)));
                var axis = ChartAxisScale.Create(low, high, chart.Grouping == ChartGrouping.PercentStacked);
                bool horizontal = chart.Kind == ChartKind.Bar;
                float FractionPosition(double fraction) => horizontal ? area.X + (float)fraction * area.Width : area.Bottom - (float)fraction * area.Height;
                float Position(double value) => FractionPosition(axis.Fraction(value));
                using var grid = new SKPaint { Color = Color("#DEE3EB"), StrokeWidth = 1, IsAntialias = true };
                foreach (var tick in axis.Ticks())
                {
                    float position = FractionPosition(tick.Fraction);
                    string label = chart.Grouping == ChartGrouping.PercentStacked ? tick.Value.ToString("0%", CultureInfo.InvariantCulture) : ChartNumber(tick.Value);
                    if (horizontal) { canvas.DrawLine(position, area.Y, position, area.Bottom, grid); Text(label, new(position - 28, area.Bottom + 3, 56, 22), size: 10); }
                    else { canvas.DrawLine(area.X, position, area.Right, position, grid); Text(label, new(0, position - 11, area.X - 6, 22), ParagraphAlignment.Right, size: 10); }
                }
                int count = chart.Categories.Length;
                float slot = (horizontal ? area.Height : area.Width) / count;
                int stride = Math.Max(1, (int)Math.Ceiling(count / (horizontal ? 12d : 10d)));
                for (int i = 0; i < count; i += stride)
                {
                    if (horizontal) Text(chart.Categories[i], new(0, area.Y + i * slot, area.X - 7, Math.Max(12, slot)), ParagraphAlignment.Right, size: 11);
                    else Text(chart.Categories[i], new(area.X + (i + .5f) * slot - slot * stride / 2, area.Bottom + 4, slot * stride, 32), size: 11);
                }
                canvas.Save(); canvas.ClipRect(new(area.X, area.Y, area.Right, area.Bottom));
                if (chart.Kind is ChartKind.Column or ChartKind.Bar)
                {
                    int slots = chart.Grouping == ChartGrouping.Clustered ? chart.Series.Length : 1;
                    foreach (var item in intervals)
                    {
                        float offset = item.Category * slot + slot * .15f + (slots > 1 ? item.Series * slot * .7f / slots : 0), size = slot * .7f / slots;
                        float from = Position(item.Start), to = Position(item.End);
                        paint.Color = Color(chart.Series[item.Series].Color);
                        if (horizontal) canvas.DrawRect(Math.Min(from, to), area.Y + offset, Math.Abs(to - from), size, paint);
                        else canvas.DrawRect(area.X + offset, Math.Min(from, to), size, Math.Abs(to - from), paint);
                    }
                }
                else
                {
                    for (int series = 0; series < chart.Series.Length; series++)
                    {
                        using var line = new SKPaint { Color = Color(chart.Series[series].Color), StrokeWidth = 2.5f, Style = SKPaintStyle.Stroke, IsAntialias = true, StrokeJoin = SKStrokeJoin.Round };
                        var segment = new List<SKPoint>();
                        void Flush()
                        {
                            if (segment.Count == 0) return;
                            using var path = new SKPath(); path.MoveTo(segment[0]); foreach (var point in segment.Skip(1)) path.LineTo(point);
                            if (chart.Kind == ChartKind.Area)
                            {
                                path.LineTo(segment[^1].X, Position(0)); path.LineTo(segment[0].X, Position(0)); path.Close();
                                paint.Color = Color(chart.Series[series].Color); canvas.DrawPath(path, paint);
                            }
                            else canvas.DrawPath(path, line);
                            if (chart.Kind == ChartKind.Line && count <= 100) foreach (var point in segment) { paint.Color = line.Color; canvas.DrawCircle(point, 3, paint); }
                            segment.Clear();
                        }
                        for (int i = 0; i < count; i++)
                        {
                            var value = chart.Series[series].Values[i];
                            if (value is null && chart.Blanks != ChartBlankMode.Zero) { if (chart.Blanks == ChartBlankMode.Gap) Flush(); continue; }
                            segment.Add(new(area.X + (i + .5f) * slot, Position(value ?? 0)));
                        }
                        Flush();
                    }
                }
                canvas.Restore();
                if (chart.ShowValues && count * chart.Series.Length <= 50)
                {
                    foreach (var item in intervals)
                    {
                        int slots = chart.Kind is ChartKind.Column or ChartKind.Bar && chart.Grouping == ChartGrouping.Clustered ? chart.Series.Length : 1;
                        float center = (item.Category + .15f) * slot + (slots > 1 ? item.Series * slot * .7f / slots : 0) + slot * .35f / slots;
                        var raw = chart.Series[item.Series].Values[item.Category] ?? 0;
                        string label = ChartNumber(raw);
                        if (horizontal) Text(label, new(Position(item.End) - (raw < 0 ? 52 : 0), area.Y + center - 10, 52, 20), size: 10);
                        else Text(label, new(area.X + center - 30, Position(item.End) + (raw < 0 ? 1 : -21), 60, 20), size: 10);
                    }
                }
            }
        }
        finally { canvas.RestoreToCount(saveCount); }
    }

    private static string ChartNumber(double value) => value.ToString(Math.Abs(value) is >= 1e7 or > 0 and < .001 ? "0.##E+0" : "0.###", CultureInfo.InvariantCulture);
}
