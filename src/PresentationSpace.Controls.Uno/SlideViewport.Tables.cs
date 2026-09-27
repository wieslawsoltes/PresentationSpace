using System.Collections.Immutable;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PresentationSpace.Core;
using PresentationSpace.Ribbon.Uno;
using PresentationSpace.Rendering.Skia;
using SkiaSharp;
using Windows.System;

namespace PresentationSpace.Controls.Uno;

public sealed partial class SlideViewport
{
    private sealed record CellSelection(Guid SlideId, Guid ShapeId, int Row, int Column, TableRange Range);
    private CellSelection? _cellSelection;
    private TextBox? _cellEditor;
    private TableSpec? _cellOriginalTable;
    private SlideShape? _cellDraft;
    private int _cellTextStart, _cellTextLength;
    private (TableSpec? Spec, RectF Bounds, TableLayout? Layout) _cellLayout;
    public TableRange? ActiveTableRange => _cellSelection?.Range;
    public event EventHandler? InteractionChanged;

    private TableLayout GetCellLayout(SlideShape shape)
    {
        var table = TableModel.Get(shape);
        if (!ReferenceEquals(table, _cellLayout.Spec) || shape.Bounds != _cellLayout.Bounds)
            _cellLayout = (table, shape.Bounds, new(table, shape.Bounds));
        return _cellLayout.Layout!;
    }
    private SlideShape? ActiveTable => Session?.PrimaryShape is { Kind: ShapeKind.Table, Locked: false } shape &&
        _cellSelection is { } selection && selection.ShapeId == shape.Id && selection.SlideId == Session.CurrentSlide.Id ? shape : null;

