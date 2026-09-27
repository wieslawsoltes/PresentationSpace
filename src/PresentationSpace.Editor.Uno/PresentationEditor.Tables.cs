using Microsoft.UI.Xaml;
using PresentationSpace.Controls.Uno;
using PresentationSpace.Core;

namespace PresentationSpace.Editor.Uno;

public sealed partial class PresentationEditor
{
    private void BuildTableCommands()
    {
        _commands.Add(("Edit table cell on slide", () => { HideInspector(); Viewport.EditTableCell(); }));
        _commands.Add(("Auto-fit table rows", () => { Viewport.AutoFitTableRows(); Viewport.Focus(FocusState.Programmatic); }));
        void Add(string title, Action<TableDataEditor> execute, bool focusText = false) => _commands.Add((title, () =>
        {
            if (Session.PrimaryShape is not { Kind: ShapeKind.Table } || Session.Selection.Count != 1) { Notice("Select one table first."); return; }
            // Keep the current editor and its range when invoking a table command from command search.
            if (_format.TableEditor is null) ShowInspector(InspectorMode.Format);
            else _formatColumn.Width = new GridLength(296);
            if (_format.TableEditor is { } table)
            {
                execute(table);
                if (focusText) DispatcherQueue.TryEnqueue(table.FocusText);
                else Viewport.Focus(FocusState.Programmatic);
            }
        }));
        Add("Edit table data", _ => { }, true);
        Add("Table select row", t => t.SelectRow());
        Add("Table select column", t => t.SelectColumn());
        Add("Table select all", t => t.SelectAllCells());
        Add("Merge table cells", t => t.MergeSelection());
        Add("Split table cell", t => t.SplitCell());
        Add("Insert table row", t => t.InsertRow());
        Add("Insert table column", t => t.InsertColumn());
        Add("Delete table row", t => t.DeleteRow());
        Add("Delete table column", t => t.DeleteColumn());
    }
}
