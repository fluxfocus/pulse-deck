using System.Windows;

namespace PulseDeck.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var window = new MainWindow();
        MainWindow = window;
        window.Show();

        var vcdPaths = e.Args.Where(a => a.EndsWith(".vcd", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (vcdPaths.Length > 0)
            await window.LoadFilesFromArgsAsync(vcdPaths);
    }
}
