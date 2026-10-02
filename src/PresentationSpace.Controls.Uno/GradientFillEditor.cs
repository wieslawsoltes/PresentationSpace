using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using PresentationSpace.Core;
using PresentationSpace.Ribbon.Uno;

namespace PresentationSpace.Controls.Uno;

/// <summary>Explicit-apply, session-independent linear gradient authoring. Emits an immutable value, or null for solid fill.</summary>
public sealed class GradientFillEditor : UserControl
{
    private readonly CheckBox _enabled = new() { Content = "Use gradient fill", FontSize = 12, MinHeight = 28 };
    private readonly CheckBox _scaled = new() { Content = "Scale angle with shape", FontSize = 12, MinHeight = 28 };
    private readonly CheckBox _rotate = new() { Content = "Rotate with shape", FontSize = 12, MinHeight = 28 };
    private readonly TextBox _angle = new() { Header = "Angle (degrees)", MinWidth = 0, FontSize = 12 };
    private readonly TextBox _stops = new() { Header = "Position %   Color   Opacity %", AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, MinWidth = 0, MinHeight = 108, FontSize = 11 };
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = OfficePalette.Brush("A4262C") };
    private readonly StackPanel _options = new() { Spacing = 7 };
    private GradientFill? _value;
    private bool _applying;
    public event EventHandler<GradientFill?>? ValueChanged;
    public GradientFillEditor() : this("gradient") { }
    public GradientFillEditor(string automationPrefix)
    {
        AutomationProperties.SetAutomationId(this, automationPrefix + "-fill-editor");
        AutomationProperties.SetAutomationId(_angle, automationPrefix + "-angle");
        AutomationProperties.SetAutomationId(_stops, automationPrefix + "-stops");
        AutomationProperties.SetName(_stops, "Gradient stops: position, RGB color and opacity");
        var body = new StackPanel { Spacing = 7 };
        var hint = new TextBlock { Text = "One stop per line: 0 #D35230 100. Positions and opacity are percentages. Equal positions create a sharp color boundary.", FontSize = 11, TextWrapping = TextWrapping.Wrap };
        body.Children.Add(_enabled); body.Children.Add(_options);
        _options.Children.Add(_angle); _options.Children.Add(_scaled); _options.Children.Add(_rotate); _options.Children.Add(hint); _options.Children.Add(_stops);
        _enabled.Checked += (_, _) => _options.Visibility = Visibility.Visible;
        _enabled.Unchecked += (_, _) => _options.Visibility = Visibility.Collapsed;
        var apply = new Button { Content = "Apply gradient fill", MinWidth = 0, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetAutomationId(apply, automationPrefix + "-apply"); apply.Click += (_, _) => Apply();
        _angle.KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Enter) { Apply(); e.Handled = true; } };
        _stops.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter && Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
            { Apply(); e.Handled = true; }
        };
        body.Children.Add(apply); body.Children.Add(_error); Content = body;
    }
    public void SetValue(GradientFill? value)
    {
        if (value is not null) GradientModel.Validate(value);
        _value = value; var display = value ?? new();
        _enabled.IsChecked = value is not null; _options.Visibility = value is not null ? Visibility.Visible : Visibility.Collapsed;
        _angle.Text = display.Angle.ToString("R", CultureInfo.InvariantCulture);
        _scaled.IsChecked = display.Scaled; _rotate.IsChecked = display.RotateWithShape;
        _stops.Text = GradientModel.FormatStops(display.Stops); _error.Text = "";
    }
    public void FocusStops() { _enabled.IsChecked = true; _stops.Focus(FocusState.Programmatic); _stops.SelectAll(); }
    public void FocusAngle() { _enabled.IsChecked = true; _angle.Focus(FocusState.Programmatic); _angle.SelectAll(); }
    public bool Apply()
    {
        if (_applying || !IsEnabled) return false;
        _applying = true;
        try
        {
            GradientFill? next = null;
            if (_enabled.IsChecked == true)
            {
                if (!float.TryParse(_angle.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float angle)) throw new InvalidDataException("Enter a finite angle from 0 to less than 360.");
                next = new() { Angle = angle, Scaled = _scaled.IsChecked == true, RotateWithShape = _rotate.IsChecked == true, Stops = GradientModel.ParseStops(_stops.Text) };
                GradientModel.Validate(next);
            }
            if (!GradientModel.Equivalent(_value, next)) ValueChanged?.Invoke(this, next);
            _value = next; _error.Text = ""; return true;
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException or InvalidOperationException)
        { _error.Text = error.Message; return false; }
        finally { _applying = false; }
    }
}
