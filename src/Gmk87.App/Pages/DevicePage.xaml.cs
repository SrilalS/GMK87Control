using Gmk87.App.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Gmk87.App.Pages;

public sealed partial class DevicePage : Page
{
    public DevicePage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
    }

    public DeviceViewModel ViewModel { get; } = new();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (ViewModel.Status == "Not checked yet")
            ViewModel.RefreshCommand.Execute(null);
    }
}
