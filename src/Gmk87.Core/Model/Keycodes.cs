namespace Gmk87.Core.Model;

public sealed record KeycodeEntry(ushort Code, string Label, string Group)
{
    public override string ToString() => $"{Label}  (0x{Code:X4})";
}

/// <summary>
/// One keycode numbering. The GMK87 changed numbering with its firmware:
/// V0104 reports VIA protocol 9 and uses pre-0.19 QMK codes; V0112 reports protocol 11 and uses
/// the modern layout (MO = 0x5220, RGB = 0x7820, custom = 0x7F00), which matches VIA's v11 table.
/// </summary>
public sealed class KeycodeSet
{
    public const ushort None = 0x0000;
    public const ushort Transparent = 0x0001;
    public const ushort VendorBlocked = 0xFFFF;
    private const ushort LegacyFn = 0x5F10;

    private readonly Ranges _r;
    private readonly Dictionary<ushort, string> _basic;
    private readonly Dictionary<ushort, string> _special;

    private KeycodeSet(string name, Ranges ranges, IReadOnlyList<string> vendorNames)
    {
        Name = name;
        _r = ranges;
        VendorNames = vendorNames;
        _basic = BuildBasic(ranges.MouseBase);
        _special = BuildSpecial(ranges, vendorNames);
        PickerEntries = BuildPicker();
    }

    /// <summary>VIA protocol 9 / firmware V0104.</summary>
    public static KeycodeSet Legacy { get; } = new("Legacy (VIA protocol 9)", new Ranges
    {
        ModTap = (0x6000, 0x7FFF), LayerTap = (0x4000, 0x4FFF), LayerMod = null,
        To = 0x5010, ToMask = 0x0F, Momentary = 0x5100, DefaultLayer = 0x5200, ToggleLayer = 0x5300,
        OneShotLayer = 0x5400, OneShotMod = 0x5500, LayerTapToggle = 0x5800, LayerRangeSize = 0x100,
        Rgb = 0x5CC2, Reset = 0x5C00, Fn1 = 0x5F10, Fn2 = 0x5F11, Macro = 0x5F12, Custom = 0x5F80, MouseBase = 0xF0,
    }, [
        "2.4G mode", "BT 1", "BT 2", "BT 3", "Lock Win key", "Desktop light low", "Desktop light high",
        "Logo LED mode", "Logo LED colour", "Screen page", "Key scan delay", "Screen power", "USER12", "USER13",
    ]);

    /// <summary>
    /// VIA protocol 11 / firmware V0112. Vendor order read from the migrated live keymap. The firmware maps
    /// 0x7F00-0x7F0C onto its action table, so Vendor 11 and 12 exist too (purpose not yet known).
    /// </summary>
    public static KeycodeSet Modern { get; } = new("Modern (VIA protocol 11)", new Ranges
    {
        ModTap = (0x2000, 0x3FFF), LayerTap = (0x4000, 0x4FFF), LayerMod = (0x5000, 0x51FF),
        To = 0x5200, ToMask = 0x1F, Momentary = 0x5220, DefaultLayer = 0x5240, ToggleLayer = 0x5260,
        OneShotLayer = 0x5280, OneShotMod = 0x52A0, LayerTapToggle = 0x52C0, LayerRangeSize = 0x20,
        Rgb = 0x7820, Reset = 0x7C00, Fn1 = 0x7700, Fn2 = 0x7701, Macro = 0x7702, Custom = 0x7F00, MouseBase = 0xCD,
    }, [
        "2.4G mode", "BT 1", "BT 2", "BT 3", "Lock Win key", "Logo LED mode", "Logo LED colour",
        "Screen page", "Key scan delay", "Screen power", "Vendor 10", "Vendor 11", "Vendor 12",
    ]);

    /// <summary>Protocol 11 and above use the modern numbering; anything older uses legacy.</summary>
    public static KeycodeSet ForProtocol(int viaProtocol) => viaProtocol >= 11 ? Modern : Legacy;

    public string Name { get; }
    public IReadOnlyList<string> VendorNames { get; }
    public IReadOnlyList<KeycodeEntry> PickerEntries { get; }
    public ushort CustomBase => _r.Custom;

