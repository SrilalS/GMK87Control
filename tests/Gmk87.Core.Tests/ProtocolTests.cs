using Gmk87.Core.Model;
using Gmk87.Core.Protocol;

namespace Gmk87.Core.Tests;

public class ConfigProtocolTests
{
    // Read from this keyboard over 0xFF1C on 2026-09-06 (Old Research/probe/config-usb-cable.txt).
    internal static readonly byte[] SavedConfig = Convert.FromHexString(
        "000509010100ff00002d00000000000000000000ff0000ff000000000309020000004951451500060926006400011100");

    [Fact]
    public void BuildPacket_lays_out_header_and_checksum_like_the_python_reference()
    {
        var packet = ConfigClient.BuildPacket(0x05, new byte[4], 8);

        Assert.Equal(64, packet.Length);
        Assert.Equal(0x04, packet[0]);
        Assert.Equal(0x05, packet[3]);
        Assert.Equal(4, packet[4]);
        Assert.Equal(new byte[] { 8, 0, 0 }, packet[5..8]);
        var sum = 0x05 + 4 + 8;
        Assert.Equal((byte)sum, packet[1]);
        Assert.Equal((byte)(sum >> 8), packet[2]);
    }

    [Fact]
    public void BuildPacket_checksum_carries_into_the_high_byte()
    {
        var packet = ConfigClient.BuildPacket(0x21, Enumerable.Repeat((byte)0xFF, 56).ToArray(), 0x010203);
        var sum = packet.Skip(3).Sum(b => b);
        Assert.Equal(sum & 0xFF, packet[1]);
        Assert.Equal((sum >> 8) & 0xFF, packet[2]);
        Assert.Equal(new byte[] { 0x03, 0x02, 0x01 }, packet[5..8]);
    }

    [Fact]
    public void BuildPacket_rejects_oversized_payload() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ConfigClient.BuildPacket(0x21, new byte[57], 0));

    [Fact]
    public async Task ReadConfig_sends_the_documented_sequence_and_assembles_48_bytes()
    {
        var channel = new FakeChannel(8, 64, request =>
        {
            var reply = new byte[8];
            reply[0] = 0x04;
            reply[3] = request[3];
            if (request[3] == ConfigClient.CmdReadConfig)
            {
                var pos = request[5];
                Array.Copy(SavedConfig, pos, reply, 4, 4);
            }
            return reply;
        });

        var config = await new ConfigClient(channel).ReadConfigAsync(TestContext.Current.CancellationToken);

        var commands = channel.Writes.Select(w => w[3]).ToArray();
        byte[] expected = [0x01, .. Enumerable.Repeat((byte)0x03, 10), 0x02, .. Enumerable.Repeat((byte)0x05, 12)];
        Assert.Equal(expected, commands);
        Assert.Equal(SavedConfig, config.ToArray());
    }

    [Fact]
    public async Task WriteConfig_sends_init_payload_commit()
    {
        var channel = new FakeChannel(8, 64, request => [0x04, 0, 0, request[3], 0, 0, 0, 0]);
        var config = new KeyboardConfig(SavedConfig);

        await new ConfigClient(channel).WriteConfigAsync(config, TestContext.Current.CancellationToken);

        Assert.Equal(new byte[] { 0x01, 0x06, 0x02 }, channel.Writes.Select(w => w[3]).ToArray());
        var write = channel.Writes[1];
        Assert.Equal(48, write[4]);
        Assert.Equal(SavedConfig, write[8..56]);
    }

    [Fact]
    public async Task Transact_skips_replies_for_other_commands()
    {
        var first = true;
        var channel = new FakeChannel(8, 64, request =>
        {
            if (!first)
                return [0x04, 0, 0, request[3], 0xAA, 0xBB, 0xCC, 0xDD];
            first = false;
            return [0x04, 0, 0, 0x99, 0, 0, 0, 0];
        });
        var client = new ConfigClient(channel);

        // First command only gets a stray reply, so it must time out rather than accept it.
        await Assert.ThrowsAsync<Gmk87ProtocolException>(() => client.TransactAsync(0x05, new byte[4], 0, TestContext.Current.CancellationToken));
        var data = await client.TransactAsync(0x05, new byte[4], 0, TestContext.Current.CancellationToken);
        Assert.Equal(new byte[] { 0xAA, 0xBB, 0xCC, 0xDD }, data);
    }
}

public class ViaProtocolTests
{
    private static readonly byte[] KeymapDump = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "keymap_v0104.bin"));

    private static FakeChannel ViaDevice(Func<byte[], byte[]> handler) =>
        new(33, 33, request =>
        {
            var payload = request[1..];
            var reply = new byte[33];
            handler(payload).CopyTo(reply, 1);
            return reply;
        });

    [Fact]
    public async Task ReadKeymap_reassembles_the_buffer_from_28_byte_chunks()
    {
        var channel = ViaDevice(p =>
        {
            var reply = p.ToArray();
            var offset = (p[1] << 8) | p[2];
            Array.Copy(KeymapDump, offset, reply, 4, p[3]);
            return reply;
        });

        var map = await new ViaClient(channel).ReadKeymapAsync(4, 8, 15, ct: TestContext.Current.CancellationToken);

        Assert.Equal(KeymapDump, map.ToBuffer());
        Assert.Equal(35, channel.Writes.Count); // ceil(960 / 28)
    }

    [Fact]
    public async Task Unimplemented_commands_are_reported_as_such()
    {
        // The firmware's default branch: byte0 = 0x01, byte2 = 0x09, everything else echoed.
        var channel = ViaDevice(p =>
        {
            var reply = p.ToArray();
            reply[0] = 0x01;
            reply[2] = 0x09;
            return reply;
        });

        var ex = await Assert.ThrowsAsync<Gmk87ProtocolException>(() =>
            new ViaClient(channel).TransactAsync([0x04, 0, 0, 0], TestContext.Current.CancellationToken));
        Assert.Contains("does not implement", ex.Message);
    }

    [Fact]
    public async Task Protocol_version_reads_bytes_1_and_2()
    {
        var channel = ViaDevice(p => [0x01, 0x00, 0x09]);
        Assert.Equal(9, await new ViaClient(channel).GetProtocolVersionAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SetKeycode_packs_big_endian_keycode()
    {
        var channel = ViaDevice(p => p.ToArray());
        await new ViaClient(channel).SetKeycodeAsync(1, 5, 10, 0x5F10, TestContext.Current.CancellationToken);
        Assert.Equal(new byte[] { 0x00, 0x05, 1, 5, 10, 0x5F, 0x10 }, channel.Writes[0][..7]);
    }
}

public class FirmwareInfoTests
{
    [Fact]
    public async Task Parses_the_reply_seen_on_the_real_keyboard()
    {
        // Measured on 2026-10-12: version 0, CRC 0x5C0AB3BC.
        var reply = new byte[64];
        Convert.FromHexString("0501080000000000bcb30a5c").CopyTo(reply, 0);
        var channel = new FakeChannel(64, 64, _ => reply);

        var info = await new FirmwareInfoClient(channel).GetFirmwareInfoAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0u, info.Version);
        Assert.Equal(0x5C0AB3BCu, info.Crc);
        var sent = channel.Writes.Single();
        Assert.Equal(0x05, sent[0]);
        Assert.Equal(0x01, sent[1]);
        Assert.All(sent[4..33], b => Assert.Equal(0xFF, b));
    }
}
