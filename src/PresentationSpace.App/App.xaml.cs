using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PresentationSpace.Ribbon.Uno;
namespace PresentationSpace.App;

public partial class App : Application
{
    public Window? MainWindow { get; private set; }
    public App() { InitializeComponent(); RequestedTheme = ApplicationTheme.Light; }
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var loading = new StackPanel { Spacing = 18, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        loading.Children.Add(new ProgressRing { IsActive = true, Width = 36, Height = 36 });
        loading.Children.Add(OfficePalette.Text("Opening PresentationSpace…", 16, true));
        MainWindow = new Window { Title = "PresentationSpace", Content = loading };
        MainWindow.Activate();
        try
        {
            await ApplicationFonts.InitializeAsync();
            MainWindow.Content = new MainPage();
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            MainWindow.Content = new TextBlock { Text = "PresentationSpace could not start.\n\n" + error.Message, TextWrapping = TextWrapping.Wrap, Margin = new(40) };
#if __WASM__
            global::Uno.Foundation.WebAssemblyRuntime.InvokeJS("document.documentElement.setAttribute('data-startup-error',decodeURIComponent('" + Uri.EscapeDataString(error.Message) + "'))");
#endif
        }
    }
}
