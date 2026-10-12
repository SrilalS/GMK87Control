using Gmk87.App.ViewModels;
using Gmk87.Core.Lighting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Gmk87.App.Pages;

public sealed partial class LightingPage : Page
{
    public LightingPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        ViewModel.PropertyChanged += (_, _) => UpdatePreview();
        UpdatePreview();
    }

    public LightingViewModel ViewModel { get; } = new();

    /// <summary>Loads the current lighting the first time the page is shown.</summary>
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (!ViewModel.IsBacklightLoaded && !ViewModel.IsBusy)
            ViewModel.ReadBacklightCommand.Execute(null);
    }

    /// <summary>The preview follows the controls as you edit, before anything is written.</summary>
    private void UpdatePreview()
    {
        var vm = ViewModel;
        Preview.Params = new EffectParams(vm.SelectedEffect.Id, (int)vm.Brightness, (int)vm.Speed, vm.Reverse,
            vm.Rainbow, new Rgb(vm.Color.R, vm.Color.G, vm.Color.B));
    }

    private void OnSwatchClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ColorSwatch swatch)
            ViewModel.PickSwatch(swatch);
    }

    // Fires during InitializeComponent (IsOn="True"), before Preview exists.
    private void OnDemoToggled(object sender, RoutedEventArgs e)
    {
        if (Preview is not null)
            Preview.DemoPresses = DemoToggle.IsOn;
    }
}
