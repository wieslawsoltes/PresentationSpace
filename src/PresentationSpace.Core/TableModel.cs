using System.Collections.Immutable;
using System.Text;
using System.Text.Json.Serialization;

namespace PresentationSpace.Core;

public enum TableBorderDash { Solid, Dash, Dot }
public sealed record TableBorder
{
    public string Color { get; init; } = "#D8DEE8";
    public float Width { get; init; } = 1;
    public TableBorderDash Dash { get; init; }
}
public sealed record TableCell
{
    public int Row { get; init; }
    public int Column { get; init; }
    public int RowSpan { get; init; } = 1;
    public int ColumnSpan { get; init; } = 1;
    public string Text { get; init; } = "";
    public TextStyle? TextStyle { get; init; }
    public ImmutableArray<TextRangeStyle> TextRanges { get; init; } = [];
    public string? Fill { get; init; }
    public TableBorder Left { get; init; } = new();
    public TableBorder Right { get; init; } = new();
    public TableBorder Top { get; init; } = new();
    public TableBorder Bottom { get; init; } = new();
    public float MarginLeft { get; init; } = 10;
    public float MarginRight { get; init; } = 10;
    public float MarginTop { get; init; } = 3;
    public float MarginBottom { get; init; } = 3;
}
/// <summary>A partition of a rectangular grid. Only merge origins are stored, never hidden duplicate text.</summary>
public sealed record TableSpec
{
    public ImmutableArray<float> ColumnWidths { get; init; } = [160, 160, 160];
    public ImmutableArray<float> RowHeights { get; init; } = [48, 48];
    public ImmutableArray<TableCell> Cells { get; init; } = [];
    public string Accent { get; init; } = "#D35230";
    public string BandFill { get; init; } = "#F1F4F8";
    public string BodyFill { get; init; } = "#FFFFFF";
    public bool HeaderRow { get; init; } = true;
    public bool BandedRows { get; init; } = true;
    public bool TotalRow { get; init; }
    public bool FirstColumn { get; init; }
    public bool LastColumn { get; init; }
    public bool BandedColumns { get; init; }
    public TextStyle TextStyle { get; init; } = new() { FontSize = 20, VerticalAlignment = VerticalAlignment.Middle };
    [JsonIgnore] public int RowCount => RowHeights.Length;
    [JsonIgnore] public int ColumnCount => ColumnWidths.Length;
}
public readonly record struct TableRange(int Row, int Column, int RowCount, int ColumnCount)
{
    public int Bottom => Row + RowCount;
    public int Right => Column + ColumnCount;
    public bool Contains(TableCell c) => c.Row >= Row && c.Column >= Column && c.Row + c.RowSpan <= Bottom && c.Column + c.ColumnSpan <= Right;
    public bool Intersects(TableCell c) => c.Row < Bottom && c.Row + c.RowSpan > Row && c.Column < Right && c.Column + c.ColumnSpan > Column;
}

