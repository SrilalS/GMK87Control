using Gmk87.App.Services;
using Gmk87.App.ViewModels;
using Gmk87.Core.Imaging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Gmk87.App.Pages;

public sealed partial class DisplayPage : Page
{
    public DisplayPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
    }

    public DisplayViewModel ViewModel { get; } = new();

    private async void OnUploadClick(object sender, RoutedEventArgs e)
    {
        var frames = Math.Max(1, ViewModel.Slot1.Animation?.Frames.Count ?? 0) + Math.Max(1, ViewModel.Slot2.Animation?.Frames.Count ?? 0);
        var seconds = Math.Ceiling(frames * DisplayImage.FrameBytes / 56.0 * 0.002);
        if (await Ui.ConfirmWriteAsync(XamlRoot, "Upload to the screen?",
                $"Writes {frames} frame(s) to the keyboard, replacing BOTH slots (roughly {seconds:0} s). " +
                "Lighting and other settings are read first and written back unchanged; the clock is synced. Don't unplug during the upload."))
            await ViewModel.UploadAsync();
    }

    private async void OnSwitchClick(object sender, RoutedEventArgs e)
    {
        if (await Ui.ConfirmWriteAsync(XamlRoot, "Switch the screen?",
                $"Writes the settings block so the screen shows \"{ViewModel.ScreenModes[ViewModel.ShowAfter]}\" and syncs the clock. Images are not touched."))
            await ViewModel.SwitchScreenAsync();
    }
}
