using System.Collections.Immutable;

namespace PresentationSpace.Core;

public static partial class TableModel
{
    /// <summary>Replace text in visible cell origins, preserving merges, styles and track geometry.</summary>
    public static SlideShape ReplaceAll(SlideShape shape, string find, string replacement)
    {
        ArgumentNullException.ThrowIfNull(find);
        ArgumentNullException.ThrowIfNull(replacement);
        if (find.Length == 0) return shape;
        var table = Get(shape); Validate(table);
        bool changed = false;
        var cells = table.Cells.Select(cell =>
        {
            var text = TextShape(table, cell);
            var result = RichTextEditing.ReplaceAll(text, find, replacement);
            if (ReferenceEquals(result, text)) return cell;
            changed = true;
            return cell with { Text = result.Text, TextRanges = result.TextRanges };
        }).ToImmutableArray();
        return changed ? Apply(shape, table with { Cells = cells }) : shape;
    }
}
