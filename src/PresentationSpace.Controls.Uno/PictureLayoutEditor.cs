using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using PresentationSpace.Core;
using PresentationSpace.Ribbon.Uno;

namespace PresentationSpace.Controls.Uno;

/// <summary>Session-independent, explicit-apply picture framing editor. Values are percentages, not pixels.</summary>
public sealed class PictureLayoutEditor : UserControl
{
    private readonly StackPanel _body = new() { Spacing = 8 };
    private readonly Dictionary<string, TextBox> _fields = [];
    private readonly ComboBox _fit = new() { Header = "Picture fitting", ItemsSource = Enum.GetNames<PictureFit>(), MinWidth = 0, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _mask = new() { Header = "Picture shape", ItemsSource = Enum.GetNames<PictureMask>(), MinWidth = 0, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly CheckBox _flipH = new() { Content = "Flip horizontally", MinWidth = 0, MinHeight = 28, FontSize = 12 };
    private readonly CheckBox _flipV = new() { Content = "Flip vertically", MinWidth = 0, MinHeight = 28, FontSize = 12 };
    private readonly TextBlock _error = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = OfficePalette.Brush("A4262C") };
    private PictureSpec? _value;
    private bool _applying;
    public event EventHandler<PictureSpec>? ValueChanged;
    public PictureLayoutEditor()
    {
        Content = _body; AutomationProperties.SetAutomationId(this, "picture-layout-editor");
        var hint = OfficePalette.Text("Non-destructive percentages. Positive crop values remove edges; negative values add transparent space. Apply all fields together.", 11);
        hint.TextWrapping = TextWrapping.Wrap; _body.Children.Add(hint);
        _body.Children.Add(_fit); _body.Children.Add(_mask);
        _body.Children.Add(_flipH); _body.Children.Add(_flipV);
        Pair("Crop left %", "crop-left", "Crop right %", "crop-right");
        Pair("Crop top %", "crop-top", "Crop bottom %", "crop-bottom");
        Pair("Frame left %", "frame-left", "Frame right %", "frame-right");
        Pair("Frame top %", "frame-top", "Frame bottom %", "frame-bottom");
        _body.Children.Add(Field("Picture opacity %", "opacity"));
        var apply = new Button { Content = "Apply picture layout", MinWidth = 0, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetAutomationId(apply, "picture-layout-apply"); apply.Click += (_, _) => Apply();
        _body.Children.Add(apply); _body.Children.Add(_error);
    }
    private TextBox Field(string label, string key)
    {
        var input = new TextBox { Header = label, MinWidth = 0, MinHeight = 30, FontSize = 11, Padding = new(6,4,6,4) };
        AutomationProperties.SetName(input, label); AutomationProperties.SetAutomationId(input, "picture-" + key);
        input.KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Enter) { Apply(); e.Handled = true; } };
        _fields.Add(key, input); return input;
    }
    private void Pair(string label, string key, string label2, string key2)
    {
        var row = new Grid { ColumnSpacing = 8, ColumnDefinitions = { new() { Width = new GridLength(1,GridUnitType.Star) }, new() { Width = new GridLength(1,GridUnitType.Star) } } };
        row.Children.Add(Field(label, key)); var second = Field(label2, key2); Grid.SetColumn(second,1); row.Children.Add(second); _body.Children.Add(row);
    }
    public void SetValue(PictureSpec picture)
    {
        PictureModel.Validate(picture); _value = picture; _fit.SelectedItem = picture.Fit.ToString(); _mask.SelectedItem = picture.Mask.ToString();
        _flipH.IsChecked = picture.FlipHorizontal; _flipV.IsChecked = picture.FlipVertical;
        void Set(string id, float number) => _fields[id].Text = (number * 100d).ToString("0.#####", CultureInfo.InvariantCulture);
        void Insets(string prefix, PictureInsets i) { Set(prefix+"left",i.Left); Set(prefix+"right",i.Right); Set(prefix+"top",i.Top); Set(prefix+"bottom",i.Bottom); }
        Insets("crop-",picture.Source); Insets("frame-",picture.Destination); Set("opacity",picture.Opacity); _error.Text = "";
    }
    public void FocusCrop() { _fields["crop-left"].Focus(FocusState.Programmatic); _fields["crop-left"].SelectAll(); }
    public bool Apply()
    {
        if (!IsEnabled || _applying || _value is null) return false;
        _applying = true;
        try
        {
            float Read(string key)
            {
                if (!float.TryParse(_fields[key].Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !float.IsFinite(value))
                    throw new InvalidDataException("Enter a finite percentage for " + _fields[key].Header + ".");
                return value / 100;
            }
            PictureInsets Insets(string prefix) => new(Read(prefix+"left"),Read(prefix+"top"),Read(prefix+"right"),Read(prefix+"bottom"));
            var next = new PictureSpec { Source = Insets("crop-"), Destination = Insets("frame-"), Opacity = Read("opacity"),
                Fit = Enum.Parse<PictureFit>((string)_fit.SelectedItem), Mask = Enum.Parse<PictureMask>((string)_mask.SelectedItem),
                FlipHorizontal = _flipH.IsChecked == true, FlipVertical = _flipV.IsChecked == true };
            PictureModel.Validate(next);
            if (next != _value) ValueChanged?.Invoke(this, next);
            _value = next; _error.Text = ""; return true;
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException or InvalidOperationException)
        { _error.Text = error.Message; return false; }
        finally { _applying = false; }
    }
}
