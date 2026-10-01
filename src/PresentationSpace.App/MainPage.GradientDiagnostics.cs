using System.Globalization;
using PresentationSpace.Core;

namespace PresentationSpace.App;

public sealed partial class MainPage
{
#if __WASM__
    private GradientFill? _gradientSnapshot;
    private TableSpec? _gradientTableSnapshot;
    private string _gradientStops = "", _gradientAngle = "";
    private int _gradientCells;
#endif
    private void UpdateGradientDiagnostics()
    {
#if __WASM__
        var session = _editor.Session;
        var gradient = session.PrimaryShape is { } shape ? shape.FillGradient : session.CurrentSlide.BackgroundGradient;
        // Immutable fill/table identities survive geometry, selection and focus
        // updates. Do not reformat stops or scan thousands of unchanged cells.
        if (!ReferenceEquals(gradient, _gradientSnapshot))
        {
            _gradientSnapshot = gradient;
            _gradientStops = Uri.EscapeDataString(gradient is null ? "" : GradientModel.FormatStops(gradient.Stops));
            _gradientAngle = gradient?.Angle.ToString("R", CultureInfo.InvariantCulture) ?? "";
        }
        var table = session.PrimaryShape?.Table;
        if (!ReferenceEquals(table, _gradientTableSnapshot))
        {
            _gradientTableSnapshot = table;
            _gradientCells = table?.Cells.Count(c => c.FillGradient is not null) ?? 0;
        }
        global::Uno.Foundation.WebAssemblyRuntime.InvokeJS($"document.documentElement.setAttribute('data-gradient-count','{gradient?.Stops.Length ?? 0}');document.documentElement.setAttribute('data-gradient-angle','{_gradientAngle}');document.documentElement.setAttribute('data-gradient-stops',decodeURIComponent('{_gradientStops}'));document.documentElement.setAttribute('data-background-gradient','{(session.CurrentSlide.BackgroundGradient is not null ? "true" : "false")}');document.documentElement.setAttribute('data-table-gradients','{_gradientCells}');");
#endif
    }
}
