using Gmk87.App.Services;
using Microsoft.UI.Xaml;

namespace Gmk87.App;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) => File.AppendAllText(CrashLogPath, $"{DateTime.Now:O} {e.Exception}\n\n");
    }

    /// <summary>Unhandled exceptions are appended here (%TEMP%\GMK87Control-crash.log).</summary>
    public static string CrashLogPath { get; } = Path.Combine(Path.GetTempPath(), "GMK87Control-crash.log");

    public static MainWindow MainWindow { get; private set; } = null!;

    public static KeyboardService Keyboard { get; } = new();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new MainWindow();
        MainWindow.Activate();
    }
}