public static partial class TableModel
{
    public const int MaxRows = 100, MaxColumns = 100, MaxCells = 10000, MaxTextLength = 1000000;
    public static TableSpec Create(int rows = 4, int columns = 3)
    {
        CheckDimensions(rows, columns);
        return new() { RowHeights = Enumerable.Repeat(48f, rows).ToImmutableArray(), ColumnWidths = Enumerable.Repeat(160f, columns).ToImmutableArray(),
            Cells = (from r in Enumerable.Range(0, rows) from c in Enumerable.Range(0, columns) select new TableCell { Row = r, Column = c }).ToImmutableArray() };
    }
    private static void CheckDimensions(int rows, int columns)
    {
        if (rows < 1 || rows > MaxRows || columns < 1 || columns > MaxColumns) throw new InvalidDataException("Tables require 1–100 rows and columns.");
    }
    public static TableSpec Get(SlideShape shape)
    {
        if (shape.Table is { } table) return table;
        if (shape.TableColumns is < 1 or > MaxColumns || shape.Cells.IsDefault || shape.Cells.Length > MaxCells) throw new InvalidDataException("Invalid legacy table dimensions.");
        int columns = shape.TableColumns, rows = Math.Max(1, (shape.Cells.Length + columns - 1) / columns);
        var value = Create(rows, columns) with { Accent = shape.Fill, TextStyle = shape.TextStyle with { FontSize = Math.Min(shape.TextStyle.FontSize, Math.Max(9, shape.Bounds.Height / rows * .35f)), VerticalAlignment = VerticalAlignment.Middle } };
        return value with { Cells = value.Cells.Select((cell, i) => cell with { Text = i < shape.Cells.Length ? shape.Cells[i] : "" }).ToImmutableArray() };
    }
    public static SlideShape Apply(SlideShape shape, TableSpec table)
    {
        Validate(table);
        var cells = new string[table.RowCount * table.ColumnCount]; Array.Fill(cells, "");
        foreach (var cell in table.Cells) cells[cell.Row * table.ColumnCount + cell.Column] = cell.Text;
        return shape with { Kind = ShapeKind.Table, Table = table, TableColumns = table.ColumnCount, Cells = cells.ToImmutableArray(), Fill = table.Accent };
    }
    public static TextStyle Style(TableSpec table, TableCell cell)
    {
        if (cell.TextStyle is { } style) return style;
        bool emphasis = Emphasized(table, cell);
        return emphasis ? table.TextStyle with { Bold = true, Color = "#FFFFFF" } : table.TextStyle;
    }
    private static bool Emphasized(TableSpec table, TableCell cell) =>
        table.HeaderRow && cell.Row == 0 || table.TotalRow && cell.Row + cell.RowSpan == table.RowCount ||
        table.FirstColumn && cell.Column == 0 || table.LastColumn && cell.Column + cell.ColumnSpan == table.ColumnCount;
    public static string Fill(TableSpec table, TableCell cell) => cell.Fill ??
        (Emphasized(table, cell) ? table.Accent : table.BandedRows && cell.Row % 2 == 0 || table.BandedColumns && cell.Column % 2 == 0 ? table.BandFill : table.BodyFill);
    public static SlideShape TextShape(TableSpec table, TableCell cell, RectF bounds = default) => new() { Kind = ShapeKind.Text, Bounds = bounds, Text = cell.Text, TextStyle = Style(table, cell), TextRanges = cell.TextRanges };
    public static void Validate(TableSpec table)
    {
        ArgumentNullException.ThrowIfNull(table);
        if (table.ColumnWidths.IsDefault || table.RowHeights.IsDefault || table.Cells.IsDefault) throw new InvalidDataException("Uninitialized table arrays.");
        CheckDimensions(table.RowCount, table.ColumnCount);
        foreach (float size in table.ColumnWidths.Concat(table.RowHeights))
            if (!float.IsFinite(size) || size < .01f || size > 100000) throw new InvalidDataException("Invalid table track size.");
        if (table.Cells.Length > MaxCells) throw new InvalidDataException("Too many table cells.");
        CheckColor(table.Accent); CheckColor(table.BandFill); CheckColor(table.BodyFill); CheckStyle(table.TextStyle);
        var occupied = new bool[table.RowCount * table.ColumnCount]; long textLength = 0;
        foreach (var cell in table.Cells)
        {
            if (cell is null || cell.Row < 0 || cell.Column < 0 || cell.RowSpan < 1 || cell.ColumnSpan < 1 || cell.RowSpan > table.RowCount || cell.ColumnSpan > table.ColumnCount || cell.Row > table.RowCount - cell.RowSpan || cell.Column > table.ColumnCount - cell.ColumnSpan) throw new InvalidDataException("Invalid table merge extent.");
            if (cell.Text is null || (textLength += cell.Text.Length) > MaxTextLength || cell.TextRanges.IsDefault) throw new InvalidDataException("Table text exceeds the limit or is invalid.");
            if (cell.TextStyle is { } style) CheckStyle(style);
            if (cell.Fill is { } fill) CheckColor(fill);
            foreach (var border in new[] { cell.Left, cell.Right, cell.Top, cell.Bottom })
            {
                if (border is null || !float.IsFinite(border.Width) || border.Width < 0 || border.Width > 100 || !Enum.IsDefined(border.Dash)) throw new InvalidDataException("Invalid table border.");
                CheckColor(border.Color);
            }
            foreach (float margin in new[] { cell.MarginLeft, cell.MarginRight, cell.MarginTop, cell.MarginBottom })
                if (!float.IsFinite(margin) || margin < 0 || margin > 1000) throw new InvalidDataException("Invalid cell margin.");
            int end = 0;
            foreach (var range in cell.TextRanges)
            {
                if (range is null || range.Start < end || range.Length <= 0 || range.Start > cell.Text.Length - range.Length || !RichText.IsBoundary(cell.Text, range.Start) || !RichText.IsBoundary(cell.Text, range.Start + range.Length)) throw new InvalidDataException("Invalid table text range.");
                CheckStyle(range.Style); end = range.Start + range.Length;
            }
            for (int r = cell.Row; r < cell.Row + cell.RowSpan; r++) for (int c = cell.Column; c < cell.Column + cell.ColumnSpan; c++)
            { int i = r * table.ColumnCount + c; if (occupied[i]) throw new InvalidDataException("Overlapping table cells."); occupied[i] = true; }
        }
        if (occupied.Any(value => !value)) throw new InvalidDataException("Table cells must cover every grid position.");
    }
    private static void CheckColor(string? color)
    {
        if (color is null || color.Length is not (7 or 9) || color[0] != '#' || color.Skip(1).Any(c => !Uri.IsHexDigit(c))) throw new InvalidDataException("Colors must be #RRGGBB or #AARRGGBB.");
    }
    private static void CheckStyle(TextStyle? style)
    {
        TextFlow.ValidateStyle(style);
        CheckColor(style!.Color);
    }
    public static TableCell CellAt(TableSpec table, int row, int column)
    {
        if (row < 0 || row >= table.RowCount || column < 0 || column >= table.ColumnCount) throw new ArgumentOutOfRangeException(nameof(row));
        return TableGridIndex.For(table).Owner(row, column);
    }
    public static TableRange ExpandRange(TableSpec table, TableRange range)
    {
        CheckRange(table, range); bool changed;
        do
        {
            changed = false;
            foreach (var cell in table.Cells.Where(range.Intersects))
            {
                int r = Math.Min(range.Row, cell.Row), c = Math.Min(range.Column, cell.Column), b = Math.Max(range.Bottom, cell.Row + cell.RowSpan), e = Math.Max(range.Right, cell.Column + cell.ColumnSpan);
                var next = new TableRange(r, c, b - r, e - c); if (next != range) { range = next; changed = true; }
            }
        } while (changed);
        return range;
    }
    private static void CheckRange(TableSpec table, TableRange range)
    {
        if (range.Row < 0 || range.Column < 0 || range.RowCount < 1 || range.ColumnCount < 1 || range.RowCount > table.RowCount || range.ColumnCount > table.ColumnCount || range.Row > table.RowCount - range.RowCount || range.Column > table.ColumnCount - range.ColumnCount) throw new ArgumentOutOfRangeException(nameof(range));
    }
    public static TableSpec EditCells(TableSpec table, TableRange range, Func<TableCell, TableCell> edit)
    {
        Validate(table); CheckRange(table, range);
        var result = table with { Cells = table.Cells.Select(c => range.Intersects(c) ? edit(c) : c).ToImmutableArray() }; Validate(result); return result;
    }
    public static TableSpec SetText(TableSpec table, int row, int column, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var cell = CellAt(table, row, column);
        var old = TextShape(table, cell);
        // Reconcile edits instead of replacing all formatting when only a substring changed.
        var next = RichText.Reconcile(old, old with { Text = text });
        return EditCells(table, new(cell.Row, cell.Column, 1, 1), c => c with { Text = text, TextRanges = next.TextRanges });
    }
    public static TableSpec Merge(TableSpec table, TableRange range)
    {
        Validate(table); CheckRange(table, range);
        var selected = table.Cells.Where(range.Intersects).OrderBy(c => c.Row).ThenBy(c => c.Column).ToArray();
        if (selected.Any(c => !range.Contains(c))) throw new InvalidOperationException("Selection cuts an existing merged cell. Select the entire merged region first.");
        if (selected.Length == 1) return table;
        var origin = selected[0]; var text = new StringBuilder(); var ranges = ImmutableArray.CreateBuilder<TextRangeStyle>();
        foreach (var cell in selected.Where(c => c.Text.Length > 0))
        {
            if (text.Length > 0) text.Append('\n'); int offset = text.Length; var shape = TextShape(table, cell);
            foreach (var run in RichText.Segments(shape, 0, shape.Text.Length)) ranges.Add(run with { Start = run.Start + offset });
            text.Append(cell.Text);
        }
        var merged = origin with { RowSpan = range.RowCount, ColumnSpan = range.ColumnCount, Text = text.ToString(), TextRanges = ranges.ToImmutable(), TextStyle = Style(table, origin), Right = CellAt(table, range.Row, range.Right - 1).Right, Bottom = CellAt(table, range.Bottom - 1, range.Column).Bottom };
        var result = table with { Cells = table.Cells.Where(c => !range.Intersects(c)).Append(merged).OrderBy(c => c.Row).ThenBy(c => c.Column).ToImmutableArray() }; Validate(result); return result;
    }
    public static TableSpec Split(TableSpec table, int row, int column)
    {
        Validate(table); var cell = CellAt(table, row, column); if (cell.RowSpan == 1 && cell.ColumnSpan == 1) return table;
        var cells = table.Cells.Remove(cell).ToBuilder();
        for (int r = cell.Row; r < cell.Row + cell.RowSpan; r++) for (int c = cell.Column; c < cell.Column + cell.ColumnSpan; c++)
            cells.Add(cell with { Row = r, Column = c, RowSpan = 1, ColumnSpan = 1, Text = r == cell.Row && c == cell.Column ? cell.Text : "", TextRanges = r == cell.Row && c == cell.Column ? cell.TextRanges : [] });
        return table with { Cells = cells.OrderBy(c => c.Row).ThenBy(c => c.Column).ToImmutableArray() };
    }
    /// <summary>Insert a track; merges crossing the insertion grow. New positions outside merges are blank.</summary>
    public static TableSpec InsertTrack(TableSpec table, int index, bool row)
    {
        Validate(table); int count = row ? table.RowCount : table.ColumnCount;
        if (index < 0 || index > count) throw new ArgumentOutOfRangeException(nameof(index));
        CheckDimensions(table.RowCount + (row ? 1 : 0), table.ColumnCount + (row ? 0 : 1));
        var tracks = row ? table.RowHeights : table.ColumnWidths; float size = tracks[Math.Min(index, count - 1)];
        var cells = table.Cells.Select(c =>
        {
            int start = row ? c.Row : c.Column, span = row ? c.RowSpan : c.ColumnSpan;
            if (start >= index) return row ? c with { Row = c.Row + 1 } : c with { Column = c.Column + 1 };
            if (start + span > index) return row ? c with { RowSpan = c.RowSpan + 1 } : c with { ColumnSpan = c.ColumnSpan + 1 };
            return c;
        }).ToImmutableArray();
        var result = row ? table with { RowHeights = tracks.Insert(index, size), Cells = cells } : table with { ColumnWidths = tracks.Insert(index, size), Cells = cells };
        var occupied = new bool[result.RowCount * result.ColumnCount];
        foreach (var c in cells) for (int r = c.Row; r < c.Row + c.RowSpan; r++) for (int col = c.Column; col < c.Column + c.ColumnSpan; col++) occupied[r * result.ColumnCount + col] = true;
        var builder = cells.ToBuilder();
        for (int r = 0; r < result.RowCount; r++) for (int c = 0; c < result.ColumnCount; c++) if (!occupied[r * result.ColumnCount + c]) builder.Add(new() { Row = r, Column = c });
        result = result with { Cells = builder.OrderBy(c => c.Row).ThenBy(c => c.Column).ToImmutableArray() }; Validate(result); return result;
    }
    /// <summary>Delete a track; a surviving merge retains its text even when its origin track is deleted.</summary>
    public static TableSpec DeleteTrack(TableSpec table, int index, bool row)
    {
        Validate(table); int count = row ? table.RowCount : table.ColumnCount;
        if (index < 0 || index >= count) throw new ArgumentOutOfRangeException(nameof(index));
        if (count == 1) throw new InvalidOperationException("A table must retain at least one row and column.");
        var cells = ImmutableArray.CreateBuilder<TableCell>();
        foreach (var cell in table.Cells)
        {
            int start = row ? cell.Row : cell.Column, span = row ? cell.RowSpan : cell.ColumnSpan; var next = cell;
            if (start == index && span == 1) continue;
            if (start > index) next = row ? cell with { Row = start - 1 } : cell with { Column = start - 1 };
            else if (start + span > index) next = row ? cell with { RowSpan = span - 1 } : cell with { ColumnSpan = span - 1 };
            cells.Add(next);
        }
        var result = row ? table with { RowHeights = table.RowHeights.RemoveAt(index), Cells = cells.ToImmutable() } : table with { ColumnWidths = table.ColumnWidths.RemoveAt(index), Cells = cells.ToImmutable() }; Validate(result); return result;
    }
    public static TableSpec SizeTrack(TableSpec table, int index, bool row, float size)
    {
        var result = row ? table with { RowHeights = table.RowHeights.SetItem(index, size) } : table with { ColumnWidths = table.ColumnWidths.SetItem(index, size) }; Validate(result); return result;
    }
    public static TableSpec Distribute(TableSpec table, bool rows) => rows ? table with { RowHeights = Enumerable.Repeat(table.RowHeights.Sum() / table.RowCount, table.RowCount).ToImmutableArray() } : table with { ColumnWidths = Enumerable.Repeat(table.ColumnWidths.Sum() / table.ColumnCount, table.ColumnCount).ToImmutableArray() };
    public static SlideShape Reconcile(SlideShape before, SlideShape after)
    {
        if (before.Table is not { } table || !ReferenceEquals(before.Table, after.Table) || after.Kind != ShapeKind.Table) return after;
        if (before.Fill != after.Fill) table = table with { Accent = after.Fill };
        if (before.TextStyle != after.TextStyle)
        {
            // Apply only properties changed on the shape, not its complete fallback style.
            // Otherwise toggling Bold would overwrite a cell's explicit color and font size.
            TextStyle Patch(TextStyle value) => RichText.ApplyStyleChanges(value, before.TextStyle, after.TextStyle);
            table = table with { TextStyle = Patch(table.TextStyle), Cells = table.Cells.Select(c => c with
            {
                TextStyle = c.TextStyle is { } style ? Patch(style) : null,
                TextRanges = c.TextRanges.Select(r => r with { Style = Patch(r.Style) }).ToImmutableArray()
            }).ToImmutableArray() };
        }
        return ReferenceEquals(table, before.Table) ? after : Apply(after, table);
    }
}