    public string GetLabel(ushort code)
    {
        if (_basic.TryGetValue(code, out var basic))
            return basic;
        if (_special.TryGetValue(code, out var special))
            return special;

        if (code is >= 0x0100 and <= 0x1FFF)
            return $"{ModPrefix((code >> 8) & 0x1F)}({BasicLabel(code & 0xFF)})";
        if (code >= _r.LayerTap.Min && code <= _r.LayerTap.Max)
            return $"LT{(code >> 8) & 0x0F}({BasicLabel(code & 0xFF)})";
        if (code >= _r.ModTap.Min && code <= _r.ModTap.Max)
            return $"MT({ModPrefix((code >> 8) & 0x1F)}, {BasicLabel(code & 0xFF)})";
        if (_r.LayerMod is { } lm && code >= lm.Min && code <= lm.Max)
            return $"LM({(code >> 5) & 0x0F}, {ModPrefix(code & 0x1F)})";

        var layer = code & (_r.LayerRangeSize - 1);
        if (InRange(code, _r.Momentary)) return $"MO({layer})";
        if (InRange(code, _r.DefaultLayer)) return $"DF({layer})";
        if (InRange(code, _r.ToggleLayer)) return $"TG({layer})";
        if (InRange(code, _r.OneShotLayer)) return $"OSL({layer})";
        if (InRange(code, _r.OneShotMod)) return $"OSM({ModPrefix(code & 0x1F)})";
        if (InRange(code, _r.LayerTapToggle)) return $"TT({layer})";
        if (code >= _r.To && code <= _r.To + _r.ToMask) return $"TO({code & _r.ToMask})";
        if (code >= _r.Custom && code < _r.Custom + 0x40) return $"Custom {code - _r.Custom}";

        return $"0x{code:X4}";
    }

    public ushort Momentary(int layer) => (ushort)(_r.Momentary + layer);
    public ushort ToggleLayer(int layer) => (ushort)(_r.ToggleLayer + layer);
    public ushort ToLayer(int layer) => (ushort)(_r.To + layer);

    private bool InRange(ushort code, ushort start) => code >= start && code < start + _r.LayerRangeSize;

    private string BasicLabel(int code) => _basic.TryGetValue((ushort)code, out var name) ? name : $"0x{code:X2}";

    private static string ModPrefix(int mods)
    {
        var side = (mods & 0x10) != 0 ? "R" : "L";
        var parts = new List<string>();
        if ((mods & 0x01) != 0) parts.Add("Ctl");
        if ((mods & 0x02) != 0) parts.Add("Sft");
        if ((mods & 0x04) != 0) parts.Add("Alt");
        if ((mods & 0x08) != 0) parts.Add("Gui");
        return side + string.Join("+", parts);
    }

