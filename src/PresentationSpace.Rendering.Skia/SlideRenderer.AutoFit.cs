using System.Collections.Immutable;
using PresentationSpace.Core;

namespace PresentationSpace.Rendering.Skia;

public sealed record TableAutoFitResult(TableSpec Table, float Height);

public sealed partial class SlideRenderer
{
    /// <summary>Content-sized rows, including multi-row spans. Uses the exact renderer's supported line layout.</summary>
    public TableAutoFitResult AutoFitTableRows(TableSpec table, float width, float minimumRowHeight = 24)
    {
        TableModel.Validate(table);
        if (!float.IsFinite(width) || width <= 0 || width > 100000 || !float.IsFinite(minimumRowHeight) || minimumRowHeight < .01f || minimumRowHeight > 100000)
            throw new ArgumentOutOfRangeException(nameof(width));
        var layout = new TableLayout(table, new(0, 0, width, 1));
        var rows = Enumerable.Repeat(minimumRowHeight, table.RowCount).ToArray();
        // Shorter-span constraints first; later increases can never invalidate an earlier minimum.
        foreach (var cell in table.Cells.OrderBy(cell => cell.RowSpan))
        {
            var bounds = layout.Bounds(cell);
            float innerWidth = Math.Max(1, bounds.Width - cell.MarginLeft - cell.MarginRight);
            float textHeight = MeasureRichTextHeight(TableModel.TextShape(table, cell), innerWidth);
            float required = textHeight + cell.MarginTop + cell.MarginBottom + Math.Max(cell.Top.Width, cell.Bottom.Width);
            double allocated = 0; for (int row = cell.Row; row < cell.Row + cell.RowSpan; row++) allocated += rows[row];
            if (required <= allocated) continue;
            float extra = (float)((required - allocated) / cell.RowSpan);
            for (int row = cell.Row; row < cell.Row + cell.RowSpan; row++) rows[row] += extra;
        }
        double height = rows.Sum(row => (double)row);
        if (!double.IsFinite(height) || height > 100000) throw new InvalidOperationException("The fitted table exceeds the maximum shape height.");
        var result = table with { RowHeights = rows.ToImmutableArray() }; TableModel.Validate(result);
        return new(result, (float)height);
    }
}
