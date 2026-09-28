using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace PresentationSpace.Core;

/// <summary>Validated, immutable grid ownership and reading-order navigation shared by all consumers.
/// Cached by table identity with weak keys: undo snapshots are supported without a permanent global cache.</summary>
public sealed class TableGridIndex
{
    private static readonly ConditionalWeakTable<TableSpec, TableGridIndex> Cache = new();
    private readonly TableCell[] _owners;
    private readonly int[] _originIndices;
    public int Rows { get; }
    public int Columns { get; }
    public ImmutableArray<TableCell> ReadingOrder { get; }
    public static TableGridIndex For(TableSpec table)
    {
        ArgumentNullException.ThrowIfNull(table);
        return Cache.GetValue(table, static value => new TableGridIndex(value));
    }
    private TableGridIndex(TableSpec table)
    {
        TableModel.Validate(table);
        Rows = table.RowCount; Columns = table.ColumnCount;
        _owners = new TableCell[Rows * Columns]; _originIndices = new int[Rows * Columns];
        ReadingOrder = table.Cells.OrderBy(c => c.Row).ThenBy(c => c.Column).ToImmutableArray();
        for (int index = 0; index < ReadingOrder.Length; index++)
        {
            var cell = ReadingOrder[index];
            for (int row = cell.Row; row < cell.Row + cell.RowSpan; row++)
                for (int column = cell.Column; column < cell.Column + cell.ColumnSpan; column++)
                { int offset = row * Columns + column; _owners[offset] = cell; _originIndices[offset] = index; }
        }
    }
    public TableCell Owner(int row, int column)
    {
        Check(row, column); return _owners[row * Columns + column];
    }
    /// <summary>Move in reading order, clamping at either end and skipping all merged continuations.</summary>
    public TableCell Move(int row, int column, int delta)
    {
        Check(row, column);
        long index = (long)_originIndices[row * Columns + column] + delta;
        return ReadingOrder[(int)Math.Clamp(index, 0, ReadingOrder.Length - 1L)];
    }
    private void Check(int row, int column)
    {
        if ((uint)row >= (uint)Rows) throw new ArgumentOutOfRangeException(nameof(row));
        if ((uint)column >= (uint)Columns) throw new ArgumentOutOfRangeException(nameof(column));
    }
}
