using System.Globalization;
using PresentationSpace.Core;

namespace PresentationSpace.App;

public sealed partial class MainPage
{
    private void UpdateGradientDiagnostics()
    {
#if __WASM__
        var session = _editor.Session;
        var gradient = session.PrimaryShape is { } shape ? shape.FillGradient : session.CurrentSlide.BackgroundGradient;
        string stops = Uri.EscapeDataString(gradient is null ? "" : GradientModel.FormatStops(gradient.Stops));
        string angle = gradient?.Angle.ToString("R", CultureInfo.InvariantCulture) ?? "";
        int cells = session.PrimaryShape?.Table?.Cells.Count(c => c.FillGradient is not null) ?? 0;
        global::Uno.Foundation.WebAssemblyRuntime.InvokeJS($"document.documentElement.setAttribute('data-gradient-count','{gradient?.Stops.Length ?? 0}');document.documentElement.setAttribute('data-gradient-angle','{angle}');document.documentElement.setAttribute('data-gradient-stops',decodeURIComponent('{stops}'));document.documentElement.setAttribute('data-background-gradient','{(session.CurrentSlide.BackgroundGradient is not null ? "true" : "false")}');document.documentElement.setAttribute('data-table-gradients','{cells}');");
#endif
    }
}
