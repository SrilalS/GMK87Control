using Gmk87.Core.Imaging;
using Gmk87.Core.Layout;
using Gmk87.Core.Model;

namespace Gmk87.Core.Tests;

public class KeyboardConfigTests
{
    [Fact]
    public void Decodes_the_saved_config()
    {
        var c = new KeyboardConfig(ConfigProtocolTests.SavedConfig);

        Assert.Equal(5, c.Effect);
        Assert.Equal(9, c.Brightness);
        Assert.Equal(1, c.Speed);
        Assert.False(c.Rainbow);
        Assert.Equal((255, 0, 0), (c.Red, c.Green, c.Blue));
        Assert.Equal(new DateTime(2026, 9, 6, 15, 45, 51), c.Clock);
        Assert.Equal(100, c.FrameDelayMs);
        Assert.Equal(ScreenMode.Clock, c.Screen);
        Assert.Equal(17, c.Slot2Frames);
    }

    [Theory]
    [InlineData(0xFF, true, 11)]
    [InlineData(0x00, true, 11)]
    [InlineData(9, false, 9)]
    [InlineData(11, false, 11)]
    public void RepairProtocolSelector_only_touches_invalid_values(byte selector, bool repaired, byte expected)
    {
        var raw = new byte[KeyboardConfig.ExtendedLength];
        raw[0x31] = selector;
        var c = new KeyboardConfig(raw);

        Assert.Equal(repaired, c.RepairProtocolSelector());
        Assert.Equal(expected, c.ProtocolSelector);
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(1, 3)]
    [InlineData(4, 0)]
    public void SpeedLevel_is_the_inverse_of_the_raw_byte_like_the_V0112_VIA_handler(int level, int raw)
    {
        var c = new KeyboardConfig(new byte[48]) { SpeedLevel = level };
        Assert.Equal(raw, c.Speed);
        Assert.Equal(level, c.SpeedLevel);
    }

    [Fact]
    public void A_48_byte_block_has_no_protocol_selector()
    {
        var c = new KeyboardConfig(new byte[48]);
        Assert.Null(c.ProtocolSelector);
        Assert.True(c.HasValidProtocolSelector);
        Assert.False(c.RepairProtocolSelector());
    }

    [Fact]
    public void Rejects_other_lengths() => Assert.Throws<ArgumentException>(() => new KeyboardConfig(new byte[50]));

    [Fact]
    public void SetClock_writes_bcd_and_sunday_zero()
    {
        var c = new KeyboardConfig(new byte[48]);
        c.SetClock(new DateTime(2026, 9, 6, 15, 45, 51));
        var raw = c.ToArray();
        Assert.Equal(ConfigProtocolTests.SavedConfig[35..42], raw[35..42]);
    }

    [Fact]
    public void Setters_clamp_to_firmware_ranges()
    {
        var c = new KeyboardConfig(new byte[48]) { Effect = 40, Brightness = 99, Speed = 7, FrameDelayMs = 10 };
        Assert.Equal((18, 9, 4, 60), (c.Effect, c.Brightness, c.Speed, c.FrameDelayMs));
    }

    [Fact]
    public void Rainbow_keeps_existing_nonzero_value()
    {
        var raw = new byte[48];
        raw[5] = 0xFF; // what the firmware's own hue key stores
        var c = new KeyboardConfig(raw) { Rainbow = true };
        Assert.Equal(0xFF, c.ToArray()[5]);
        c.Rainbow = false;
        Assert.Equal(0, c.ToArray()[5]);
    }

    [Fact]
    public void Unknown_bytes_survive_edits()
    {
        var c = new KeyboardConfig(ConfigProtocolTests.SavedConfig) { Effect = 1, Red = 0, Blue = 255, LogoColor = 5 };
        var raw = c.ToArray();
        foreach (var i in new[] { 0, 9, 20, 23, 30, 42, 45, 47 })
            Assert.Equal(ConfigProtocolTests.SavedConfig[i], raw[i]);
    }
}

