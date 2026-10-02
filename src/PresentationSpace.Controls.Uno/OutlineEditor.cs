using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using PresentationSpace.Core;
using PresentationSpace.Ribbon.Uno;

namespace PresentationSpace.Controls.Uno;

/// <summary>Session-independent whole-draft outline authoring. Gradient editing is a separate operation; changing other fields preserves it.</summary>
public sealed class OutlineEditor : UserControl
{
    private readonly StackPanel _body = new() { Spacing = 7 };
    private readonly TextBox _color = new() { Header = "Color (#RRGGBB or #AARRGGBB)", MinWidth = 0, FontSize = 12 };
    private readonly TextBox _width = new() { Header = "Width (slide units)", MinWidth = 0, FontSize = 12 };
    private readonly TextBox _miter = new() { Header = "Miter limit (1–100)", MinWidth = 0, FontSize = 12 };
    private readonly TextBox _custom = new() { Header = "Custom dash / gap (width multiples)", AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, MinWidth = 0, MinHeight = 70, FontSize = 11 };
    private readonly ComboBox _dash = Choice<StrokeDash>("Dash preset"), _cap = Choice<StrokeCap>("Caps"), _join = Choice<StrokeJoin>("Joins");
    private readonly ComboBox _begin = Choice<LineEndKind>("Begin arrow"), _end = Choice<LineEndKind>("End arrow");
    private readonly ComboBox _beginWidth = Choice<LineEndSize>("Begin width"), _beginLength = Choice<LineEndSize>("Begin length");
    private readonly ComboBox _endWidth = Choice<LineEndSize>("End width"), _endLength = Choice<LineEndSize>("End length");
    private readonly StackPanel _ends = new() { Spacing = 7 };
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = OfficePalette.Brush("A4262C") };
    private StrokeSettings? _value;
    private bool _applying;
    public event EventHandler<StrokeSettings>? ValueChanged;
    private static ComboBox Choice<T>(string header) where T : struct, Enum => new()
    { Header = header, ItemsSource = Enum.GetNames<T>(), MinWidth = 0, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
    public OutlineEditor()
    {
        Content = _body; AutomationProperties.SetAutomationId(this, "outline-editor");
        AutomationProperties.SetAutomationId(_width, "outline-width"); AutomationProperties.SetAutomationId(_color, "outline-color");
        AutomationProperties.SetAutomationId(_custom, "outline-dashes"); AutomationProperties.SetAutomationId(_miter, "outline-miter");
        _body.Children.Add(_color); Pair(_width, _miter); _body.Children.Add(_dash); Pair(_cap, _join);
        _body.Children.Add(new TextBlock { Text = "Custom pairs override the preset. One pair per line, e.g. 4 2. Clear all pairs to use the preset. A zero-width outline is hidden.", FontSize = 11, TextWrapping = TextWrapping.Wrap });
        _body.Children.Add(_custom); _body.Children.Add(_ends);
        _ends.Children.Add(_begin); _ends.Children.Add(_beginWidth); _ends.Children.Add(_beginLength);
        _ends.Children.Add(_end); _ends.Children.Add(_endWidth); _ends.Children.Add(_endLength);
        foreach (var field in new[] { _color, _width, _miter }) field.KeyDown += (_, e) =>
        { if (e.Key == Windows.System.VirtualKey.Enter) { Apply(); e.Handled = true; } };
        _custom.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter && Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
            { Apply(); e.Handled = true; }
        };
        var apply = new Button { Content = "Apply outline", FontSize = 12, MinWidth = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetAutomationId(apply, "outline-apply"); apply.Click += (_, _) => Apply(); _body.Children.Add(apply); _body.Children.Add(_error);
    }
    private void Pair(UIElement first, UIElement second)
    {
        var row = new Grid { ColumnSpacing = 8, ColumnDefinitions = { new() { Width = new GridLength(1, GridUnitType.Star) }, new() { Width = new GridLength(1, GridUnitType.Star) } } };
        row.Children.Add(first); Grid.SetColumn(second, 1); row.Children.Add(second); _body.Children.Add(row);
    }
    public void SetValue(StrokeSettings value, bool showEnds = true)
    {
        StrokeModel.Validate(value); _value = value; var s = value.Style;
        _color.Text = value.Color; _width.Text = value.Width.ToString("R", CultureInfo.InvariantCulture);
        _miter.Text = s.MiterLimit.ToString("R", CultureInfo.InvariantCulture); _custom.Text = StrokeModel.FormatDashes(s.CustomDashes);
        _dash.SelectedItem = s.Dash.ToString(); _cap.SelectedItem = s.Cap.ToString(); _join.SelectedItem = s.Join.ToString();
        _begin.SelectedItem = s.Begin.Kind.ToString(); _beginWidth.SelectedItem = s.Begin.Width.ToString(); _beginLength.SelectedItem = s.Begin.Length.ToString();
        _end.SelectedItem = s.End.Kind.ToString(); _endWidth.SelectedItem = s.End.Width.ToString(); _endLength.SelectedItem = s.End.Length.ToString();
        _ends.Visibility = showEnds ? Visibility.Visible : Visibility.Collapsed; _error.Text = "";
    }
    public void FocusWidth() { _width.Focus(FocusState.Programmatic); _width.SelectAll(); }
    public void FocusDashes() { _custom.Focus(FocusState.Programmatic); _custom.SelectAll(); }
    public bool Apply()
    {
        if (!IsEnabled || _applying || _value is null) return false;
        _applying = true;
        try
        {
            float Number(TextBox field, double min, double max)
            {
                if (!double.TryParse(field.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value) || value < min || value > max)
                    throw new InvalidDataException("Enter a valid " + field.Header + ".");
                return (float)value;
            }
            T EnumValue<T>(ComboBox field) where T : struct, Enum => Enum.Parse<T>((string)field.SelectedItem);
            var style = _value.Style with { Dash = EnumValue<StrokeDash>(_dash), Cap = EnumValue<StrokeCap>(_cap), Join = EnumValue<StrokeJoin>(_join),
                MiterLimit = Number(_miter, 1, 100), CustomDashes = StrokeModel.ParseDashes(_custom.Text),
                Begin = new() { Kind = EnumValue<LineEndKind>(_begin), Width = EnumValue<LineEndSize>(_beginWidth), Length = EnumValue<LineEndSize>(_beginLength) },
                End = new() { Kind = EnumValue<LineEndKind>(_end), Width = EnumValue<LineEndSize>(_endWidth), Length = EnumValue<LineEndSize>(_endLength) } };
            var next = new StrokeSettings(_color.Text.Trim(), Number(_width, 0, 1000), style); StrokeModel.Validate(next);
            if (next.Color != _value.Color || next.Width != _value.Width || !StrokeModel.Equivalent(next.Style, _value.Style)) ValueChanged?.Invoke(this, next);
            _value = next; _error.Text = ""; return true;
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException or InvalidOperationException)
        { _error.Text = error.Message; return false; }
        finally { _applying = false; }
    }
}
