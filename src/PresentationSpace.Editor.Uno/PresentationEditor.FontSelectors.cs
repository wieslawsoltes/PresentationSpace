using Microsoft.UI.Xaml.Controls;
using PresentationSpace.Core;

namespace PresentationSpace.Editor.Uno;

public sealed partial class PresentationEditor
{
    private static readonly string[] FontFamilyPresets = ["Arial", "Aptos", "Calibri", "Segoe UI", "Verdana", "Georgia", "Times New Roman", "Courier New"];
    private static readonly string[] FontSizePresets = ["12", "14", "16", "18", "20", "24", "28", "32", "36", "40", "44", "48", "54", "60", "72", "80", "96", "120"];

    // Auto-fit and imported fonts need not be in the preset galleries. A ComboBox
    // cannot reliably select a value absent from ItemsSource. Keep one bounded
    // custom option rather than showing the previous selection or mutating text.
    private void SynchronizeFontSelectors(TextStyle? style)
    {
        Select(_fontFamily, FontFamilyPresets, style?.FontFamily ?? "Arial");
        Select(_fontSize, FontSizePresets, (style?.FontSize ?? 28).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
        static void Select(ComboBox? box, string[] presets, string value)
        {
            if (box is null || box.SelectedItem as string == value) return;
            if (!box.Items.Contains(value)) box.ItemsSource = presets.Contains(value) ? presets : [.. presets, value];
            box.SelectedItem = value;
        }
    }
    /// <summary>The actual displayed font-size selection, rounded to two decimals.</summary>
    public string SelectedFontSizeLabel => _fontSize?.SelectedItem as string ?? "";
}
