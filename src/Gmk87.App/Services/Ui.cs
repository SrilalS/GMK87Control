using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;

namespace Gmk87.App.Services;

public static class Ui
{
    /// <summary>Asks before anything that writes to the keyboard. Returns true only on an explicit "Write".</summary>
    public static async Task<bool> ConfirmWriteAsync(XamlRoot root, string title, string body, string primary = "Write to keyboard")
    {
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = title,
            Content = new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = primary,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    public static async Task<string?> PickOpenFileAsync(params string[] extensions)
    {
        var picker = new FileOpenPicker(App.MainWindow.AppWindow.Id);
        foreach (var ext in extensions)
            picker.FileTypeFilter.Add(ext);
        var result = await picker.PickSingleFileAsync();
        return result?.Path;
    }

    public static async Task<string?> PickSaveFileAsync(string suggestedName, string typeLabel, string extension)
    {
        var picker = new FileSavePicker(App.MainWindow.AppWindow.Id)
        {
            SuggestedFileName = suggestedName,
        };
        picker.FileTypeChoices.Add(typeLabel, [extension]);
        var result = await picker.PickSaveFileAsync();
        return result?.Path;
    }
}
