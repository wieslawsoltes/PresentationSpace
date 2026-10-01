using System.Globalization;
using PresentationSpace.Core;

namespace PresentationSpace.App;

public sealed partial class MainPage
{
    private void UpdatePictureDiagnostics()
    {
#if __WASM__
        var shape = _editor.Session.PrimaryShape;
        var picture = shape?.Kind == ShapeKind.Image ? PictureModel.Resolve(shape) : null;
        string Number(float? value) => value?.ToString("R",CultureInfo.InvariantCulture) ?? "";
        global::Uno.Foundation.WebAssemblyRuntime.InvokeJS($"document.documentElement.setAttribute('data-picture-fit','{picture?.Fit.ToString() ?? ""}');document.documentElement.setAttribute('data-picture-mask','{picture?.Mask.ToString() ?? ""}');document.documentElement.setAttribute('data-picture-opacity','{Number(picture?.Opacity)}');document.documentElement.setAttribute('data-picture-flip-h','{(picture?.FlipHorizontal==true ? "true" : "false")}');document.documentElement.setAttribute('data-picture-flip-v','{(picture?.FlipVertical==true ? "true" : "false")}');document.documentElement.setAttribute('data-picture-crop-left','{Number(picture?.Source.Left)}');document.documentElement.setAttribute('data-picture-count','{_editor.Session.CurrentSlide.Shapes.Count(s=>s.Kind==ShapeKind.Image)}');");
#endif
    }
}
