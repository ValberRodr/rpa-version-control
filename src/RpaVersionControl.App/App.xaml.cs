using Microsoft.UI.Xaml;
using RpaVersionControl.App.Services;

namespace RpaVersionControl.App;

public partial class App : Application
{
    public static new App Current => (App)Application.Current;
    public AppServices Services { get; }
    public MainWindow? MainWindow { get; private set; }

    public App()
    {
        InitializeComponent();
        Services = new AppServices();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new MainWindow();
        MainWindow.Activate();
        _ = Services.InitializeAsync(MainWindow);
    }
}