    public void SelectTableCell(int row, int column, bool extend = false)
    {
        CommitCellText();
        if (Session?.PrimaryShape is not { Kind: ShapeKind.Table, Locked: false } shape) return;
        var table = TableModel.Get(shape); var cell = TableModel.CellAt(table, row, column);
        var old = _cellSelection; var range = new TableRange(cell.Row, cell.Column, cell.RowSpan, cell.ColumnSpan);
        if (extend && old?.ShapeId == shape.Id)
            range = TableModel.ExpandRange(table, new(Math.Min(old.Row, cell.Row), Math.Min(old.Column, cell.Column),
                Math.Max(old.Row + 1, cell.Row + cell.RowSpan) - Math.Min(old.Row, cell.Row), Math.Max(old.Column + 1, cell.Column + cell.ColumnSpan) - Math.Min(old.Column, cell.Column)));
        _cellSelection = new(Session.CurrentSlide.Id, shape.Id, extend && old is not null ? old.Row : cell.Row,
            extend && old is not null ? old.Column : cell.Column, range);
        _cellTextStart = _cellTextLength = 0; Refresh(); InteractionChanged?.Invoke(this, EventArgs.Empty);
    }
    private void SynchronizeCellSelection()
    {
        if (_cellSelection is not { } selection) return;
        if (ActiveTable is not { } shape) { CancelCellText(); _cellSelection = null; return; }
        var table = TableModel.Get(shape);
        if (selection.Row >= table.RowCount || selection.Column >= table.ColumnCount || selection.Range.Bottom > table.RowCount || selection.Range.Right > table.ColumnCount)
        { CancelCellText(); _cellSelection = null; return; }
        _cellSelection = selection with { Range = TableModel.ExpandRange(table, selection.Range) };
    }
    private bool SelectTableAt(PointF position, bool extend, bool edit)
    {
        if (Session?.PrimaryShape is not { Kind: ShapeKind.Table, Locked: false } shape) return false;
        var local = Geometry.Rotate(position, shape.Bounds.Center, -shape.Rotation);
        var cell = GetCellLayout(shape).HitTest(local); if (cell is null) return false;
        SelectTableCell(cell.Row, cell.Column, extend); if (edit) EditTableCell(); return true;
    }
    public void EditTableCell()
    {
        CommitCellText();
        if (Session?.PrimaryShape is not { Kind: ShapeKind.Table, Locked: false } shape) return;
        if (ActiveTable is null) SelectTableCell(0, 0);
        var selection = _cellSelection!; var table = TableModel.Get(shape); var cell = TableModel.CellAt(table, selection.Row, selection.Column);
        var bounds = GetCellLayout(shape).Bounds(cell); var style = TableModel.Style(table, cell);
        _cellOriginalTable = table; _cellDraft = TableModel.TextShape(table, cell);
        _cellEditor = new TextBox
        {
            Text = cell.Text, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxLength = TableModel.MaxTextLength,
            FontFamily = new FontFamily(style.FontFamily), FontSize = Math.Max(1, style.FontSize * _scale),
            FontWeight = style.Bold ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal,
            FontStyle = style.Italic ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal,
            Width = Math.Max(20, bounds.Width * _scale), Height = Math.Max(20, bounds.Height * _scale), MinWidth = 0, MinHeight = 0,
            Padding = new(cell.MarginLeft * _scale, cell.MarginTop * _scale, cell.MarginRight * _scale, cell.MarginBottom * _scale),
            BorderThickness = new(1), BorderBrush = OfficePalette.Accent,
            Background = OfficePalette.Brush(TableModel.Fill(table, cell)), Foreground = OfficePalette.Brush(style.Color),
            TextAlignment = style.Alignment switch { ParagraphAlignment.Center => TextAlignment.Center, ParagraphAlignment.Right => TextAlignment.Right, _ => TextAlignment.Left },
            RenderTransform = new RotateTransform { Angle = shape.Rotation, CenterX = (shape.Bounds.Center.X - bounds.X) * _scale, CenterY = (shape.Bounds.Center.Y - bounds.Y) * _scale }
        };
        AutomationProperties.SetName(_cellEditor, $"Slide table cell row {cell.Row + 1}, column {cell.Column + 1}");
        AutomationProperties.SetAutomationId(_cellEditor, "canvas-table-cell-editor");
        Canvas.SetLeft(_cellEditor, _ox + bounds.X * _scale); Canvas.SetTop(_cellEditor, _oy + bounds.Y * _scale);
        _overlay.Children.Add(_cellEditor);
        _cellEditor.TextChanged += (_, _) => CaptureCellDraft();
        _cellEditor.SelectionChanged += (_, _) => CaptureCellDraft();
        _cellEditor.LostFocus += (_, _) => CommitCellText();
        _cellEditor.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Escape) { CancelCellText(); Focus(FocusState.Programmatic); e.Handled = true; }
            else if (e.Key == VirtualKey.Enter && Key(VirtualKey.Control)) { CommitCellText(); Focus(FocusState.Programmatic); e.Handled = true; }
            else if (e.Key == VirtualKey.Tab) { CommitCellText(); MoveTableCell(Key(VirtualKey.Shift) ? -1 : 1); EditTableCell(); e.Handled = true; }
            else if (Key(VirtualKey.Control) && e.Key is VirtualKey.B or VirtualKey.I or VirtualKey.U)
            {
                CaptureCellDraft(); int start = _cellTextStart, length = _cellTextLength;
                if (e.Key == VirtualKey.B) ToggleBold(); else if (e.Key == VirtualKey.I) ToggleItalic(); else ToggleUnderline();
                EditTableCell(); _cellEditor?.Select(start, length); e.Handled = true;
            }
        };
        _cellEditor.Focus(FocusState.Programmatic); _cellEditor.SelectAll(); InteractionChanged?.Invoke(this, EventArgs.Empty);
    }
    private void CaptureCellDraft()
    {
        if (_cellEditor is not { } editor || _cellDraft is not { } draft) return;
        if (editor.Text != draft.Text) _cellDraft = RichText.Reconcile(draft, draft with { Text = editor.Text });
        _cellTextStart = editor.SelectionStart; _cellTextLength = editor.SelectionLength;
    }
    private void CancelCellText()
    {
        var editor = _cellEditor; _cellEditor = null; _cellDraft = null; _cellOriginalTable = null;
        if (editor is not null) _overlay.Children.Remove(editor);
    }
    private void CommitCellText()
    {
        if (_cellEditor is null) return;
        CaptureCellDraft(); var draft = _cellDraft; var original = _cellOriginalTable; var selection = _cellSelection;
        CancelCellText();
        if (draft is null || original is null || selection is null || Session is not { } session) return;
        int index = session.Document.Slides.FindIndex(slide => slide.Id == selection.SlideId); if (index < 0) return;
        var slide = session.Document.Slides[index]; var shape = slide.Shapes.FirstOrDefault(s => s.Id == selection.ShapeId);
        if (shape is not { Kind: ShapeKind.Table, Locked: false } || !ReferenceEquals(TableModel.Get(shape), original)) return; // Never overwrite a concurrent replacement.
        var cell = TableModel.CellAt(original, selection.Row, selection.Column);
        if (cell.Text == draft.Text && cell.TextRanges == draft.TextRanges) return;
        var next = TableModel.EditCells(original, new(cell.Row, cell.Column, cell.RowSpan, cell.ColumnSpan), c => c with { Text = draft.Text, TextRanges = draft.TextRanges });
        var updated = slide with { Shapes = slide.Shapes.SetItem(slide.Shapes.IndexOf(shape), TableModel.Apply(shape, next)) };
        if (index == session.SlideIndex) session.EditSlide("Edit table cell", _ => updated);
        else session.EditDocument("Edit table cell", document => document with { Slides = document.Slides.SetItem(index, updated) });
        Refresh(); InteractionChanged?.Invoke(this, EventArgs.Empty);
    }
    private TextStyle? CurrentTableTextStyle()
    {
        CaptureCellDraft();
        if (ActiveTable is not { } shape || _cellSelection is not { } selection) return null;
        var table = TableModel.Get(shape); var text = _cellDraft ?? TableModel.TextShape(table, TableModel.CellAt(table, selection.Row, selection.Column));
        return RichText.StyleAt(text, Math.Clamp(_cellTextStart, 0, Math.Max(0, text.Text.Length - 1)));
    }
    private bool FormatTableText(string label, Func<TextStyle, TextStyle> format, bool paragraph)
    {
        if (ActiveTable is null || _cellSelection is not { } selection) return false;
        CaptureCellDraft(); CommitCellText();
        var shape = ActiveTable; if (shape is null) return true;
        var table = TableModel.Get(shape);
        var next = TableModel.EditCells(table, selection.Range, cell =>
        {
            var text = TableModel.TextShape(table, cell);
            int start = Math.Clamp(_cellTextStart, 0, text.Text.Length), length = Math.Clamp(_cellTextLength, 0, text.Text.Length - start);
            while (!RichText.IsBoundary(text.Text, start)) start--;
            int end = Math.Min(text.Text.Length, start + length); while (!RichText.IsBoundary(text.Text, end)) end++;
            bool range = selection.Range.RowCount == cell.RowSpan && selection.Range.ColumnCount == cell.ColumnSpan && end > start;
            var result = range ? (paragraph ? RichTextEditing.FormatParagraphs(text, start, end - start, format) : RichText.Format(text, start, end - start, format)) :
                text with { TextStyle = format(text.TextStyle), TextRanges = text.TextRanges.Select(r => r with { Style = format(r.Style) }).ToImmutableArray() };
            return cell with { TextStyle = result.TextStyle, TextRanges = result.TextRanges };
        });
        Session!.Apply(label, s => s.Id == shape.Id ? TableModel.Apply(s, next) : s); return true;
    }
    private void MoveTableCell(int delta)
    {
        if (ActiveTable is not { } shape || _cellSelection is not { } selection) return;
        var table = TableModel.Get(shape); var ordered = table.Cells.OrderBy(c => c.Row).ThenBy(c => c.Column).ToArray();
        int index = Array.FindIndex(ordered, c => c.Row == selection.Row && c.Column == selection.Column);
        int target = Math.Clamp(index + delta, 0, ordered.Length - 1);
        SelectTableCell(ordered[target].Row, ordered[target].Column);
    }
    private bool HandleTableKey(KeyRoutedEventArgs e)
    {
        if (_cellEditor is not null) return true;
        if (ActiveTable is not { } shape || _cellSelection is not { } selection || Key(VirtualKey.Control)) return false;
        var table = TableModel.Get(shape); var cell = TableModel.CellAt(table, selection.Row, selection.Column);
        switch (e.Key)
        {
            case VirtualKey.Escape: _cellSelection = null; Refresh(); InteractionChanged?.Invoke(this, EventArgs.Empty); break;
            case VirtualKey.F2: case VirtualKey.Enter: EditTableCell(); break;
            case VirtualKey.Tab: MoveTableCell(Key(VirtualKey.Shift) ? -1 : 1); break;
            case VirtualKey.Right: SelectTableCell(cell.Row, Math.Min(table.ColumnCount - 1, cell.Column + cell.ColumnSpan), Key(VirtualKey.Shift)); break;
            case VirtualKey.Left: SelectTableCell(cell.Row, Math.Max(0, cell.Column - 1), Key(VirtualKey.Shift)); break;
            case VirtualKey.Up: SelectTableCell(Math.Max(0, cell.Row - 1), cell.Column, Key(VirtualKey.Shift)); break;
            case VirtualKey.Down: SelectTableCell(Math.Min(table.RowCount - 1, cell.Row + cell.RowSpan), cell.Column, Key(VirtualKey.Shift)); break;
            case VirtualKey.Delete: case VirtualKey.Back:
                var cleared = TableModel.EditCells(table, selection.Range, c => c with { Text = "", TextRanges = [] });
                Session!.Apply("Clear table cells", s => s.Id == shape.Id ? TableModel.Apply(s, cleared) : s); break;
            default: return false;
        }
        e.Handled = true; return true;
    }
    private void DrawTableSelection(SKCanvas canvas)
    {
        if (ActiveTable is not { } shape || _cellSelection is not { } selection) return;
        var layout = GetCellLayout(shape); var range = selection.Range;
        var bounds = new SKRect(layout.X[range.Column], layout.Y[range.Row], layout.X[range.Right], layout.Y[range.Bottom]);
        canvas.Save();
        try
        {
            canvas.RotateDegrees(shape.Rotation, shape.Bounds.Center.X, shape.Bounds.Center.Y);
            using var fill = new SKPaint { Color = SlideRenderer.Color("#20D35230") };
            using var border = new SKPaint { Color = SlideRenderer.Color("#D35230"), StrokeWidth = 2 / _scale, Style = SKPaintStyle.Stroke, IsAntialias = true };
            canvas.DrawRect(bounds, fill); canvas.DrawRect(bounds, border);
        }
        finally { canvas.Restore(); }
    }
    public void AutoFitTableRows()
    {
        CommitText();
        if (Session?.PrimaryShape is not { Kind: ShapeKind.Table, Locked: false } shape) return;
        var result = _renderer.AutoFitTableRows(TableModel.Get(shape), shape.Bounds.Width);
        if (result.Height > 100000) throw new InvalidOperationException("The fitted table exceeds the maximum shape height.");
        var bounds = Geometry.ResizeRotated(shape.Bounds, shape.Rotation, 5, new(0, result.Height - shape.Bounds.Height), false);
        Session.Apply("Auto-fit table rows", s => s.Id == shape.Id ? TableModel.Apply(s, result.Table) with { Bounds = bounds } : s);
    }
}
