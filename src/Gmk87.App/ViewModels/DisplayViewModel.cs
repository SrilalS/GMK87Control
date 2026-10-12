using System.Runtime.InteropServices.WindowsRuntime;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gmk87.App.Services;
using Gmk87.Core.Imaging;
using Gmk87.Core.Model;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Gmk87.App.ViewModels;

/// <summary>One of the two image slots: the loaded animation plus a live preview.</summary>
public sealed partial class SlotViewModel : ObservableObject
{
    private readonly DispatcherQueueTimer _timer;
    private int _frameIndex;

    public SlotViewModel(string title)
    {
        Title = title;
        Preview = new WriteableBitmap(DisplayImage.Width, DisplayImage.Height);
        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Tick += (_, _) => ShowFrame(_frameIndex + 1);
    }

    public string Title { get; }
    public WriteableBitmap Preview { get; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasImage), nameof(Summary))]
    public partial ScreenAnimation? Animation { get; set; }

    public bool HasImage => Animation is not null;

    public string Summary => Animation is { } a
        ? $"{a.FileName}: {a.Frames.Count} frame{(a.Frames.Count == 1 ? "" : "s")}"
        : "Empty (uploads as black)";

    public void SetAnimation(ScreenAnimation? animation, int delayMs)
    {
        Animation = animation;
        _timer.Stop();
        ShowFrame(0);
        SetDelay(delayMs);
    }

    public void SetDelay(int delayMs)
    {
        _timer.Interval = TimeSpan.FromMilliseconds(Math.Max(KeyboardConfig.MinFrameDelayMs, delayMs));
        if (Animation is { Frames.Count: > 1 })
            _timer.Start();
    }

    private void ShowFrame(int index)
    {
        var frames = Animation?.Frames;
        _frameIndex = frames is { Count: > 0 } ? index % frames.Count : 0;
        var pixels = frames is { Count: > 0 } ? frames[_frameIndex] : new byte[DisplayImage.Width * DisplayImage.Height * 4];
        using (var stream = Preview.PixelBuffer.AsStream())
            stream.Write(pixels, 0, pixels.Length);
        Preview.Invalidate();
    }
}

public sealed partial class DisplayViewModel : ObservableObject
{
    public DisplayViewModel()
    {
        Slot1 = new SlotViewModel("Slot 1");
        Slot2 = new SlotViewModel("Slot 2");
        Slot1.SetAnimation(null, 100);
        Slot2.SetAnimation(null, 100);
    }

    public SlotViewModel Slot1 { get; }
    public SlotViewModel Slot2 { get; }

    public IReadOnlyList<string> ScreenModes { get; } = ["Clock / status", "Slot 1", "Slot 2"];

    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial double Progress { get; set; }
    [ObservableProperty] public partial int ShowAfter { get; set; } = 1;

    [ObservableProperty] public partial double FrameDelayMs { get; set; } = 100;

    partial void OnFrameDelayMsChanged(double value)
    {
        Slot1.SetDelay((int)value);
        Slot2.SetDelay((int)value);
    }

    [RelayCommand]
    private Task PickSlot1Async() => PickAsync(Slot1, Slot2);

    [RelayCommand]
    private Task PickSlot2Async() => PickAsync(Slot2, Slot1);

    [RelayCommand]
    private void ClearSlot1() => Slot1.SetAnimation(null, (int)FrameDelayMs);

    [RelayCommand]
    private void ClearSlot2() => Slot2.SetAnimation(null, (int)FrameDelayMs);

    private async Task PickAsync(SlotViewModel slot, SlotViewModel other)
    {
        var path = await Ui.PickOpenFileAsync(".png", ".jpg", ".jpeg", ".bmp", ".gif");
        if (path is null)
            return;

        await RunAsync(async () =>
        {
            var budget = DisplayImage.MaxTotalFrames - Math.Max(1, other.Animation?.Frames.Count ?? 0);
            var animation = await ImageLoader.LoadAsync(path, budget);
            if (!slot.HasImage && !other.HasImage)
                FrameDelayMs = Math.Max(KeyboardConfig.MinFrameDelayMs, animation.SuggestedDelayMs);
            slot.SetAnimation(animation, (int)FrameDelayMs);
            Show($"Loaded {animation.Frames.Count} frame(s) into {slot.Title}" +
                 (animation.Frames.Count == budget ? $" (capped at the {DisplayImage.MaxTotalFrames}-frame total)." : "."),
                InfoBarSeverity.Informational);
        });
    }

    /// <summary>WRITES: config (frame counts, delay, screen choice, clock) then both image slots.</summary>
    public async Task UploadAsync()
    {
        await RunAsync(async () =>
        {
            Progress = 0;
            var frames1 = Slot1.Animation?.Frames.Select(f => DisplayImage.EncodeFrame(f)).ToList() ?? [];
            var frames2 = Slot2.Animation?.Frames.Select(f => DisplayImage.EncodeFrame(f)).ToList() ?? [];
            var data = DisplayImage.BuildUpload(frames1, frames2, out var count1, out var count2);

            var config = (await App.Keyboard.ReadConfigAsync()).Clone();
            config.Slot1Frames = count1;
            config.Slot2Frames = count2;
            config.FrameDelayMs = (int)FrameDelayMs;
            config.Screen = (ScreenMode)ShowAfter;
            config.SetClock(DateTime.Now);

            var progress = new Progress<double>(p => Progress = p * 100);
            await App.Keyboard.WithConfigAsync(async c =>
            {
                await c.UploadImagesAsync(config, data, progress);
                return true;
            });
            App.Keyboard.SetConfig(config);
            Show($"Uploaded {count1 + count2} frame(s).", InfoBarSeverity.Success);
        });
    }

    /// <summary>WRITES: config only, to switch what the screen shows (and sync the clock).</summary>
    public async Task SwitchScreenAsync()
    {
        await RunAsync(async () =>
        {
            var config = (await App.Keyboard.ReadConfigAsync()).Clone();
            config.Screen = (ScreenMode)ShowAfter;
            config.SetClock(DateTime.Now);
            await App.Keyboard.WriteConfigAsync(config);
            Show($"Screen set to {ScreenModes[ShowAfter]}; clock synced.", InfoBarSeverity.Success);
        });
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