/// <summary>Reusable normalized track geometry and O(1) grid-to-origin mapping.</summary>
public sealed class TableLayout
{
    public TableSpec Table { get; }
    public float[] X { get; }
    public float[] Y { get; }
    private readonly TableGridIndex _index;
    public TableLayout(TableSpec table, RectF bounds)
    {
        if (!float.IsFinite(bounds.X) || !float.IsFinite(bounds.Y) || !float.IsFinite(bounds.Width) || !float.IsFinite(bounds.Height) || !float.IsFinite(bounds.Right) || !float.IsFinite(bounds.Bottom) || bounds.Width <= 0 || bounds.Height <= 0) throw new ArgumentOutOfRangeException(nameof(bounds));
        _index = TableGridIndex.For(table); Table = table;
        X = Edges(table.ColumnWidths, bounds.X, bounds.Width); Y = Edges(table.RowHeights, bounds.Y, bounds.Height);
    }
    private static float[] Edges(ImmutableArray<float> sizes, float start, float extent)
    {
        var edges = new float[sizes.Length + 1]; double total = sizes.Sum(s => (double)s), offset = 0; edges[0] = start;
        for (int i = 0; i < sizes.Length; i++) { offset += sizes[i]; edges[i + 1] = start + (float)(offset / total * extent); } return edges;
    }
    public RectF Bounds(TableCell c) => new(X[c.Column], Y[c.Row], X[c.Column + c.ColumnSpan] - X[c.Column], Y[c.Row + c.RowSpan] - Y[c.Row]);
    public TableCell Owner(int row, int column) => _index.Owner(row, column);
    public TableCell? HitTest(PointF point)
    {
        if (!float.IsFinite(point.X) || !float.IsFinite(point.Y) || point.X < X[0] || point.X > X[^1] || point.Y < Y[0] || point.Y > Y[^1]) return null;
        return Owner(Track(Y, point.Y), Track(X, point.X));
    }
    private static int Track(float[] edges, float value)
    {
        int low = 0, high = edges.Length;
        while (low < high) { int middle = low + (high - low) / 2; if (edges[middle] <= value) low = middle + 1; else high = middle; }
        return Math.Clamp(low - 1, 0, edges.Length - 2);
    }
}
