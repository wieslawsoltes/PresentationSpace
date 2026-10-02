using System.Globalization;
using PresentationSpace.Core;

namespace PresentationSpace.App;

public sealed partial class MainPage
{
#if __WASM__
    private StrokeSpec? _strokeSnapshot;
    private string _strokeDashes = "";
#endif
    private void UpdateStrokeDiagnostics()
    {
#if __WASM__
        var shape = _editor.Session.PrimaryShape; var style = shape is not null ? StrokeModel.Resolve(shape) : null;
        if (!ReferenceEquals(style, _strokeSnapshot))
        { _strokeSnapshot = style; _strokeDashes = Uri.EscapeDataString(style is null ? "" : StrokeModel.FormatDashes(style.CustomDashes)); }
        string width = shape?.StrokeWidth.ToString("R", CultureInfo.InvariantCulture) ?? "";
        global::Uno.Foundation.WebAssemblyRuntime.InvokeJS($"document.documentElement.setAttribute('data-outline-width','{width}');document.documentElement.setAttribute('data-outline-dash','{style?.Dash}');document.documentElement.setAttribute('data-outline-cap','{style?.Cap}');document.documentElement.setAttribute('data-outline-join','{style?.Join}');document.documentElement.setAttribute('data-outline-begin','{style?.Begin.Kind}');document.documentElement.setAttribute('data-outline-end','{style?.End.Kind}');document.documentElement.setAttribute('data-outline-custom',decodeURIComponent('{_strokeDashes}'));document.documentElement.setAttribute('data-outline-gradients','{style?.Gradient?.Stops.Length ?? 0}');document.documentElement.setAttribute('data-line-flip-h','{(shape?.LineDirection?.FlipHorizontal == true ? "true" : "false")}');document.documentElement.setAttribute('data-line-flip-v','{(shape?.LineDirection?.FlipVertical == true ? "true" : "false")}');");
#endif
    }
}
