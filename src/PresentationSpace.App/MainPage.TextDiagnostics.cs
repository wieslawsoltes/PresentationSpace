using System.Globalization;
namespace PresentationSpace.App;

public sealed partial class MainPage
{
    private void UpdateTextDiagnostics()
    {
#if __WASM__
        var session = _editor.Session;
        var box = session.PrimaryShape is { } selected ? PresentationSpace.Core.TextBoxModel.Resolve(selected) : null;
        var paragraph = session.PrimaryShape is { } primary ? PresentationSpace.Core.RichText.StyleAt(primary, 0) : null;
        global::Uno.Foundation.WebAssemblyRuntime.InvokeJS($"document.documentElement.setAttribute('data-text-wrap','{(box?.Wrap == true ? "true" : "false")}');document.documentElement.setAttribute('data-text-margin-left','{box?.MarginLeft.ToString(CultureInfo.InvariantCulture) ?? ""}');document.documentElement.setAttribute('data-paragraph-before','{paragraph?.SpaceBefore.ToString(CultureInfo.InvariantCulture) ?? ""}');document.documentElement.setAttribute('data-paragraph-after','{paragraph?.SpaceAfter.ToString(CultureInfo.InvariantCulture) ?? ""}');document.documentElement.setAttribute('data-paragraph-align','{paragraph?.Alignment.ToString() ?? ""}');");
        string Number(float? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "";
        string selectedSize = Uri.EscapeDataString(_editor.SelectedFontSizeLabel);
        string name = Uri.EscapeDataString(session.CurrentSlide.Name);
        global::Uno.Foundation.WebAssemblyRuntime.InvokeJS($"document.documentElement.setAttribute('data-ribbon-font-size',decodeURIComponent('{selectedSize}'));document.documentElement.setAttribute('data-primary-font-size','{Number(session.PrimaryShape?.TextStyle.FontSize)}');document.documentElement.setAttribute('data-primary-first-run-size','{Number(session.PrimaryShape?.TextRanges.FirstOrDefault()?.Style.FontSize)}');document.documentElement.setAttribute('data-slide-width','{Number(session.Document.Width)}');document.documentElement.setAttribute('data-slide-height','{Number(session.Document.Height)}');document.documentElement.setAttribute('data-slide-name',decodeURIComponent('{name}'));");
#endif
    }
}
