using System.Collections.Immutable;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using PresentationSpace.Core;
using PresentationSpace.Ribbon.Uno;
using Windows.System;
using Windows.UI.Core;

namespace PresentationSpace.Controls.Uno;

/// <summary>Session-independent table authoring. A bounded grid navigator and validated immutable edits.</summary>
public sealed class TableDataEditor : UserControl
{
    private const int PageRows = 8, PageColumns = 4;
    private readonly Grid _grid = new() { RowSpacing = 2, ColumnSpacing = 2 };
    private readonly TextBlock _selection = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _error = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = OfficePalette.Brush("B42318") };
    private readonly TextBox _text = new() { Header = "Cell text · Ctrl+Enter to apply", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 85, MaxHeight = 180, MaxLength = TableModel.MaxTextLength, FontSize = 12 };
    private readonly TextBox _rowSize = new() { Header = "Row weight", FontSize = 12 }, _columnSize = new() { Header = "Column weight", FontSize = 12 }, _fontSize = new() { Header = "Font size", FontSize = 12 }, _borderWidth = new() { Header = "Border width", Text = "1", FontSize = 12 };
    private readonly ComboBox _horizontal = new() { Header = "Alignment", ItemsSource = Enum.GetNames<ParagraphAlignment>(), HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _vertical = new() { Header = "Vertical alignment", ItemsSource = Enum.GetNames<Core.VerticalAlignment>(), HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _dash = new() { Header = "Border style", ItemsSource = Enum.GetNames<TableBorderDash>(), SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly CheckBox _header = new() { Content = "Header row" }, _bands = new() { Content = "Banded rows" }, _total = new() { Content = "Total row" };
    private TableSpec _value = TableModel.Create();
    private int _row, _column, _pageRow, _pageColumn;
    private TableRange _range = new(0, 0, 1, 1);
    private bool _loading;
    public TableSpec Value => _value;
    public TableRange Selection => _range;
    public event EventHandler<TableSpec>? ValueChanged;

    public TableDataEditor()
    {
        var panel = new StackPanel { Spacing = 9 };
        panel.Children.Add(new TextBlock { Text = "Click a cell; Shift+click extends selection. Row/column headers select a track. Apply text before changing selection.", FontSize = 11, Foreground = OfficePalette.Muted, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(_selection); panel.Children.Add(_grid);
        var paging = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
        paging.Children.Add(Button("↑ Rows", () => Page(-PageRows, 0))); paging.Children.Add(Button("↓ Rows", () => Page(PageRows, 0))); paging.Children.Add(Button("←", () => Page(0, -PageColumns))); paging.Children.Add(Button("→", () => Page(0, PageColumns))); panel.Children.Add(paging);
        panel.Children.Add(_text); panel.Children.Add(_error);
        panel.Children.Add(Button("Apply cell text", ApplyText));
        var merge = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        merge.Children.Add(Button("Merge cells", MergeSelection)); merge.Children.Add(Button("Split merged cell", SplitCell)); panel.Children.Add(merge);
        var tracks = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        tracks.Children.Add(Button("+ Row", InsertRow)); tracks.Children.Add(Button("+ Column", InsertColumn)); panel.Children.Add(tracks);
        var remove = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        remove.Children.Add(Button("Delete row", DeleteRow)); remove.Children.Add(Button("Delete column", DeleteColumn)); panel.Children.Add(remove);
        panel.Children.Add(_rowSize); panel.Children.Add(_columnSize);
        panel.Children.Add(Button("Apply track sizes", () => Change(t => TableModel.SizeTrack(TableModel.SizeTrack(t, _row, true, Number(_rowSize)), _column, false, Number(_columnSize)))));
        var distribute = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        distribute.Children.Add(Button("Equal rows", () => Change(t => TableModel.Distribute(t, true)))); distribute.Children.Add(Button("Equal columns", () => Change(t => TableModel.Distribute(t, false)))); panel.Children.Add(distribute);
        panel.Children.Add(_header); panel.Children.Add(_bands); panel.Children.Add(_total);
        panel.Children.Add(new TextBlock { Text = "Table accent", FontSize = 12 }); var accent = new ColorPalette(); accent.ColorSelected += (_, color) => Change(t => t with { Accent = color }); panel.Children.Add(accent);
        panel.Children.Add(Button("Reset cell formatting", () => Change(t => TableModel.EditCells(t, _range, c => c with { Fill = null, TextStyle = null, TextRanges = [], Left = new(), Right = new(), Top = new(), Bottom = new() }))));
        panel.Children.Add(new TextBlock { Text = "Selected cell fill", FontSize = 12 }); var fill = new ColorPalette(); fill.ColorSelected += (_, color) => Change(t => TableModel.EditCells(t, _range, c => c with { Fill = color })); panel.Children.Add(fill);
        panel.Children.Add(new TextBlock { Text = "Selected text color", FontSize = 12 }); var foreground = new ColorPalette(); foreground.ColorSelected += (_, color) => FormatText(s => s with { Color = color }); panel.Children.Add(foreground);
        var font = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        font.Children.Add(Button("Bold", () => FormatText(s => s with { Bold = !TableModel.Style(_value, TableModel.CellAt(_value, _row, _column)).Bold })));
        font.Children.Add(Button("Italic", () => FormatText(s => s with { Italic = !TableModel.Style(_value, TableModel.CellAt(_value, _row, _column)).Italic }))); panel.Children.Add(font);
        panel.Children.Add(_fontSize); panel.Children.Add(Button("Apply font size", () => FormatText(s => s with { FontSize = Number(_fontSize) })));
        panel.Children.Add(_horizontal); panel.Children.Add(_vertical); panel.Children.Add(_borderWidth); panel.Children.Add(_dash);
        panel.Children.Add(new TextBlock { Text = "All selected borders", FontSize = 12 }); var borders = new ColorPalette(); borders.ColorSelected += (_, color) => Change(t => TableModel.EditCells(t, _range, c =>
        { var border = new TableBorder { Color = color, Width = Number(_borderWidth), Dash = (TableBorderDash)Math.Max(0, _dash.SelectedIndex) }; return c with { Left = border, Right = border, Top = border, Bottom = border }; })); panel.Children.Add(borders);
        panel.Children.Add(Button("No selected borders", () => Change(t => TableModel.EditCells(t, _range, c => c with { Left = new() { Width = 0 }, Right = new() { Width = 0 }, Top = new() { Width = 0 }, Bottom = new() { Width = 0 } }))));
        _header.Click += (_, _) => Change(t => t with { HeaderRow = _header.IsChecked == true });
        _bands.Click += (_, _) => Change(t => t with { BandedRows = _bands.IsChecked == true });
        _total.Click += (_, _) => Change(t => t with { TotalRow = _total.IsChecked == true });
        _horizontal.SelectionChanged += (_, _) => { if (!_loading && _horizontal.SelectedIndex >= 0) FormatText(s => s with { Alignment = (ParagraphAlignment)_horizontal.SelectedIndex }); };
        _vertical.SelectionChanged += (_, _) => { if (!_loading && _vertical.SelectedIndex >= 0) FormatText(s => s with { VerticalAlignment = (Core.VerticalAlignment)_vertical.SelectedIndex }); };
        _text.KeyDown += (_, e) => { if (e.Key == VirtualKey.Enter && Key(VirtualKey.Control)) { ApplyText(); e.Handled = true; } };
        AutomationProperties.SetName(_text, "Table cell text"); AutomationProperties.SetAutomationId(_text, "table-cell-text");
        Content = panel; SetValue(_value);
    }
    private static bool Key(VirtualKey key) => (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key) & CoreVirtualKeyStates.Down) != 0;
    private static Button Button(string title, Action action)
    {
        var button = new Button { Content = title, FontSize = 11, Padding = new(7, 5, 7, 5), MinHeight = 28 }; AutomationProperties.SetName(button, title);
        button.Click += (_, _) => action(); return button;
    }
    private static float Number(TextBox box) => float.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) && float.IsFinite(value) ? value : throw new InvalidDataException("Enter a finite number using a decimal point.");
    public void SetValue(TableSpec value)
    {
        TableModel.Validate(value); _value = value; _row = Math.Clamp(_row, 0, value.RowCount - 1); _column = Math.Clamp(_column, 0, value.ColumnCount - 1);
        _range = TableModel.ExpandRange(value, new(Math.Min(_range.Row, value.RowCount - 1), Math.Min(_range.Column, value.ColumnCount - 1), Math.Clamp(_range.RowCount, 1, value.RowCount - Math.Min(_range.Row, value.RowCount - 1)), Math.Clamp(_range.ColumnCount, 1, value.ColumnCount - Math.Min(_range.Column, value.ColumnCount - 1))));
        Synchronize();
    }
    public void FocusText() { _text.Focus(FocusState.Programmatic); _text.SelectAll(); }
    public void SelectCell(int row, int column, bool extend = false)
    {
        var cell = TableModel.CellAt(_value, row, column);
        if (extend) _range = TableModel.ExpandRange(_value, new(Math.Min(_row, cell.Row), Math.Min(_column, cell.Column), Math.Max(_row + 1, cell.Row + cell.RowSpan) - Math.Min(_row, cell.Row), Math.Max(_column + 1, cell.Column + cell.ColumnSpan) - Math.Min(_column, cell.Column)));
        else { _row = cell.Row; _column = cell.Column; _range = new(cell.Row, cell.Column, cell.RowSpan, cell.ColumnSpan); }
        Synchronize();
    }
    public void SelectRow() { _range = TableModel.ExpandRange(_value, new(_row, 0, 1, _value.ColumnCount)); Synchronize(); }
    public void SelectColumn() { _range = TableModel.ExpandRange(_value, new(0, _column, _value.RowCount, 1)); Synchronize(); }
    public void SelectAllCells() { _range = new(0, 0, _value.RowCount, _value.ColumnCount); Synchronize(); }
    public void MergeSelection() => Change(t => TableModel.Merge(t, _range));
    public void SplitCell() => Change(t => TableModel.Split(t, _row, _column));
    public void InsertRow() => Change(t => TableModel.InsertTrack(t, _row + 1, true));
    public void InsertColumn() => Change(t => TableModel.InsertTrack(t, _column + 1, false));
    public void DeleteRow() => Change(t => TableModel.DeleteTrack(t, _row, true));
    public void DeleteColumn() => Change(t => TableModel.DeleteTrack(t, _column, false));
    public void ApplyText() => Change(t => t);
    public void FormatText(Func<TextStyle, TextStyle> change) => Change(t => TableModel.EditCells(t, _range, c =>
    {
        var shape = TableModel.TextShape(t, c); var modified = RichText.Format(shape, 0, shape.Text.Length, change);
        return c with { TextStyle = change(shape.TextStyle), TextRanges = modified.TextRanges };
    }));
    private void Change(Func<TableSpec, TableSpec> action)
    {
        if (_loading || !IsEnabled) return;
        try
        {
            var cell = TableModel.CellAt(_value, _row, _column); var next = action(cell.Text == _text.Text ? _value : TableModel.SetText(_value, _row, _column, _text.Text));
            TableModel.Validate(next); if (ReferenceEquals(next, _value)) return;
            SetValue(next); _error.Text = ""; ValueChanged?.Invoke(this, next);
        }
        catch (Exception e) when (e is InvalidDataException or InvalidOperationException or ArgumentException) { _error.Text = e.Message; }
    }
    private void Page(int rows, int columns) { _pageRow = Math.Clamp(_pageRow + rows, 0, Math.Max(0, _value.RowCount - 1)); _pageColumn = Math.Clamp(_pageColumn + columns, 0, Math.Max(0, _value.ColumnCount - 1)); BuildGrid(); }
    private void Synchronize()
    {
        _loading = true;
        try
        {
            var cell = TableModel.CellAt(_value, _row, _column); var style = TableModel.Style(_value, cell); _text.Text = cell.Text;
            _rowSize.Text = _value.RowHeights[_row].ToString("0.##", CultureInfo.InvariantCulture); _columnSize.Text = _value.ColumnWidths[_column].ToString("0.##", CultureInfo.InvariantCulture); _fontSize.Text = style.FontSize.ToString("0.##", CultureInfo.InvariantCulture);
            _horizontal.SelectedIndex = (int)style.Alignment; _vertical.SelectedIndex = (int)style.VerticalAlignment;
            _header.IsChecked = _value.HeaderRow; _bands.IsChecked = _value.BandedRows; _total.IsChecked = _value.TotalRow;
            _selection.Text = $"{_value.RowCount} rows × {_value.ColumnCount} columns · R{_range.Row + 1}C{_range.Column + 1} : R{_range.Bottom}C{_range.Right}";
            _pageRow = _row / PageRows * PageRows; _pageColumn = _column / PageColumns * PageColumns; BuildGrid();
        }
        finally { _loading = false; }
    }
    private void BuildGrid()
    {
        _grid.Children.Clear(); _grid.RowDefinitions.Clear(); _grid.ColumnDefinitions.Clear();
        int rows = Math.Min(PageRows, _value.RowCount - _pageRow), columns = Math.Min(PageColumns, _value.ColumnCount - _pageColumn);
        _grid.ColumnDefinitions.Add(new() { Width = new GridLength(24) });
        for (int c = 0; c < columns; c++) _grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        for (int r = 0; r <= rows; r++) _grid.RowDefinitions.Add(new() { Height = new GridLength(r == 0 ? 25 : 34) });
        void Add(UIElement item, int r, int c) { Grid.SetRow(item, r); Grid.SetColumn(item, c); _grid.Children.Add(item); }
        for (int c = 0; c < columns; c++) { int column = _pageColumn + c; Add(Button((column + 1).ToString(), () => { _column = column; SelectColumn(); }), 0, c + 1); }
        for (int r = 0; r < rows; r++) { int row = _pageRow + r; Add(Button((row + 1).ToString(), () => { _row = row; SelectRow(); }), r + 1, 0); }
        foreach (var cell in _value.Cells)
        {
            int r = Math.Max(cell.Row, _pageRow), c = Math.Max(cell.Column, _pageColumn), bottom = Math.Min(cell.Row + cell.RowSpan, _pageRow + rows), right = Math.Min(cell.Column + cell.ColumnSpan, _pageColumn + columns);
            if (r >= bottom || c >= right) continue;
            var button = Button(cell.Text.Length == 0 ? "·" : cell.Text.Split('\n')[0], () => SelectCell(cell.Row, cell.Column, Key(VirtualKey.Shift)));
            button.Content = new TextBlock { Text = cell.Text.Length == 0 ? "·" : cell.Text.Split('\n')[0], FontSize = 10, TextTrimming = TextTrimming.CharacterEllipsis };
            button.Padding = new(3); button.HorizontalAlignment = HorizontalAlignment.Stretch; button.VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Stretch;
            button.Background = OfficePalette.Brush(TableModel.Fill(_value, cell)); button.Foreground = OfficePalette.Brush(TableModel.Style(_value, cell).Color);
            button.BorderThickness = new(_range.Intersects(cell) ? 2 : 1); button.BorderBrush = _range.Intersects(cell) ? OfficePalette.Accent : OfficePalette.Line;
            AutomationProperties.SetName(button, $"Table cell row {cell.Row + 1}, column {cell.Column + 1}, {cell.RowSpan} by {cell.ColumnSpan}, {cell.Text}");
            button.KeyDown += (_, e) =>
            {
                int rr = cell.Row, cc = cell.Column;
                switch (e.Key) { case VirtualKey.Left: cc--; break; case VirtualKey.Right: cc += cell.ColumnSpan; break; case VirtualKey.Up: rr--; break; case VirtualKey.Down: rr += cell.RowSpan; break; case VirtualKey.Enter: case VirtualKey.F2: FocusText(); e.Handled = true; return; default: return; }
                if (rr >= 0 && rr < _value.RowCount && cc >= 0 && cc < _value.ColumnCount) { SelectCell(rr, cc, Key(VirtualKey.Shift)); FocusSelectedButton(); } e.Handled = true;
            };
            button.Tag = (cell.Row, cell.Column); Grid.SetRowSpan(button, bottom - r); Grid.SetColumnSpan(button, right - c); Add(button, r - _pageRow + 1, c - _pageColumn + 1);
        }
    }
    private void FocusSelectedButton() => _grid.Children.OfType<Button>().FirstOrDefault(b => b.Tag is ValueTuple<int, int> p && p.Item1 == _row && p.Item2 == _column)?.Focus(FocusState.Programmatic);
}
