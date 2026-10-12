using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gmk87.Core.Model;
using Gmk87.Core.Protocol;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Gmk87.App.ViewModels;

public sealed record ColorSwatch(string Name, Color Color)
{
    public SolidColorBrush Brush { get; } = new(Color);
}

/// <summary>
/// Backlight storage differs by firmware:
/// V0104 (VIA protocol 9): everything lives in the vendor 0xFF1C settings block.
/// V0112 (VIA protocol 11): effect, brightness and speed go over VIA channel 3, but direction, the rainbow
/// flag and the RGB colour are still only in the vendor block (VIA's colour value is ignored; tested 2026-10-12).
/// The logo light bar, Win lock and clock are always in the vendor block.
/// </summary>
public sealed partial class LightingViewModel : ObservableObject
{
    private ViaLightingState? _viaState;

    public LightingViewModel()
    {
        SelectedEffect = Effects[0];
        App.Keyboard.ConfigChanged += (_, _) =>
        {
            if (App.Keyboard.LastConfig is { } latest)
                LoadVendor(latest);
        };
        if (App.Keyboard.LastConfig is { } config)
            LoadVendor(config);
    }

    public IReadOnlyList<LightingEffect> Effects => LightingCatalog.Effects;
    public IReadOnlyList<string> LogoModes => LightingCatalog.LogoModes;
    public IReadOnlyList<string> LogoColors { get; } = LightingCatalog.LogoColors.Select(c => c.Name).ToList();

    /// <summary>Picked from the firmware's own 46-step hue wheel (file offset 0x1DB28 in V0104), plus white.</summary>
    public IReadOnlyList<ColorSwatch> Swatches { get; } =
    [
        new("Red", Color.FromArgb(255, 0xFF, 0x01, 0x00)),
        new("Orange", Color.FromArgb(255, 0xFF, 0x6D, 0x00)),
        new("Yellow", Color.FromArgb(255, 0xFF, 0xFF, 0x00)),
        new("Lime", Color.FromArgb(255, 0x9E, 0xFF, 0x00)),
        new("Green", Color.FromArgb(255, 0x00, 0xFF, 0x00)),
        new("Spring", Color.FromArgb(255, 0x00, 0xFF, 0x6D)),
        new("Cyan", Color.FromArgb(255, 0x00, 0xFF, 0xFF)),
        new("Sky", Color.FromArgb(255, 0x00, 0x9E, 0xFF)),
        new("Blue", Color.FromArgb(255, 0x00, 0x00, 0xFF)),
        new("Violet", Color.FromArgb(255, 0x63, 0x00, 0xFF)),
        new("Magenta", Color.FromArgb(255, 0xFF, 0x00, 0xFF)),
        new("Pink", Color.FromArgb(255, 0xFF, 0x00, 0x68)),
        new("White", Color.FromArgb(255, 0xFF, 0xFF, 0xFF)),
    ];

