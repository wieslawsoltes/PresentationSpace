using Microsoft.UI.Xaml;
namespace PresentationSpace.App;
public partial class App : Application
{
    public Window? MainWindow { get; private set; }
    public App() { InitializeComponent(); RequestedTheme = ApplicationTheme.Light; }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new Window { Title = "PresentationSpace", Content = new MainPage() };
        MainWindow.Activate();
    }
}
