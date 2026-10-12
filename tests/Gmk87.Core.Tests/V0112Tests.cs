using Gmk87.Core.Model;
using Gmk87.Core.Protocol;

namespace Gmk87.Core.Tests;

/// <summary>Firmware V0112 (VIA protocol 11): modern keycodes and VIA V3 lighting on channel 3.</summary>
public class V0112Tests
{
    // Read from the keyboard right after flashing V0112 on 2026-10-12.
    private static Keymap Live() => Keymap.FromBuffer(
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "keymap_v0112.bin")), 4, 8, 15);

    [Theory]
    [InlineData(9, "Legacy (VIA protocol 9)")]
    [InlineData(10, "Legacy (VIA protocol 9)")]
    [InlineData(11, "Modern (VIA protocol 11)")]
    [InlineData(12, "Modern (VIA protocol 11)")]
    public void Scheme_follows_protocol(int protocol, string expected) => Assert.Equal(expected, KeycodeSet.ForProtocol(protocol).Name);

    [Fact]
    public void Migrated_keymap_labels_match_the_research()
    {
        var map = Live();
        var k = KeycodeSet.Modern;
        Assert.Equal("MO(1)", k.GetLabel(map[0, 5, 10]));      // what the migration wrote; does NOT work as Fn on V0112
        Assert.Equal("2.4G mode", k.GetLabel(map[1, 2, 4]));   // Fn+R
        Assert.Equal("BT 1", k.GetLabel(map[1, 2, 1]));        // Fn+Q
        Assert.Equal("Screen power", k.GetLabel(map[1, 4, 3])); // Fn+X
        Assert.Equal("Logo LED mode", k.GetLabel(map[1, 3, 14]));
        Assert.Equal("RGB Toggle", k.GetLabel(map[1, 1, 13])); // Fn+Backspace
        Assert.Equal("Reset", k.GetLabel(map[1, 5, 8]));       // Fn+Space
        Assert.Equal("Vendor 10", k.GetLabel(map[1, 4, 13]));  // Fn+Right Shift, new in V0112
    }

    [Fact]
    public void Every_key_on_the_two_main_layers_gets_a_name()
    {
        var map = Live();
        for (var l = 0; l < 2; l++)
            for (var r = 0; r < 8; r++)
                for (var c = 0; c < 15; c++)
                {
                    if ((r, c) == (6, 14))
                        continue; // holds a legacy-numbered 0x5F8B, a position with no physical key
                    Assert.DoesNotContain("0x", KeycodeSet.Modern.GetLabel(map[l, r, c]));
                }
    }

    [Theory]
    [InlineData(0x5200, "TO(0)")]
    [InlineData(0x5263, "TG(3)")]
    [InlineData(0x2204, "MT(LSft, A)")]
    [InlineData(0x00CD, "Mouse Up")]
    [InlineData(0x00D1, "Mouse Btn1")]
    [InlineData(0x7702, "Macro 0")]
    [InlineData(0x7F20, "Custom 32")]
    [InlineData(0x5F10, "Fn")]
    [InlineData(0x7700, "FN_MO13")]
    public void Modern_ranges(int code, string expected) => Assert.Equal(expected, KeycodeSet.Modern.GetLabel((ushort)code));

    [Fact]
    public void Modern_picker_is_unique_and_uses_modern_layers()
    {
        var entries = KeycodeSet.Modern.PickerEntries;
        Assert.Equal(entries.Count, entries.Select(e => e.Code).Distinct().Count());
        Assert.Contains(entries, e => e is { Code: 0x5221, Label: "MO(1)" });
        Assert.DoesNotContain(entries, e => e.Code == 0x5101);
    }

    [Theory]
    [InlineData(255, 0, 0, 0, 255)]
    [InlineData(0, 255, 0, 85, 255)]
    [InlineData(0, 0, 255, 170, 255)]
    [InlineData(255, 255, 255, 0, 0)]
    [InlineData(0, 0, 0, 0, 0)]
    public void Rgb_to_via_hue_sat(byte r, byte g, byte b, byte hue, byte sat) =>
        Assert.Equal((hue, sat), ColorMath.ToHueSat(r, g, b));

    [Fact]
    public void Hue_sat_round_trips_within_one_step()
    {
        for (var hue = 0; hue < 256; hue += 5)
        {
            var (r, g, b) = ColorMath.FromHueSat((byte)hue, 255);
            var (h2, s2) = ColorMath.ToHueSat(r, g, b);
            Assert.InRange(Math.Min(Math.Abs(h2 - hue), 256 - Math.Abs(h2 - hue)), 0, 1);
            Assert.Equal(255, s2);
        }
    }

    private static FakeChannel ViaDevice(Func<byte[], byte[]> handler) =>
        new(33, 33, request =>
        {
            var reply = new byte[33];
            handler(request[1..]).CopyTo(reply, 1);
            return reply;
        });

    [Fact]
    public async Task Reads_lighting_from_channel_3()
    {
        // Replies observed on the keyboard: brightness 9, effect 1, speed 2, hue 0 sat 255.
        var values = new Dictionary<byte, byte[]> { [1] = [9], [2] = [1], [3] = [2], [4] = [0, 255] };
        var channel = ViaDevice(p => [0x08, p[1], p[2], .. values[p[2]]]);

        var state = await ViaLighting.ReadAsync(new ViaClient(channel), TestContext.Current.CancellationToken);

        Assert.Equal(new ViaLightingState(9, 1, 2, 0, 255), state);
        Assert.All(channel.Writes, w => Assert.Equal(new byte[] { 0x08, 3 }, w[1..3]));
    }

    [Fact]
    public async Task Writes_only_changed_values_then_saves_once()
    {
        var channel = ViaDevice(p => p.ToArray());
        var current = new ViaLightingState(9, 1, 2, 0, 255);

        await ViaLighting.WriteAsync(new ViaClient(channel), current, current with { Effect = 5, Hue = 170 },
            TestContext.Current.CancellationToken);

        var sent = channel.Writes.Select(w => Convert.ToHexString(w[1..6])).ToList();
        Assert.Equal(["0703020500", "070304AAFF", "0903000000"], sent);
        Assert.Equal(3, sent.Count);
    }

    [Fact]
    public async Task Nothing_is_written_when_nothing_changed()
    {
        var channel = ViaDevice(p => p.ToArray());
        var current = new ViaLightingState(9, 1, 2, 0, 255);
        await ViaLighting.WriteAsync(new ViaClient(channel), current, current, TestContext.Current.CancellationToken);
        Assert.Empty(channel.Writes);
    }
}
