using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gmk87.Core.Hid;
using Gmk87.Core.Protocol;

namespace Gmk87.App.ViewModels;

public sealed record InterfaceRow(string Tag, string UsagePage, string Usage, string Reports, string Role);

public sealed partial class DeviceViewModel : ObservableObject
{
    /// <summary>Trailing CRCs of the vendor firmware images analysed in Old Research.</summary>
    private static readonly Dictionary<uint, string> KnownFirmware = new()
    {
        [0x5C0AB3BC] = "V0104 (CS754A updater, 2023)",
        [0x4D33004C] = "V0112 (CS8175 updater, 2026-01-23)",
    };

    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial bool IsConnected { get; set; }
    [ObservableProperty] public partial string Status { get; set; } = "Not checked yet";
    [ObservableProperty] public partial string ProductName { get; set; } = "-";
    [ObservableProperty] public partial string ConnectionMode { get; set; } = "-";
    [ObservableProperty] public partial string UsbRevision { get; set; } = "-";
    [ObservableProperty] public partial string ViaProtocol { get; set; } = "-";
    [ObservableProperty] public partial string LayerCount { get; set; } = "-";
    [ObservableProperty] public partial string FirmwareVersion { get; set; } = "-";
    [ObservableProperty] public partial string FirmwareCrc { get; set; } = "-";
    [ObservableProperty] public partial string FirmwareNote { get; set; } = "";

    public ObservableCollection<InterfaceRow> Interfaces { get; } = [];

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            var service = App.Keyboard;
            var list = await Task.Run(service.Enumerate);
            Interfaces.Clear();
            foreach (var i in list.OrderBy(i => i.InterfaceTag))
                Interfaces.Add(ToRow(i));

            IsConnected = list.Count > 0;
            if (!IsConnected)
            {
                Status = "Not connected";
                ProductName = ConnectionMode = UsbRevision = ViaProtocol = LayerCount = FirmwareVersion = FirmwareCrc = "-";
                FirmwareNote = "";
                return;
            }

            var first = list[0];
            ProductName = first.ProductName ?? "GMK87";
            ConnectionMode = first.ProductId == Gmk87Ids.ProductIdUsb ? "USB cable" : $"PID 0x{first.ProductId:X4} (dongle?)";
            UsbRevision = first.ReleaseNumber.ToString("X4");
            Status = "Connected";

            try
            {
                var (protocol, layers) = await service.WithViaAsync(async via =>
                    (await via.GetProtocolVersionAsync(), await via.GetLayerCountAsync()));
                ViaProtocol = $"{protocol} ({(protocol >= 11 ? "VIA V3, modern keycodes" : "VIA V2, legacy keycodes")})";
                LayerCount = layers.ToString();
            }
            catch (Exception ex)
            {
                ViaProtocol = $"error: {ex.Message}";
            }

            try
            {
                var info = await service.WithFirmwareInfoAsync(f => f.GetFirmwareInfoAsync());
                FirmwareVersion = $"0x{info.Version:X8}";
                FirmwareCrc = $"0x{info.Crc:X8}";
                FirmwareNote = KnownFirmware.TryGetValue(info.Crc, out var name)
                    ? $"Matches vendor firmware {name}."
                    : "Unknown firmware: this image has not been analysed.";
            }
            catch (Exception ex)
            {
                FirmwareVersion = $"error: {ex.Message}";
            }
        }
        catch (Exception ex)
        {
            Status = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static InterfaceRow ToRow(HidInterfaceInfo i)
    {
        var role = (i.UsagePage, i.Usage) switch
        {
            (Gmk87Ids.UsagePageVia, _) => "VIA (keymap)",
            (Gmk87Ids.UsagePageConfig, _) => "Vendor: lighting, clock, screen",
            (Gmk87Ids.UsagePageOta, _) => "Firmware update (read-only here)",
            (0x0001, 0x06) => "Keyboard",
            (0x0001, 0x02) => "Mouse",
            (0x0001, 0x80) => "System control",
            (0x000C, 0x01) => "Consumer control",
            _ => "",
        };
        return new InterfaceRow(i.InterfaceTag, $"0x{i.UsagePage:X4}", $"0x{i.Usage:X2}",
            $"{i.InputReportLength} / {i.OutputReportLength} / {i.FeatureReportLength}", role);
    }
}
