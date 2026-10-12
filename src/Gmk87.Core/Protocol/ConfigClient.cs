using Gmk87.Core.Hid;
using Gmk87.Core.Model;

namespace Gmk87.Core.Protocol;

/// <summary>
/// Client for the vendor interface (usage page 0xFF1C). Protocol from the gmk87-node project
/// and the Python reference by Jochen Eisinger, matched against sniffed vendor-app traffic.
/// </summary>
public sealed class ConfigClient(IHidChannel channel) : IDisposable
{
    public const byte CmdInit = 0x01;
    public const byte CmdCommit = 0x02;
    /// <summary>Called PREP_READ by gmk87-node; V0112 code shows it reads the second 64-byte block (flash 0x82000).</summary>
    public const byte CmdPrepareRead = 0x03;
    public const byte CmdReadConfig = 0x05;
    public const byte CmdWriteConfig = 0x06;
    public const byte CmdFrameData = 0x21;
    public const byte CmdUploadStart = 0x23;

    public const int MaxPayload = 56;

    private static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ExtendedReplyTimeout = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Reads the settings block: 64 bytes when the firmware answers for positions 48-63 (V0112), else 48.
    /// Note: the read sequence itself sends INIT and COMMIT, exactly as the vendor app does.
    /// </summary>
    public async Task<KeyboardConfig> ReadConfigAsync(CancellationToken ct = default)
    {
        await channel.DrainAsync(ct).ConfigureAwait(false);
        await TransactAsync(CmdInit, [], 0, ct).ConfigureAwait(false);
        for (var i = 0; i < 9; i++)
            await TransactAsync(CmdPrepareRead, new byte[4], i * 4, ct).ConfigureAwait(false);
        await TransactAsync(CmdPrepareRead, new byte[1], 36, ct).ConfigureAwait(false);
        await TransactAsync(CmdCommit, [], 0, ct).ConfigureAwait(false);

        var raw = new byte[KeyboardConfig.ExtendedLength];
        for (var pos = 0; pos < KeyboardConfig.Length; pos += 4)
        {
            var chunk = await TransactAsync(CmdReadConfig, new byte[4], pos, ct).ConfigureAwait(false);
            Array.Copy(chunk, 0, raw, pos, 4);
        }

        try
        {
            for (var pos = KeyboardConfig.Length; pos < KeyboardConfig.ExtendedLength; pos += 4)
            {
                var chunk = await TransactAsync(CmdReadConfig, new byte[4], pos, ct, ExtendedReplyTimeout).ConfigureAwait(false);
                Array.Copy(chunk, 0, raw, pos, 4);
            }
        }
        catch (Gmk87ProtocolException)
        {
            return new KeyboardConfig(raw.AsSpan(0, KeyboardConfig.Length));
        }

        return new KeyboardConfig(raw);
    }

    /// <summary>
    /// WRITES the settings block (lighting, logo LED, clock, display selection). The firmware starts a fresh,
    /// erased flash slot at position 0, so every byte not written ends up 0xFF: a 64-byte block is written in
    /// full (two packets) to keep bytes 48-63, and a damaged VIA protocol selector is reset to V3 on the way.
    /// </summary>
    public async Task WriteConfigAsync(KeyboardConfig config, CancellationToken ct = default)
    {
        var block = config.Clone();
        block.RepairProtocolSelector();
        var raw = block.ToArray();

        await channel.DrainAsync(ct).ConfigureAwait(false);
        await TransactAsync(CmdInit, [], 0, ct).ConfigureAwait(false);
        for (var pos = 0; pos < raw.Length; pos += MaxPayload)
            await TransactAsync(CmdWriteConfig, raw.AsSpan(pos, Math.Min(MaxPayload, raw.Length - pos)).ToArray(), pos, ct).ConfigureAwait(false);
        await TransactAsync(CmdCommit, [], 0, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// WRITES the config and then the screen image memory. The upload always replaces BOTH slots:
    /// <paramref name="imageData"/> is slot-1 frames followed by slot-2 frames, each frame
    /// <see cref="Imaging.DisplayImage.FrameBytes"/> long, and the frame counts in
    /// <paramref name="config"/> must describe that layout.
    /// </summary>
    public async Task UploadImagesAsync(KeyboardConfig config, byte[] imageData, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        await WriteConfigAsync(config, ct).ConfigureAwait(false);
        await TransactAsync(CmdUploadStart, [], 0, ct).ConfigureAwait(false);
        await TransactAsync(CmdInit, [], 0, ct).ConfigureAwait(false);

        for (var pos = 0; pos < imageData.Length; pos += MaxPayload)
        {
            var size = Math.Min(MaxPayload, imageData.Length - pos);
            await TransactAsync(CmdFrameData, imageData.AsSpan(pos, size).ToArray(), pos, ct).ConfigureAwait(false);
            progress?.Report((double)(pos + size) / imageData.Length);
        }

        await TransactAsync(CmdCommit, [], 0, ct).ConfigureAwait(false);
    }

    /// <summary>Builds one 64-byte report: id, checksum, command, length, 24-bit position, payload.</summary>
    public static byte[] BuildPacket(byte command, ReadOnlySpan<byte> data, int position, int reportLength = 64)
    {
        if (data.Length > MaxPayload)
            throw new ArgumentOutOfRangeException(nameof(data), $"Payload is limited to {MaxPayload} bytes.");

        var buffer = new byte[reportLength];
        buffer[0] = 0x04;
        buffer[3] = command;
        buffer[4] = (byte)data.Length;
        buffer[5] = (byte)position;
        buffer[6] = (byte)(position >> 8);
        buffer[7] = (byte)(position >> 16);
        data.CopyTo(buffer.AsSpan(8));

        var sum = 0;
        for (var i = 3; i < reportLength; i++)
            sum += buffer[i];
        buffer[1] = (byte)sum;
        buffer[2] = (byte)(sum >> 8);
        return buffer;
    }

    /// <summary>Sends one command and waits for the reply that echoes it; returns reply bytes 4 onward.</summary>
    internal async Task<byte[]> TransactAsync(byte command, byte[] data, int position, CancellationToken ct, TimeSpan? timeout = null)
    {
        // The Python reference waits before COMMIT to give the keyboard time to finish the previous step.
        if (command == CmdCommit)
            await Task.Delay(100, ct).ConfigureAwait(false);

        await channel.WriteAsync(BuildPacket(command, data, position, channel.OutputReportLength), ct).ConfigureAwait(false);

        var deadline = DateTime.UtcNow + (timeout ?? ReplyTimeout);
        while (DateTime.UtcNow < deadline)
        {
            var reply = await channel.ReadAsync(deadline - DateTime.UtcNow, ct).ConfigureAwait(false);
            if (reply is null)
                break;
            if (reply.Length > 3 && reply[3] == command)
                return reply.AsSpan(4).ToArray();
        }

        throw new Gmk87ProtocolException($"No reply to config command 0x{command:X2} at position {position}.");
    }

    public void Dispose() => channel.Dispose();
}