    [ObservableProperty] public partial bool IsBusy { get; set; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ColorHint), nameof(IsColorEditable))]
    public partial bool UsesVia { get; set; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanWriteBacklight))]
    public partial bool IsBacklightLoaded { get; set; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanWriteBacklight))]
    public partial bool IsVendorLoaded { get; set; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ColorHint), nameof(IsColorEditable))]
    public partial LightingEffect SelectedEffect { get; set; }

    [ObservableProperty] public partial double Brightness { get; set; }
    [ObservableProperty] public partial double Speed { get; set; }
    [ObservableProperty] public partial bool Reverse { get; set; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsColorEditable), nameof(ColorHint))]
    public partial bool Rainbow { get; set; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ColorBrush), nameof(ColorHex))]
    public partial Color Color { get; set; } = Color.FromArgb(255, 255, 0, 0);

    [ObservableProperty] public partial int LogoMode { get; set; }
    [ObservableProperty] public partial int LogoColor { get; set; }
    [ObservableProperty] public partial double LogoBrightness { get; set; }
    [ObservableProperty] public partial bool LogoRainbow { get; set; }
    [ObservableProperty] public partial bool WinLock { get; set; }
    [ObservableProperty] public partial bool SyncClock { get; set; } = true;
    [ObservableProperty] public partial string KeyboardClock { get; set; } = "-";

    public SolidColorBrush ColorBrush => new(Color);
    public string ColorHex => $"#{Color.R:X2}{Color.G:X2}{Color.B:X2}";

    /// <summary>Colour, rainbow and direction always come from the vendor block, so it must be read first.</summary>
    public bool CanWriteBacklight => IsBacklightLoaded && IsVendorLoaded;

    public double MaxBrightness => KeyboardConfig.MaxBrightness;

    public bool IsColorEditable => SelectedEffect.Id != 0 && !Rainbow
                                   && (UsesVia || SelectedEffect.Color == ColorSupport.Custom);

    public string ColorHint => SelectedEffect.Id == 0 ? "Lighting is off."
        : Rainbow ? "Rainbow is on, so the colour is ignored. Turn it off to use a single colour."
        : UsesVia ? "Uses your colour (confirmed on 6 Light). Which other V0112 effects use it hasn't been mapped yet."
        : SelectedEffect.Color switch
        {
            ColorSupport.Custom => "This mode uses your colour.",
            ColorSupport.BuiltInRainbow => "This mode is always rainbow; the colour setting is ignored.",
            ColorSupport.Palette => "This mode cycles its own 4 built-in colours; the colour setting is ignored.",
            _ => "",
        };

    public void PickSwatch(ColorSwatch swatch) => Color = swatch.Color;

    /// <summary>Reads VIA (V0112 only) and the vendor block. The vendor read sends INIT/COMMIT, as the vendor app does.</summary>
    [RelayCommand]
    private async Task ReadBacklightAsync()
    {
        await RunAsync(async () =>
        {
            UsesVia = await App.Keyboard.GetViaProtocolAsync() >= 11;
            if (UsesVia)
            {
                _viaState = await App.Keyboard.WithViaAsync(via => ViaLighting.ReadAsync(via));
                LoadVia(_viaState);
            }
            await App.Keyboard.ReadConfigAsync(); // LoadVendor runs via ConfigChanged
            IsBacklightLoaded = true;
            Show("Read the backlight settings.", InfoBarSeverity.Success);
        });
    }

    [RelayCommand]
    private async Task WriteBacklightAsync()
    {
        await RunAsync(async () =>
        {
            if (UsesVia && _viaState is { } current)
            {
                // VIA's colour value is ignored by V0112, so it is never sent; colour goes through the vendor block below.
                var desired = new ViaLightingState((int)Brightness, SelectedEffect.Id, (int)Speed, current.Hue, current.Saturation);
                await App.Keyboard.WithViaAsync(async via =>
                {
                    await ViaLighting.WriteAsync(via, current, desired);
                    return true;
                });

                var vendor = App.Keyboard.LastConfig ?? throw new InvalidOperationException("Read the backlight first.");
                if (vendor.ReverseDirection != Reverse || vendor.Rainbow != Rainbow || !SameColor(vendor))
                {
                    var config = vendor.Clone();
                    // Bytes 1-3 mirror what VIA just set; keep them in step so this write doesn't undo it.
                    config.Effect = desired.Effect;
                    config.Brightness = desired.Brightness;
                    config.SpeedLevel = desired.Speed;
                    ApplyColorAndDirection(config);
                    await App.Keyboard.WriteConfigAsync(config);
                }

                _viaState = await App.Keyboard.WithViaAsync(via => ViaLighting.ReadAsync(via));
                LoadVia(_viaState);
                Show(_viaState == desired
                        ? "Backlight written and read back."
                        : $"Written, but the keyboard reports brightness {_viaState.Brightness}, effect {_viaState.Effect}, speed {_viaState.Speed}.",
                    _viaState == desired ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
            }
            else
            {
                await WriteVendorBlockAsync(includeBacklight: true);
                Show("Settings written to the keyboard.", InfoBarSeverity.Success);
            }
        });
    }

    /// <summary>Vendor block read: sends INIT/COMMIT, exactly as the vendor app does.</summary>
    [RelayCommand]
    private async Task ReadVendorAsync()
    {
        await RunAsync(async () =>
        {
            await App.Keyboard.ReadConfigAsync();
            Show("Read the vendor settings (logo, Win lock, clock).", InfoBarSeverity.Success);
        });
    }

    [RelayCommand]
    private async Task WriteVendorAsync()
    {
        await RunAsync(async () =>
        {
            await WriteVendorBlockAsync(includeBacklight: false);
            Show("Vendor settings written to the keyboard.", InfoBarSeverity.Success);
        });
    }

    private async Task WriteVendorBlockAsync(bool includeBacklight)
    {
        var config = (App.Keyboard.LastConfig ?? throw new InvalidOperationException("Read the vendor settings first.")).Clone();
        if (includeBacklight)
        {
            config.Effect = SelectedEffect.Id;
            config.Brightness = (int)Brightness;
            config.SpeedLevel = (int)Speed;
            ApplyColorAndDirection(config);
        }
        config.LogoMode = LogoMode;
        config.LogoColor = LogoColor;
        config.LogoBrightness = (int)LogoBrightness;
        config.LogoRainbow = LogoRainbow;
        config.WinLock = WinLock;
        if (SyncClock)
            config.SetClock(DateTime.Now);
        await App.Keyboard.WriteConfigAsync(config);
    }

    private void ApplyColorAndDirection(KeyboardConfig config)
    {
        config.ReverseDirection = Reverse;
        config.Rainbow = Rainbow;
        config.Red = Color.R;
        config.Green = Color.G;
        config.Blue = Color.B;
    }

    private bool SameColor(KeyboardConfig c) => c.Red == Color.R && c.Green == Color.G && c.Blue == Color.B;

    private void LoadVia(ViaLightingState s)
    {
        SelectedEffect = Effects.FirstOrDefault(e => e.Id == s.Effect) ?? Effects[0];
        Brightness = s.Brightness;
        Speed = s.Speed;
    }

    private void LoadVendor(KeyboardConfig c)
    {
        if (!UsesVia)
        {
            SelectedEffect = Effects.FirstOrDefault(e => e.Id == c.Effect) ?? Effects[0];
            Brightness = c.Brightness;
            Speed = c.SpeedLevel;
        }
        Reverse = c.ReverseDirection;
        Rainbow = c.Rainbow;
        Color = Color.FromArgb(255, c.Red, c.Green, c.Blue);
        LogoMode = Math.Min(c.LogoMode, LogoModes.Count - 1);
        LogoColor = Math.Min(c.LogoColor, LogoColors.Count - 1);
        LogoBrightness = c.LogoBrightness;
        LogoRainbow = c.LogoRainbow;
        WinLock = c.WinLock;
        KeyboardClock = c.Clock?.ToString("yyyy-MM-dd HH:mm:ss") ?? "unreadable";
        IsVendorLoaded = true;
    }

    private async Task RunAsync(Func<Task> action)
    {
        IsBusy = true;
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Show(ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static void Show(string message, InfoBarSeverity severity) => Services.Toast.Show(message, severity);
}
