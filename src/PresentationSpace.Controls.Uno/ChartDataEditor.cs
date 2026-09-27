using System.Collections.Immutable;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using PresentationSpace.Core;
using PresentationSpace.Ribbon.Uno;
using Windows.System;
using Windows.UI.Core;

namespace PresentationSpace.Controls.Uno;

/// <summary>Reusable, session-independent chart editor. Commits validated immutable snapshots.</summary>
public sealed class ChartDataEditor : UserControl
{
    private readonly ComboBox _kind = new() { Header = "Chart type", ItemsSource = Enum.GetNames<ChartKind>(), HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _grouping = new() { Header = "Grouping", ItemsSource = new[] { "Clustered", "Stacked", "100% stacked" }, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _blanks = new() { Header = "Empty cells", ItemsSource = Enum.GetNames<ChartBlankMode>(), HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox _title = new() { Header = "Chart title", MaxLength = 32767 };
    private readonly CheckBox _legend = new() { Content = "Show legend" }, _values = new() { Content = "Show data labels" };
    private readonly TextBox _data = new() { Header = "Data · Ctrl+Enter to apply", AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, MinHeight = 150, MaxHeight = 260, MaxLength = ChartTabularData.MaxCharacters, FontSize = 12 };
    private readonly Slider _hole = new() { Header = "Doughnut hole (%)", Minimum = 10, Maximum = 90, StepFrequency = 1 };
    private readonly ComboBox _series = new() { Header = "Series color", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, Foreground = OfficePalette.Brush("B42318"), FontSize = 11, Visibility = Visibility.Collapsed };
    private ChartSpec _value = new() { Categories = ["Category 1"], Series = [new() { Values = [0] }] };
    private bool _loading;
    public ChartSpec Value => _value;
    public event EventHandler<ChartSpec>? ValueChanged;

    public ChartDataEditor()
    {
        var body = new StackPanel { Spacing = 9 };
        body.Children.Add(_kind); body.Children.Add(_grouping); body.Children.Add(_title);
        body.Children.Add(_legend); body.Children.Add(_values); body.Children.Add(_blanks); body.Children.Add(_hole);
        body.Children.Add(new TextBlock { Text = "Paste tab-separated cells: category labels in the first column, series names in the first row. Blank numbers remain missing. Quotes preserve tabs and line breaks inside labels. Apply commits the title, hole size and data; closing the pane discards unapplied changes.", TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = OfficePalette.Muted });
        body.Children.Add(_data); body.Children.Add(_error);
        var apply = new Button { Content = "Apply chart data", HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(apply, "Apply chart data"); apply.Click += (_, _) => Update(c => c); body.Children.Add(apply);
        body.Children.Add(_series); var palette = new ColorPalette(); body.Children.Add(palette);
        var add = new Button { Content = "Add series", HorizontalAlignment = HorizontalAlignment.Stretch };
        var remove = new Button { Content = "Remove selected series", HorizontalAlignment = HorizontalAlignment.Stretch };
        body.Children.Add(add); body.Children.Add(remove); Content = body;
        AutomationProperties.SetName(_data, "Chart data"); AutomationProperties.SetName(_kind, "Chart type");
        _kind.SelectionChanged += (_, _) => { if (!_loading && _kind.SelectedItem is string name) Update(c => c with { Kind = Enum.Parse<ChartKind>(name), Grouping = name is "Column" or "Bar" ? c.Grouping : ChartGrouping.Clustered }); };
        _grouping.SelectionChanged += (_, _) => { if (!_loading && _grouping.SelectedIndex >= 0) Update(c => c with { Grouping = (ChartGrouping)_grouping.SelectedIndex }); };
        _blanks.SelectionChanged += (_, _) => { if (!_loading && _blanks.SelectedIndex >= 0) Update(c => c with { Blanks = (ChartBlankMode)_blanks.SelectedIndex }); };
        _legend.Click += (_, _) => Update(c => c with { ShowLegend = _legend.IsChecked == true });
        _values.Click += (_, _) => Update(c => c with { ShowValues = _values.IsChecked == true });
        _data.KeyDown += OnDataKey;
        palette.ColorSelected += (_, color) => { int selected = _series.SelectedIndex; if (selected >= 0) Update(c => c with { Series = c.Series.SetItem(Math.Min(selected, c.Series.Length - 1), c.Series[Math.Min(selected, c.Series.Length - 1)] with { Color = color }) }); };
        add.Click += (_, _) => Update(c => c with { Series = c.Series.Add(new() { Name = $"Series {c.Series.Length + 1}", Color = ChartModel.PointColor(c.Series.Length), Values = Enumerable.Repeat<double?>(null, c.Categories.Length).ToImmutableArray() }) });
        remove.Click += (_, _) => { int selected = _series.SelectedIndex; if (selected >= 0) Update(c => c with { Series = c.Series.RemoveAt(Math.Min(selected, c.Series.Length - 1)) }); };
        SetValue(_value);
    }

    public void SetValue(ChartSpec value)
    {
        ChartModel.Validate(value); _value = value; _data.Text = ChartTabularData.Format(value); _title.Text = value.Title;
        Synchronize(); _error.Visibility = Visibility.Collapsed;
    }
    public void FocusData() { _data.Focus(FocusState.Programmatic); _data.SelectAll(); }
    private void Synchronize()
    {
        _loading = true;
        try
        {
            int selected = _series.SelectedIndex;
            _kind.SelectedItem = _value.Kind.ToString(); _grouping.SelectedIndex = (int)_value.Grouping;
            _grouping.IsEnabled = _value.Kind is ChartKind.Column or ChartKind.Bar;
            _hole.Value = _value.HoleSize; _hole.Visibility = _value.Kind == ChartKind.Doughnut ? Visibility.Visible : Visibility.Collapsed;
            _blanks.SelectedItem = _value.Blanks.ToString(); _legend.IsChecked = _value.ShowLegend; _values.IsChecked = _value.ShowValues;
            _series.ItemsSource = _value.Series.Select(s => s.Name).ToArray(); _series.SelectedIndex = Math.Clamp(selected, 0, _value.Series.Length - 1);
        }
        finally { _loading = false; }
    }
    private void Update(Func<ChartSpec, ChartSpec> change)
    {
        if (_loading) return;
        try
        {
            var next = change(ChartTabularData.Parse(_data.Text, _value) with { Title = _title.Text, HoleSize = (int)Math.Round(_hole.Value) }); ChartModel.Validate(next);
            _value = next; Synchronize(); _data.Text = ChartTabularData.Format(next); _error.Visibility = Visibility.Collapsed;
            ValueChanged?.Invoke(this, next);
        }
        catch (InvalidDataException e) { _error.Text = e.Message; _error.Visibility = Visibility.Visible; Synchronize(); }
    }
    private void OnDataKey(object sender, KeyRoutedEventArgs e)
    {
        bool control = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & CoreVirtualKeyStates.Down) != 0;
        if (control && e.Key == VirtualKey.Enter) { Update(c => c); e.Handled = true; }
    }
}