    private static Dictionary<ushort, string> BuildBasic(ushort mouseBase)
    {
        var d = new Dictionary<ushort, string>
        {
            [0x00] = "None", [0x01] = "Transparent",
            [0x28] = "Enter", [0x29] = "Esc", [0x2A] = "Backspace", [0x2B] = "Tab", [0x2C] = "Space",
            [0x2D] = "-", [0x2E] = "=", [0x2F] = "[", [0x30] = "]", [0x31] = "\\", [0x32] = "ISO #",
            [0x33] = ";", [0x34] = "'", [0x35] = "`", [0x36] = ",", [0x37] = ".", [0x38] = "/", [0x39] = "Caps Lock",
            [0x46] = "Print Screen", [0x47] = "Scroll Lock", [0x48] = "Pause", [0x49] = "Insert", [0x4A] = "Home",
            [0x4B] = "Page Up", [0x4C] = "Delete", [0x4D] = "End", [0x4E] = "Page Down",
            [0x4F] = "Right", [0x50] = "Left", [0x51] = "Down", [0x52] = "Up",
            [0x53] = "Num Lock", [0x54] = "KP /", [0x55] = "KP *", [0x56] = "KP -", [0x57] = "KP +", [0x58] = "KP Enter",
            [0x62] = "KP 0", [0x63] = "KP .", [0x64] = "ISO \\", [0x65] = "Menu", [0x66] = "Power", [0x67] = "KP =",
            [0x74] = "Execute", [0x75] = "Help", [0x77] = "Select", [0x78] = "Stop", [0x79] = "Again",
            [0x7A] = "Undo", [0x7B] = "Cut", [0x7C] = "Copy", [0x7D] = "Paste", [0x7E] = "Find",
            [0x85] = "KP ,", [0x87] = "Int1 (Ro)", [0x88] = "Int2 (Kana)", [0x89] = "Int3 (Yen)",
            [0x8A] = "Int4 (Henkan)", [0x8B] = "Int5 (Muhenkan)", [0x8C] = "Int6", [0x8D] = "Int7", [0x8E] = "Int8", [0x8F] = "Int9",
            [0xA5] = "System Power", [0xA6] = "System Sleep", [0xA7] = "System Wake",
            [0xA8] = "Mute", [0xA9] = "Volume Up", [0xAA] = "Volume Down", [0xAB] = "Next Track", [0xAC] = "Prev Track",
            [0xAD] = "Media Stop", [0xAE] = "Play/Pause", [0xAF] = "Media Select", [0xB0] = "Eject", [0xB1] = "Mail",
            [0xB2] = "Calculator", [0xB3] = "My Computer", [0xB4] = "Web Search", [0xB5] = "Web Home", [0xB6] = "Web Back",
            [0xB7] = "Web Forward", [0xB8] = "Web Stop", [0xB9] = "Web Refresh", [0xBA] = "Web Favorites",
            [0xBB] = "Fast Forward", [0xBC] = "Rewind", [0xBD] = "Brightness Up", [0xBE] = "Brightness Down",
            [0xE0] = "Left Ctrl", [0xE1] = "Left Shift", [0xE2] = "Left Alt", [0xE3] = "Left Win",
            [0xE4] = "Right Ctrl", [0xE5] = "Right Shift", [0xE6] = "Right Alt", [0xE7] = "Right Win",
        };
        for (var i = 0; i < 26; i++)
            d[(ushort)(0x04 + i)] = ((char)('A' + i)).ToString();
        for (var i = 0; i < 9; i++)
            d[(ushort)(0x1E + i)] = (i + 1).ToString();
        d[0x27] = "0";
        for (var i = 0; i < 12; i++)
            d[(ushort)(0x3A + i)] = $"F{i + 1}";
        for (var i = 0; i < 12; i++)
            d[(ushort)(0x68 + i)] = $"F{i + 13}";
        for (var i = 0; i < 9; i++)
            d[(ushort)(0x59 + i)] = $"KP {i + 1}";
        for (var i = 0; i < 9; i++)
            d[(ushort)(0x90 + i)] = $"Lang{i + 1}";

        // Mouse block: legacy 0xF0..0xFF (5 buttons), modern 0xCD..0xDF (8 buttons).
        string[] mouse = mouseBase == 0xF0
            ? ["Mouse Up", "Mouse Down", "Mouse Left", "Mouse Right", "Mouse Btn1", "Mouse Btn2", "Mouse Btn3", "Mouse Btn4",
               "Mouse Btn5", "Wheel Up", "Wheel Down", "Wheel Left", "Wheel Right", "Mouse Accel 0", "Mouse Accel 1", "Mouse Accel 2"]
            : ["Mouse Up", "Mouse Down", "Mouse Left", "Mouse Right", "Mouse Btn1", "Mouse Btn2", "Mouse Btn3", "Mouse Btn4",
               "Mouse Btn5", "Mouse Btn6", "Mouse Btn7", "Mouse Btn8", "Wheel Up", "Wheel Down", "Wheel Left", "Wheel Right",
               "Mouse Accel 0", "Mouse Accel 1", "Mouse Accel 2"];
        for (var i = 0; i < mouse.Length; i++)
            d[(ushort)(mouseBase + i)] = mouse[i];
        if (mouseBase != 0xF0)
        {
            d[0xC1] = "Mission Control";
            d[0xC2] = "Launchpad";
        }
        return d;
    }

