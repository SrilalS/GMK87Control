using Gmk87.App.Pages;
using Gmk87.App.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace Gmk87.App;

public sealed partial class MainWindow : Window
{
    private static readonly Dictionary<string, Type> Pages = new()
    {
        ["Device"] = typeof(DevicePage),
        ["Lighting"] = typeof(LightingPage),
        ["Keymap"] = typeof(KeymapPage),
        ["Display"] = typeof(DisplayPage),
    };

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarArea);
        AppWindow.Resize(new SizeInt32(1280, 860));
        Nav.SelectedItem = Nav.MenuItems[0];

        _toastTimer = DispatcherQueue.CreateTimer();
        _toastTimer.IsRepeating = false;
        _toastTimer.Tick += (_, _) => ToastBar.IsOpen = false;
        Toast.Requested += ShowToast;
    }

    private readonly DispatcherQueueTimer _toastTimer;

    private void ShowToast(string message, InfoBarSeverity severity)
    {
        ToastBar.Message = message;
        ToastBar.Severity = severity;
        ToastBar.IsOpen = true;
        // Errors and warnings stay long enough to read; success fades quickly.
        _toastTimer.Interval = TimeSpan.FromSeconds(severity is InfoBarSeverity.Error or InfoBarSeverity.Warning ? 8 : 3);
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem { Tag: string tag } && Pages.TryGetValue(tag, out var page))
            ContentFrame.Navigate(page);
    }
}
