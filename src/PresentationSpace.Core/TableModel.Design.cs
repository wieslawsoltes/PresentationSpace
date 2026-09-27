using System.Collections.Immutable;

namespace PresentationSpace.Core;

public enum TableBorderScope { All, Outside, Inside, InsideHorizontal, InsideVertical, Top, Bottom, Left, Right, None }
public enum TableStylePreset { Office, Blue, Green, Slate }

public static partial class TableModel
{
    /// <summary>Apply a built-in palette without changing content, geometry, merges or direct cell overrides.</summary>
    public static TableSpec ApplyStyle(TableSpec table, TableStylePreset preset)
    {
        Validate(table);
        var (accent, band) = preset switch
        {
            TableStylePreset.Office => ("#D35230", "#FBECE6"),
            TableStylePreset.Blue => ("#285EA8", "#EAF0FA"),
            TableStylePreset.Green => ("#287D61", "#EAF4EF"),
            TableStylePreset.Slate => ("#43546C", "#EEF0F4"),
            _ => throw new ArgumentOutOfRangeException(nameof(preset))
        };
        return table with { Accent = accent, BandFill = band, BodyFill = "#FFFFFF" };
    }

    private enum CellEdge { Left, Right, Top, Bottom }

    /// <summary>Apply borders to an expanded, merged-aware selection and both sides of shared edges.
    /// A side that would require different borders along one neighboring merged edge is rejected atomically.</summary>
    public static TableSpec SetBorders(TableSpec table, TableRange selection, TableBorderScope scope, TableBorder border)
    {
        ArgumentNullException.ThrowIfNull(border);
        if (!Enum.IsDefined(scope)) throw new ArgumentOutOfRangeException(nameof(scope));
        if (!float.IsFinite(border.Width) || border.Width < 0 || border.Width > 100 || !Enum.IsDefined(border.Dash)) throw new InvalidDataException("Invalid table border.");
        CheckColor(border.Color);
        var index = TableGridIndex.For(table);
        var range = ExpandRange(table, selection);
        var coverage = new Dictionary<(TableCell Cell, CellEdge Edge), HashSet<int>>();
        void Cover(TableCell cell, CellEdge edge, int start, int end)
        {
            var key = (cell, edge);
            if (!coverage.TryGetValue(key, out var positions)) coverage.Add(key, positions = []);
            for (int coordinate = start; coordinate < end; coordinate++) positions.Add(coordinate);
        }
        void Mark(TableCell cell, CellEdge edge)
        {
            bool vertical = edge is CellEdge.Left or CellEdge.Right;
            int start = vertical ? cell.Row : cell.Column, end = start + (vertical ? cell.RowSpan : cell.ColumnSpan);
            Cover(cell, edge, start, end);
            int neighbor = edge switch { CellEdge.Left => cell.Column - 1, CellEdge.Right => cell.Column + cell.ColumnSpan, CellEdge.Top => cell.Row - 1, _ => cell.Row + cell.RowSpan };
            if (neighbor < 0 || neighbor >= (vertical ? table.ColumnCount : table.RowCount)) return;
            var opposite = edge switch { CellEdge.Left => CellEdge.Right, CellEdge.Right => CellEdge.Left, CellEdge.Top => CellEdge.Bottom, _ => CellEdge.Top };
            for (int coordinate = start; coordinate < end; coordinate++)
            {
                var adjacent = vertical ? index.Owner(coordinate, neighbor) : index.Owner(neighbor, coordinate);
                Cover(adjacent, opposite, coordinate, coordinate + 1);
            }
        }
        foreach (var cell in table.Cells)
        {
            if (!range.Contains(cell)) continue;
            bool left = cell.Column == range.Column, right = cell.Column + cell.ColumnSpan == range.Right;
            bool top = cell.Row == range.Row, bottom = cell.Row + cell.RowSpan == range.Bottom;
            bool Choose(bool outside, CellEdge edge) => scope switch
            {
                TableBorderScope.All or TableBorderScope.None => true,
                TableBorderScope.Outside => outside,
                TableBorderScope.Inside => !outside,
                TableBorderScope.InsideHorizontal => !outside && edge is CellEdge.Top or CellEdge.Bottom,
                TableBorderScope.InsideVertical => !outside && edge is CellEdge.Left or CellEdge.Right,
                TableBorderScope.Left => outside && edge == CellEdge.Left,
                TableBorderScope.Right => outside && edge == CellEdge.Right,
                TableBorderScope.Top => outside && edge == CellEdge.Top,
                TableBorderScope.Bottom => outside && edge == CellEdge.Bottom,
                _ => false
            };
            if (Choose(left, CellEdge.Left)) Mark(cell, CellEdge.Left);
            if (Choose(right, CellEdge.Right)) Mark(cell, CellEdge.Right);
            if (Choose(top, CellEdge.Top)) Mark(cell, CellEdge.Top);
            if (Choose(bottom, CellEdge.Bottom)) Mark(cell, CellEdge.Bottom);
        }
        foreach (var (key, positions) in coverage)
        {
            int span = key.Edge is CellEdge.Left or CellEdge.Right ? key.Cell.RowSpan : key.Cell.ColumnSpan;
            if (positions.Count != span) throw new InvalidOperationException("This selection cuts a neighboring merged border. Expand the selection or split that merged cell before changing this edge.");
        }
        var value = scope == TableBorderScope.None ? border with { Width = 0 } : border;
        bool changed = false;
        var cells = table.Cells.Select(cell =>
        {
            var next = cell with
            {
                Left = coverage.ContainsKey((cell, CellEdge.Left)) ? value : cell.Left,
                Right = coverage.ContainsKey((cell, CellEdge.Right)) ? value : cell.Right,
                Top = coverage.ContainsKey((cell, CellEdge.Top)) ? value : cell.Top,
                Bottom = coverage.ContainsKey((cell, CellEdge.Bottom)) ? value : cell.Bottom
            };
            if (next == cell) return cell;
            changed = true; return next;
        }).ToImmutableArray();
        return changed ? table with { Cells = cells } : table;
    }
}
