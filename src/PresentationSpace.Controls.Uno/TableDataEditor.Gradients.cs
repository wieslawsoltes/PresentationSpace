using Microsoft.UI.Xaml.Controls;
using PresentationSpace.Core;

namespace PresentationSpace.Controls.Uno;

public sealed partial class TableDataEditor
{
    private readonly GradientFillEditor _cellGradient = new();
    public void ApplyGradient(GradientFill? value) => Change(t => TableModel.EditCells(t, _range, c => c with { FillGradient = value }));
    public void FocusGradient() => _cellGradient.FocusStops();
    private void BuildCellGradient(StackPanel panel)
    {
        panel.Children.Add(new TextBlock { Text = "Selected cell gradient", FontSize = 12 });
        _cellGradient.ValueChanged += (_, value) => ApplyGradient(value);
        panel.Children.Add(_cellGradient);
    }
}
