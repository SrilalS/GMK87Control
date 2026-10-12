using Microsoft.UI.Xaml.Controls;

namespace Gmk87.App.Services;

/// <summary>App-wide transient notification, shown by <see cref="MainWindow"/> in the bottom-right corner.</summary>
public static class Toast
{
    internal static event Action<string, InfoBarSeverity>? Requested;

    public static void Show(string message, InfoBarSeverity severity = InfoBarSeverity.Success) =>
        Requested?.Invoke(message, severity);
}
