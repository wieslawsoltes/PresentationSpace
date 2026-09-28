using Microsoft.UI.Xaml;
using PresentationSpace.Controls.Uno;
using PresentationSpace.Core;
using PresentationSpace.Ribbon.Uno;

namespace PresentationSpace.Editor.Uno;

public sealed partial class PresentationEditor
{
    private void DesignTable(Action<TableDataEditor> change, bool retainFocus = false)
    {
        if (Session.PrimaryShape is not { Kind: ShapeKind.Table, Locked: false } || Session.Selection.Count != 1) { Notice("Select one unlocked table first."); return; }
        var canvasRange = _format.Visibility != Visibility.Visible ? Viewport.ActiveTableRange : null;
        if (_format.TableEditor is null) ShowInspector(InspectorMode.Format);
        else { _inspectorOpen = true; ApplyEditorLayout(); }
        if (_format.TableEditor is not { } editor) return;
        if (canvasRange is { } range) { editor.ApplyText(); editor.SelectRange(range); }
        change(editor);
        if (!retainFocus) Viewport.Focus(FocusState.Programmatic);
    }
    private IEnumerable<RibbonGroup> TableDesignGroups()
    {
        yield return new("Table styles", Enum.GetValues<TableStylePreset>().Select(preset => (UIElement)Cmd("table-style-" + preset, preset.ToString(), "\uE80A", () => DesignTable(t => t.ApplyTableStyle(preset)))).ToArray());
        yield return new("Style options", RibbonGroup.Column(
            Cmd("table-first-column", "First Column", "\uE8E4", () => DesignTable(t => t.ToggleFirstColumn()), false),
            Cmd("table-last-column", "Last Column", "\uE8E2", () => DesignTable(t => t.ToggleLastColumn()), false),
            Cmd("table-banded-columns", "Banded Columns", "\uE80A", () => DesignTable(t => t.ToggleBandedColumns()), false)));
        yield return new("Borders", Menu("table-borders", "Borders  ⌄", "\uE80A", Enum.GetValues<TableBorderScope>().Select(scope => (scope.ToString(), (Action)(() => DesignTable(t => t.ApplyBorderScope(scope)))))));
        yield return new("Layout", Cmd("table-autofit", "Auto-fit\nRows", "\uE740", Viewport.AutoFitTableRows), Cmd("table-edit", "Edit\nCells", "\uE70F", () => DesignTable(t => t.FocusText(), true)));
    }
}
