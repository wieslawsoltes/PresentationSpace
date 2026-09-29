using System.Globalization;
namespace PresentationSpace.App;

public sealed partial class MainPage
{
    private void UpdateTextDiagnostics()
    {
#if __WASM__
        var session = _editor.Session;
        string Number(float? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "";
        string name = Uri.EscapeDataString(session.CurrentSlide.Name);
        global::Uno.Foundation.WebAssemblyRuntime.InvokeJS($"document.documentElement.setAttribute('data-primary-font-size','{Number(session.PrimaryShape?.TextStyle.FontSize)}');document.documentElement.setAttribute('data-primary-first-run-size','{Number(session.PrimaryShape?.TextRanges.FirstOrDefault()?.Style.FontSize)}');document.documentElement.setAttribute('data-slide-width','{Number(session.Document.Width)}');document.documentElement.setAttribute('data-slide-height','{Number(session.Document.Height)}');document.documentElement.setAttribute('data-slide-name',decodeURIComponent('{name}'));");
#endif
    }
}