    private static Dictionary<ushort, string> BuildSpecial(Ranges r, IReadOnlyList<string> vendorNames)
    {
        string[] rgb = ["RGB Toggle", "RGB Mode +", "RGB Mode -", "RGB Hue +", "RGB Hue -", "RGB Sat +", "RGB Sat -",
                        "RGB Bright +", "RGB Bright -", "RGB Speed +", "RGB Speed -"];
        var d = new Dictionary<ushort, string>
        {
            [r.Reset] = "Reset",
            [r.Fn1] = "Fn (MO1)",
            [r.Fn2] = "Fn2 (MO2)",
            [VendorBlocked] = "Blocked (vendor)",
        };
        // The GMK87 Fn key is 0x5F10 on both firmwares. On V0112 the migrated MO(1) (0x5221) does NOT work as Fn
        // (Fn+Backspace did nothing), while 0x5F10 does (both confirmed on the keyboard 2026-10-12).
        if (r.Fn1 != LegacyFn)
        {
            d[LegacyFn] = "Fn";
            d[r.Fn1] = "FN_MO13";
            d[r.Fn2] = "FN_MO23";
        }
        for (var i = 0; i < rgb.Length; i++)
            d[(ushort)(r.Rgb + i)] = rgb[i];
        for (var i = 0; i < 16; i++)
            d[(ushort)(r.Macro + i)] = $"Macro {i}";
        for (var i = 0; i < vendorNames.Count; i++)
            d[(ushort)(r.Custom + i)] = vendorNames[i];
        return d;
    }

    private List<KeycodeEntry> BuildPicker()
    {
        var list = new List<KeycodeEntry>();
        foreach (var (code, name) in _basic.OrderBy(p => p.Key))
        {
            var group = code switch
            {
                <= 0x01 => "Special",
                >= 0xE0 and <= 0xE7 => "Modifiers",
                >= 0xA5 and <= 0xBE => "Media",
                _ when name.StartsWith("Mouse") || name.StartsWith("Wheel") => "Mouse",
                _ => "Basic",
            };
            list.Add(new KeycodeEntry(code, name, group));
        }

        list.Add(new KeycodeEntry(LegacyFn, "Fn", "Layers"));
        for (var layer = 0; layer < 4; layer++)
        {
            list.Add(new KeycodeEntry(Momentary(layer), $"MO({layer})", "Layers"));
            list.Add(new KeycodeEntry(ToggleLayer(layer), $"TG({layer})", "Layers"));
            list.Add(new KeycodeEntry(ToLayer(layer), $"TO({layer})", "Layers"));
        }

        var seen = list.Select(e => e.Code).ToHashSet();
        foreach (var (code, name) in _special.OrderBy(p => p.Key))
        {
            if (!seen.Add(code))
                continue;
            var group = name.StartsWith("RGB") ? "Lighting"
                : name.StartsWith("Macro") ? "Macros"
                : code >= _r.Custom && code < VendorBlocked ? "Vendor"
                : "Special";
            list.Add(new KeycodeEntry(code, name, group));
        }

        return list;
    }

    private sealed class Ranges
    {
        public (ushort Min, ushort Max) ModTap { get; init; }
        public (ushort Min, ushort Max) LayerTap { get; init; }
        public (ushort Min, ushort Max)? LayerMod { get; init; }
        public ushort To { get; init; }
        public ushort ToMask { get; init; }
        public ushort Momentary { get; init; }
        public ushort DefaultLayer { get; init; }
        public ushort ToggleLayer { get; init; }
        public ushort OneShotLayer { get; init; }
        public ushort OneShotMod { get; init; }
        public ushort LayerTapToggle { get; init; }
        public ushort LayerRangeSize { get; init; }
        public ushort Rgb { get; init; }
        public ushort Reset { get; init; }
        public ushort Fn1 { get; init; }
        public ushort Fn2 { get; init; }
        public ushort Macro { get; init; }
        public ushort Custom { get; init; }
        public ushort MouseBase { get; init; }
    }
}
