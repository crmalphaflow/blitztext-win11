using System.Windows;

namespace BlitztextWindows;

public partial class App : System.Windows.Application
{
    private MainWindow? mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
        mainWindow = new MainWindow();
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        mainWindow?.Dispose();
        base.OnExit(e);
    }
}