public class KeymapAndKeycodeTests
{
    private static Keymap Dump() => Keymap.FromBuffer(
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "keymap_v0104.bin")), 4, 8, 15);

    [Fact]
    public void Indexes_match_the_documented_layout()
    {
        var map = Dump();
        Assert.Equal(0x0029, map[0, 0, 0]);      // Esc
        Assert.Equal(0x5F10, map[0, 5, 10]);     // Fn
        Assert.Equal(0x5F8D, map[0, 6, 14]);     // USER13
        Assert.Equal(0x00AA, map[0, 7, 0]);      // encoder: volume down
        Assert.Equal(0xFFFF, map[1, 0, 0]);
    }

    [Fact]
    public void Json_round_trip_and_diff()
    {
        var map = Dump();
        var copy = Keymap.FromJson(map.ToJson());
        Assert.Empty(map.DiffTo(copy));

        copy[2, 3, 4] = 0x0004;
        var diff = Assert.Single(map.DiffTo(copy));
        Assert.Equal((2, 3, 4, (ushort)0x0004), diff);
    }

    [Theory]
    [InlineData(0x0029, "Esc")]
    [InlineData(0x0004, "A")]
    [InlineData(0x0027, "0")]
    [InlineData(0x5F10, "Fn (MO1)")]
    [InlineData(0x5F80, "2.4G mode")]
    [InlineData(0x5F8D, "USER13")]
    [InlineData(0x5101, "MO(1)")]
    [InlineData(0x0204, "LSft(A)")]
    [InlineData(0x1104, "RCtl(A)")]
    [InlineData(0x4105, "LT1(B)")]
    [InlineData(0x5CC5, "RGB Hue +")]
    [InlineData(0xFFFF, "Blocked (vendor)")]
    [InlineData(0x6104, "MT(LCtl, A)")]
    [InlineData(0x7204, "MT(RSft, A)")]
    public void Labels(int code, string expected) => Assert.Equal(expected, KeycodeSet.Legacy.GetLabel((ushort)code));

    [Fact]
    public void Every_key_in_the_dump_gets_a_label()
    {
        var map = Dump();
        for (var l = 0; l < 4; l++)
            for (var r = 0; r < 8; r++)
                for (var c = 0; c < 15; c++)
                    Assert.DoesNotContain("0x", KeycodeSet.Legacy.GetLabel(map[l, r, c]));
    }

    [Fact]
    public void Picker_has_unique_codes()
    {
        var codes = KeycodeSet.Legacy.PickerEntries.Select(e => e.Code).ToList();
        Assert.Equal(codes.Count, codes.Distinct().Count());
    }
}

public class LayoutTests
{
    [Fact]
    public void Embedded_layout_parses()
    {
        var layout = KeyboardLayout.LoadEmbedded();

        Assert.Equal("ZUOYA GMK87", layout.Name);
        Assert.Equal((8, 15), (layout.Rows, layout.Cols));
        Assert.Equal(88, layout.Keys.Count);
        Assert.Equal(88, layout.Keys.Select(k => (k.Row, k.Col)).Distinct().Count());
        Assert.All(layout.Keys, k => Assert.True(k.Row < 8 && k.Col < 15));
    }

    [Fact]
    public void Kle_offsets_and_widths_accumulate()
    {
        var layout = KeyboardLayout.LoadEmbedded();
        var esc = layout.Keys.Single(k => (k.Row, k.Col) == (0, 0));
        var f1 = layout.Keys.Single(k => (k.Row, k.Col) == (0, 1));
        var space = layout.Keys.Single(k => (k.Row, k.Col) == (5, 8));

        Assert.Equal(0.1, esc.X, 3);
        Assert.Equal(0.1 + 1 + 0.6, f1.X, 3);
        Assert.Equal(6.0, space.Width);
        Assert.True(space.Y > esc.Y + 5);
    }
}

public class DisplayImageTests
{
    [Fact]
    public void Rgb565_packing()
    {
        Assert.Equal(0xF800, DisplayImage.ToRgb565(255, 0, 0));
        Assert.Equal(0x07E0, DisplayImage.ToRgb565(0, 255, 0));
        Assert.Equal(0x001F, DisplayImage.ToRgb565(0, 0, 255));
    }

    [Fact]
    public void EncodeFrame_is_big_endian_and_padded()
    {
        var bgra = new byte[240 * 135 * 4];
        for (var i = 0; i < 240 * 135; i++)
        {
            bgra[i * 4 + 2] = 255; // red
            bgra[i * 4 + 3] = 255;
        }

        var frame = DisplayImage.EncodeFrame(bgra);

        Assert.Equal(65536, frame.Length);
        Assert.Equal(0xF8, frame[0]);
        Assert.Equal(0x00, frame[1]);
        Assert.All(frame[64800..], b => Assert.Equal(0, b));
    }

    [Fact]
    public void ResizeCover_keeps_solid_colour_and_target_size()
    {
        var src = new byte[100 * 100 * 4];
        for (var i = 0; i < 100 * 100; i++)
        {
            src[i * 4] = 10;
            src[i * 4 + 1] = 20;
            src[i * 4 + 2] = 30;
            src[i * 4 + 3] = 255;
        }

        var dst = DisplayImage.ResizeCover(src, 100, 100);

        Assert.Equal(240 * 135 * 4, dst.Length);
        Assert.Equal(new byte[] { 10, 20, 30, 255 }, dst[..4]);
        Assert.Equal(new byte[] { 10, 20, 30, 255 }, dst[^4..]);
    }

    [Fact]
    public void BuildUpload_fills_empty_slot_and_enforces_limit()
    {
        var data = DisplayImage.BuildUpload([DisplayImage.BlankFrame(), DisplayImage.BlankFrame()], [], out var s1, out var s2);
        Assert.Equal((2, 1), (s1, s2));
        Assert.Equal(3 * 65536, data.Length);

        var tooMany = Enumerable.Range(0, 37).Select(_ => DisplayImage.BlankFrame()).ToList();
        Assert.Throws<ArgumentException>(() => DisplayImage.BuildUpload(tooMany, [], out _, out _));
    }
}
