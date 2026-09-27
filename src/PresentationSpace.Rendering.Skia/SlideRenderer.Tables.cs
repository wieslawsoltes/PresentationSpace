using PresentationSpace.Core;
using SkiaSharp;

namespace PresentationSpace.Rendering.Skia;

public sealed partial class SlideRenderer
{
    private readonly Dictionary<Guid, (TableSpec Spec, RectF Bounds, TableLayout Layout)> _tableLayouts = [];
    private void DrawTable(SKCanvas canvas, SlideShape shape)
    {
        var table = TableModel.Get(shape);
        if (!_tableLayouts.TryGetValue(shape.Id, out var entry) || !ReferenceEquals(entry.Spec, table) || entry.Bounds != shape.Bounds)
        {
            if (_tableLayouts.Count >= 32) _tableLayouts.Clear();
            entry = (table, shape.Bounds, new TableLayout(table, shape.Bounds)); _tableLayouts[shape.Id] = entry;
        }
        var layout = entry.Layout;
        var borders = new List<(TableBorder Border, SKPoint Start, SKPoint End)>();
        using var paint = new SKPaint { IsAntialias = true }; canvas.Save();
        try
        {
            canvas.ClipRect(new(shape.Bounds.X, shape.Bounds.Y, shape.Bounds.Right, shape.Bounds.Bottom));
            foreach (var cell in table.Cells)
            {
                var b = layout.Bounds(cell); var rect = new SKRect(b.X, b.Y, b.Right, b.Bottom);
                if (!canvas.LocalClipBounds.IntersectsWith(rect)) continue;
                paint.Color = Color(TableModel.Fill(table, cell)); canvas.DrawRect(rect, paint);
                var textBounds = new RectF(b.X + cell.MarginLeft, b.Y + cell.MarginTop, b.Width - cell.MarginLeft - cell.MarginRight, b.Height - cell.MarginTop - cell.MarginBottom);
                if (textBounds.Width > 0 && textBounds.Height > 0 && cell.Text.Length > 0)
                    DrawRichText(canvas, TableModel.TextShape(table, cell, textBounds), 0);
                borders.Add((cell.Left, new(b.X, b.Y), new(b.X, b.Bottom)));
                borders.Add((cell.Right, new(b.Right, b.Y), new(b.Right, b.Bottom)));
                borders.Add((cell.Top, new(b.X, b.Y), new(b.Right, b.Y)));
                borders.Add((cell.Bottom, new(b.X, b.Bottom), new(b.Right, b.Bottom)));
            }
            // Paint after every fill so a neighboring cell cannot erase the border. Wider borders win.
            foreach (var (border, start, end) in borders.OrderBy(b => b.Border.Width))
            {
                if (border.Width <= 0 || Color(border.Color).Alpha == 0) continue;
                using var line = new SKPaint { IsAntialias = true, Color = Color(border.Color), Style = SKPaintStyle.Stroke, StrokeWidth = border.Width };
                using var dash = border.Dash switch { TableBorderDash.Dash => SKPathEffect.CreateDash([4 * border.Width, 3 * border.Width], 0), TableBorderDash.Dot => SKPathEffect.CreateDash([border.Width, 2 * border.Width], 0), _ => null };
                line.PathEffect = dash; canvas.DrawLine(start, end, line);
            }
        }
        finally { canvas.Restore(); }
    }
}
