using Flight.Core.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;

namespace Flight.App;

public partial class App : Application
{
    private Window? _window;

    public IHost Host { get; }

    public App()
    {
        InitializeComponent();
        Host = AppHost.Build();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
