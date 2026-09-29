using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using PresentationSpace.Core;
using PresentationSpace.Ribbon.Uno;

namespace PresentationSpace.Controls.Uno;

/// <summary>Session-independent, explicit-apply text-box and whole-object paragraph editor.</summary>
public sealed class TextLayoutEditor : UserControl
{
    private readonly StackPanel _body = new() { Spacing = 8 };
    private readonly Dictionary<string, TextBox> _fields = [];
    private readonly CheckBox _wrap = new() { Content = "Wrap text in shape", IsChecked = true, MinWidth = 0, MinHeight = 28, FontSize = 12 };
    private readonly ComboBox _alignment = new() { Header = "Paragraph alignment", ItemsSource = Enum.GetNames<ParagraphAlignment>(), MinWidth = 0, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = OfficePalette.Brush("A4262C") };
    private SlideShape? _source;
    public event EventHandler<SlideShape>? ValueChanged;
    public TextLayoutEditor()
    {
        Content = _body;
        AutomationProperties.SetAutomationId(this, "text-layout-editor");
        var hint = OfficePalette.Text("Slide units. Apply to this whole object's paragraphs; character styles are retained. Blank indent or exact spacing uses its default.", 11);
        hint.TextWrapping = TextWrapping.Wrap; _body.Children.Add(hint);
        _body.Children.Add(_wrap); AutomationProperties.SetAutomationId(_wrap, "text-layout-wrap");
        Pair("Left margin", "left", "Right margin", "right");
        Pair("Top margin", "top", "Bottom margin", "bottom");
        _body.Children.Add(_alignment);
        Pair("Space before", "before", "Space after", "after");
        Pair("Left indent", "indent-left", "Right indent", "indent-right");
        Pair("First line / marker", "first", "Tab interval", "tab");
        Pair("Line multiple", "multiple", "Exact line advance", "exact");
        var apply = new Button { Content = "Apply text layout", MinWidth = 0, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetAutomationId(apply, "text-layout-apply"); apply.Click += (_, _) => Apply();
        _body.Children.Add(apply); _body.Children.Add(_error);
    }
    private void Pair(string label, string key, string label2, string key2)
    {
        var row = new Grid { ColumnSpacing = 8, ColumnDefinitions = { new() { Width = new GridLength(1, GridUnitType.Star) }, new() { Width = new GridLength(1, GridUnitType.Star) } } };
        TextBox Field(string name, string id)
        {
            var input = new TextBox { Header = name, MinWidth = 0, MinHeight = 30, FontSize = 11, Padding = new(6, 4, 6, 4) };
            AutomationProperties.SetAutomationId(input, "text-layout-" + id); AutomationProperties.SetName(input, name);
            input.KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Enter) { Apply(); e.Handled = true; } };
            _fields.Add(id, input); return input;
        }
        row.Children.Add(Field(label, key)); var second = Field(label2, key2); Grid.SetColumn(second, 1); row.Children.Add(second); _body.Children.Add(row);
    }
    public void SetValue(SlideShape shape)
    {
        ArgumentNullException.ThrowIfNull(shape); _source = shape;
        var box = TextBoxModel.Resolve(shape); var style = RichText.StyleAt(shape, 0);
        void Set(string key, float? value) => _fields[key].Text = value?.ToString("R", CultureInfo.InvariantCulture) ?? "";
        Set("left", box.MarginLeft); Set("right", box.MarginRight); Set("top", box.MarginTop); Set("bottom", box.MarginBottom);
        Set("before", style.SpaceBefore); Set("after", style.SpaceAfter); Set("indent-left", style.ParagraphLeftMargin);
        Set("indent-right", style.ParagraphRightMargin); Set("first", style.ParagraphIndent); Set("tab", style.DefaultTabSize);
        Set("multiple", style.LineSpacing); Set("exact", style.LineSpacingPoints);
        _wrap.IsChecked = box.Wrap; _alignment.SelectedItem = style.Alignment.ToString(); _error.Text = "";
    }
    public void FocusFirstField() { _fields["left"].Focus(FocusState.Programmatic); _fields["left"].SelectAll(); }
    public bool Apply()
    {
        if (!IsEnabled || _source is not { } source) return false;
        try
        {
            float? Read(string key, bool optional = false)
            {
                string value = _fields[key].Text.Trim();
                if (optional && value.Length == 0) return null;
                if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) || !float.IsFinite(parsed))
                    throw new InvalidDataException("Enter a finite number for " + _fields[key].Header + ".");
                return parsed;
            }
            var box = new TextBoxSpec { MarginLeft = Read("left")!.Value, MarginRight = Read("right")!.Value,
                MarginTop = Read("top")!.Value, MarginBottom = Read("bottom")!.Value, Wrap = _wrap.IsChecked == true };
            var style = source.TextStyle with { SpaceBefore = Read("before")!.Value, SpaceAfter = Read("after")!.Value,
                ParagraphLeftMargin = Read("indent-left", true), ParagraphRightMargin = Read("indent-right")!.Value,
                ParagraphIndent = Read("first", true), DefaultTabSize = Read("tab")!.Value,
                LineSpacing = Read("multiple")!.Value, LineSpacingPoints = Read("exact", true),
                Alignment = Enum.Parse<ParagraphAlignment>((string)_alignment.SelectedItem) };
            TextBoxModel.Validate(box); TextFlow.ValidateStyle(style);
            var next = TextBoxModel.ApplyLayout(source, box, style);
            ValueChanged?.Invoke(this, next); _source = next; _error.Text = ""; return true;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or InvalidDataException)
        { _error.Text = error.Message; return false; }
    }
}
