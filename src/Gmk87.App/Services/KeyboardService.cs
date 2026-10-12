using Gmk87.Core.Hid;
using Gmk87.Core.Model;
using Gmk87.Core.Protocol;

namespace Gmk87.App.Services;

/// <summary>
/// Single entry point for talking to the keyboard. Every operation opens its interface, runs, and
/// closes again, all serialized, so two pages can never interleave packets on the same interface.
/// </summary>
public sealed class KeyboardService
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>The settings block from the most recent read or write, shared by the Lighting and Screen pages.</summary>
    public KeyboardConfig? LastConfig { get; set; }

    public event EventHandler? ConfigChanged;

    public IReadOnlyList<HidInterfaceInfo> Enumerate() =>
        HidEnumerator.Find(Gmk87Ids.VendorId)
            .Where(i => i.ProductId is Gmk87Ids.ProductIdUsb or Gmk87Ids.ProductIdDongle)
            .ToList();

    public Task<T> WithViaAsync<T>(Func<ViaClient, Task<T>> action) =>
        RunAsync(Gmk87Ids.UsagePageVia, channel => action(new ViaClient(channel)));

    public Task<T> WithConfigAsync<T>(Func<ConfigClient, Task<T>> action) =>
        RunAsync(Gmk87Ids.UsagePageConfig, channel => action(new ConfigClient(channel)));

    public Task<T> WithFirmwareInfoAsync<T>(Func<FirmwareInfoClient, Task<T>> action) =>
        RunAsync(Gmk87Ids.UsagePageOta, channel => action(new FirmwareInfoClient(channel)));

    /// <summary>VIA protocol reported by the firmware: 9 on V0104, 11 on V0112. Decides keycode numbering and lighting path.</summary>
    public Task<int> GetViaProtocolAsync() => WithViaAsync(via => via.GetProtocolVersionAsync());

    public async Task<KeyboardConfig> ReadConfigAsync()
    {
        var config = await WithConfigAsync(c => c.ReadConfigAsync());
        SetConfig(config);
        return config;
    }

    public async Task WriteConfigAsync(KeyboardConfig config)
    {
        await WithConfigAsync(async c =>
        {
            await c.WriteConfigAsync(config);
            return true;
        });
        SetConfig(config.Clone());
    }

    public void SetConfig(KeyboardConfig config)
    {
        LastConfig = config;
        ConfigChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task<T> RunAsync<T>(ushort usagePage, Func<IHidChannel, Task<T>> action)
    {
        await _gate.WaitAsync();
        try
        {
            return await Task.Run(async () =>
            {
                var info = Enumerate().FirstOrDefault(i => i.UsagePage == usagePage)
                           ?? throw new InvalidOperationException(
                               $"GMK87 interface 0x{usagePage:X4} not found. Connect the keyboard with the USB cable.");
                using var channel = HidChannel.Open(info);
                return await action(channel);
            });
        }
        finally
        {
            _gate.Release();
        }
    }
}
