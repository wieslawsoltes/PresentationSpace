namespace PresentationSpace.App;

public sealed partial class MainPage
{
    private string? _lastChromeDiagnostics;
    private long _chromeLayoutVersion;

    // Model changes are dispatched before XAML necessarily finishes arranging children.
    // Refresh geometry after layout as well, without rescanning document/table content,
    // forcing UpdateLayout(), or introducing a perpetual rendering/timer subscription.
    private void OnLayoutUpdated(object? sender, object args)
    {
        if (_editor.IsLoaded) UpdateChromeDiagnostics();
    }

    private void UpdateChromeDiagnostics()
    {
#if __WASM__
        string snapshot = _editor.GetChromeDiagnostics();
        if (snapshot == _lastChromeDiagnostics) return;
        _lastChromeDiagnostics = snapshot;
        string encoded = Uri.EscapeDataString(snapshot);
        string version = (++_chromeLayoutVersion).ToString(System.Globalization.CultureInfo.InvariantCulture);
        global::Uno.Foundation.WebAssemblyRuntime.InvokeJS($"document.documentElement.setAttribute('data-ui-chrome',decodeURIComponent('{encoded}'));document.documentElement.setAttribute('data-ui-layout-version','{version}');");
#endif
    }
}
