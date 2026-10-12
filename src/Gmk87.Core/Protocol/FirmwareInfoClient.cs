using Gmk87.Core.Hid;

namespace Gmk87.Core.Protocol;

public sealed record FirmwareInfo(uint Version, uint Crc);

/// <summary>
/// Read-only access to the firmware update interface. Deliberately has no way to send command 0x02
/// (the OTA transfer): flashing is out of scope until a hardware recovery path exists.
/// </summary>
public sealed class FirmwareInfoClient(IHidChannel channel) : IDisposable
{
    private const byte ReportId = 0x05;
    private const byte CmdGetFirmwareInfo = 0x01;

    public async Task<FirmwareInfo> GetFirmwareInfoAsync(CancellationToken ct = default)
    {
        await channel.DrainAsync(ct).ConfigureAwait(false);

        // Byte-for-byte what the vendor tool's "Get Device Fw Version" sends.
        var report = new byte[channel.OutputReportLength];
        report[0] = ReportId;
        report[1] = CmdGetFirmwareInfo;
        for (var i = 4; i <= 32 && i < report.Length; i++)
            report[i] = 0xFF;
        await channel.WriteAsync(report, ct).ConfigureAwait(false);

        var reply = await channel.ReadAsync(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false)
                    ?? throw new Gmk87ProtocolException("No reply to firmware info query.");
        if (reply[1] != CmdGetFirmwareInfo || reply.Length < 12)
            throw new Gmk87ProtocolException($"Unexpected firmware info reply 0x{reply[1]:X2}.");

        return new FirmwareInfo(BitConverter.ToUInt32(reply, 4), BitConverter.ToUInt32(reply, 8));
    }

    public void Dispose() => channel.Dispose();
}
