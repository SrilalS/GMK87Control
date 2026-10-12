using Gmk87.Core.Hid;
using Gmk87.Core.Model;

namespace Gmk87.Core.Protocol;

/// <summary>
/// VIA v2 (protocol 9) client. Only commands the firmware actually implements are exposed;
/// see Old Research/docs/07-memory-map.md for the dispatch table.
/// </summary>
public sealed class ViaClient(IHidChannel channel) : IDisposable
{
    private const byte CmdGetProtocolVersion = 0x01;
    private const byte CmdSetKeycode = 0x05;
    private const byte CmdCustomSet = 0x07;
    private const byte CmdCustomGet = 0x08;
    private const byte CmdCustomSave = 0x09;
    private const byte CmdGetLayerCount = 0x11;
    private const byte CmdGetKeymapBuffer = 0x12;

    /// <summary>Largest chunk that fits a 32-byte payload after the 4-byte header.</summary>
    internal const int MaxKeymapChunk = 28;

    private static readonly TimeSpan ReplyTimeout = TimeSpan.FromMilliseconds(600);

    public async Task<int> GetProtocolVersionAsync(CancellationToken ct = default)
    {
        var reply = await TransactAsync([CmdGetProtocolVersion, 0, 0], ct).ConfigureAwait(false);
        return (reply[1] << 8) | reply[2];
    }

    public async Task<int> GetLayerCountAsync(CancellationToken ct = default)
    {
        var reply = await TransactAsync([CmdGetLayerCount, 0], ct).ConfigureAwait(false);
        return reply[1];
    }

    public async Task<Keymap> ReadKeymapAsync(int layers, int rows, int cols, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var total = layers * rows * cols * 2;
        var buffer = new byte[total];
        for (var offset = 0; offset < total; offset += MaxKeymapChunk)
        {
            var size = Math.Min(MaxKeymapChunk, total - offset);
            var reply = await TransactAsync([CmdGetKeymapBuffer, (byte)(offset >> 8), (byte)offset, (byte)size], ct).ConfigureAwait(false);
            if (((reply[1] << 8) | reply[2]) != offset)
                throw new Gmk87ProtocolException($"Keymap read returned offset {(reply[1] << 8) | reply[2]}, expected {offset}.");
            Array.Copy(reply, 4, buffer, offset, size);
            progress?.Report((double)(offset + size) / total);
        }

        return Keymap.FromBuffer(buffer, layers, rows, cols);
    }

    /// <summary>WRITES to the keyboard: changes one key on one layer. The firmware persists it.</summary>
    public async Task SetKeycodeAsync(int layer, int row, int col, ushort keycode, CancellationToken ct = default)
    {
        await TransactAsync([CmdSetKeycode, (byte)layer, (byte)row, (byte)col, (byte)(keycode >> 8), (byte)keycode], ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads a VIA V3 custom-menu value: 08 channel id. Returns the value bytes (reply byte 3 onward).
    /// Only meaningful on protocol 11+; V0104's 0x08 handler returns hard-coded V2 values instead.
    /// </summary>
    public async Task<byte[]> GetCustomValueAsync(byte channel, byte valueId, int length, CancellationToken ct = default)
    {
        var reply = await TransactAsync([CmdCustomGet, channel, valueId], ct).ConfigureAwait(false);
        if (reply[1] != channel || reply[2] != valueId)
            throw new Gmk87ProtocolException($"Custom value reply for {reply[1]}/{reply[2]}, expected {channel}/{valueId}.");
        return reply.AsSpan(3, length).ToArray();
    }

    /// <summary>WRITES a VIA V3 custom-menu value (07 channel id value...). Takes effect immediately; call <see cref="SaveCustomAsync"/> to persist.</summary>
    public async Task SetCustomValueAsync(byte channel, byte valueId, byte[] value, CancellationToken ct = default)
    {
        await TransactAsync([CmdCustomSet, channel, valueId, .. value], ct).ConfigureAwait(false);
    }

    /// <summary>WRITES: asks the keyboard to persist a custom-menu channel (09 channel), as VIA does after each change.</summary>
    public async Task SaveCustomAsync(byte channel, CancellationToken ct = default)
    {
        await TransactAsync([CmdCustomSave, channel], ct).ConfigureAwait(false);
    }

    /// <summary>Sends one VIA command and returns the 32-byte reply payload (report ID stripped).</summary>
    internal async Task<byte[]> TransactAsync(byte[] payload, CancellationToken ct)
    {
        var report = new byte[channel.OutputReportLength];
        payload.CopyTo(report, 1);
        await channel.WriteAsync(report, ct).ConfigureAwait(false);

        var reply = await channel.ReadAsync(ReplyTimeout, ct).ConfigureAwait(false)
                    ?? throw new Gmk87ProtocolException($"No reply to VIA command 0x{payload[0]:X2}.");
        var data = reply.AsSpan(1).ToArray();

        // The firmware's default branch answers unknown commands with byte[0] = 0x01, byte[2] = 0x09.
        if (payload[0] != CmdGetProtocolVersion && data[0] == 0x01 && data[2] == 0x09)
            throw new Gmk87ProtocolException($"The keyboard does not implement VIA command 0x{payload[0]:X2}.");
        if (data[0] != payload[0])
            throw new Gmk87ProtocolException($"VIA command 0x{payload[0]:X2} got a reply for 0x{data[0]:X2}.");
        return data;
    }

    public void Dispose() => channel.Dispose();
}
