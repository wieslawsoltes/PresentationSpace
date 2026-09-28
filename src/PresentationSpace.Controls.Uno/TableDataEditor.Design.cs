using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PresentationSpace.Core;

namespace PresentationSpace.Controls.Uno;

public sealed partial class TableDataEditor
{
    private readonly CheckBox _firstColumn = new() { Content = "First column emphasis" }, _lastColumn = new() { Content = "Last column emphasis" }, _bandColumns = new() { Content = "Banded columns" };
    private readonly ComboBox _borderScope = new() { Header = "Borders to apply", ItemsSource = Enum.GetNames<TableBorderScope>(), SelectedIndex = 0, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
    private string _borderColor = "#243247";
    private void BuildDesignControls(StackPanel panel)
    {
        panel.Children.Add(_firstColumn); panel.Children.Add(_lastColumn); panel.Children.Add(_bandColumns);
        _firstColumn.Click += (_, _) => Change(t => t with { FirstColumn = _firstColumn.IsChecked == true });
        _lastColumn.Click += (_, _) => Change(t => t with { LastColumn = _lastColumn.IsChecked == true });
        _bandColumns.Click += (_, _) => Change(t => t with { BandedColumns = _bandColumns.IsChecked == true });
        var presets = new Grid { ColumnSpacing = 4, RowSpacing = 4 };
        for (int i = 0; i < 2; i++) { presets.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); presets.RowDefinitions.Add(new() { Height = GridLength.Auto }); }
        foreach (var preset in Enum.GetValues<TableStylePreset>())
        {
            var value = preset;
            var button = Button(preset + " table style", () => ApplyTableStyle(value));
            button.Content = new TextBlock { Text = preset.ToString(), FontSize = 11 };
            Grid.SetRow(button, (int)preset / 2); Grid.SetColumn(button, (int)preset % 2); presets.Children.Add(button);
        }
        panel.Children.Add(presets);
    }
    public void SelectRange(TableRange range)
    {
        _range = TableModel.ExpandRange(_value, range); _row = _range.Row; _column = _range.Column; Synchronize();
    }
    public void ApplyTableStyle(TableStylePreset preset) => Change(t => TableModel.ApplyStyle(t, preset));
    public void ToggleFirstColumn() => Change(t => t with { FirstColumn = !t.FirstColumn });
    public void ToggleLastColumn() => Change(t => t with { LastColumn = !t.LastColumn });
    public void ToggleBandedColumns() => Change(t => t with { BandedColumns = !t.BandedColumns });
    public void ApplyBorderScope(TableBorderScope scope) => Change(t => TableModel.SetBorders(t, _range, scope,
        new TableBorder { Color = _borderColor, Width = Number(_borderWidth), Dash = (TableBorderDash)Math.Max(0, _dash.SelectedIndex) }));
}
