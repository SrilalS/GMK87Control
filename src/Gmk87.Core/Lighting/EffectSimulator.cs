using Gmk87.Core.Layout;
using Gmk87.Core.Model;

namespace Gmk87.Core.Lighting;

public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static readonly Rgb Black = new(0, 0, 0);
}

/// <summary>What the simulator needs to know, in the keyboard's own units.</summary>
public readonly record struct EffectParams(int Effect, int Brightness, int Speed, bool Reverse, bool Rainbow, Rgb Color);

/// <summary>
/// An on-screen approximation of the 19 backlight effects. The firmware's renderers have not been
/// reverse engineered: each effect here is a plausible animation inferred from its name, plus what is
/// known about colour support (built-in rainbow, 4-colour palette). It is a preview, not a replica.
/// </summary>
public sealed class EffectSimulator
{
    /// <summary>The "Trigger colour" palette, from the firmware table at file offset 0x1DC64 (V0104).</summary>
    public static readonly Rgb[] TriggerPalette = [new(0xFF, 0x3C, 0x50), new(0xFF, 0xFF, 0x28), new(0x50, 0xFF, 0x50), new(0x00, 0xFF, 0xFF)];

    private const double PressLifetime = 4.0;

    private readonly KeyPos[] _keys;
    private readonly double _width;
    private readonly double _height;
    private readonly double[] _twinklePhase;
    private readonly double[] _twinklePeriod;
    private readonly double[] _columnOffset;
    private readonly Random _random;
    private readonly List<KeyPress> _presses = [];
    private double _nextDemoPress;
    private int _pressCount;

    public EffectSimulator(IReadOnlyList<LayoutKey> keys, int seed = 87)
    {
        _random = new Random(seed);
        _width = keys.Max(k => k.X + k.Width);
        _height = keys.Max(k => k.Y + k.Height);
        _keys = keys.Select(k => new KeyPos(k.X + k.Width / 2, k.Y + k.Height / 2, k.Row, k.Col)).ToArray();
        _twinklePhase = _keys.Select(_ => _random.NextDouble()).ToArray();
        _twinklePeriod = _keys.Select(_ => 1.5 + _random.NextDouble() * 3).ToArray();
        _columnOffset = Enumerable.Range(0, 64).Select(_ => _random.NextDouble()).ToArray();
    }

    public int KeyCount => _keys.Length;

    /// <summary>When true, reactive effects get a random key press every so often.</summary>
    public bool DemoPresses { get; set; } = true;

    public static bool IsReactive(int effect) => effect is 7 or 8 or 9 or 17;

    /// <summary>Registers a key press (from a click on the preview, or the demo).</summary>
    public void Press(int keyIndex, double time)
    {
        if ((uint)keyIndex >= _keys.Length)
            return;
        _presses.Add(new KeyPress(keyIndex, time, _pressCount++ % TriggerPalette.Length));
        _nextDemoPress = time + 1.2;
    }

    public Rgb[] Render(EffectParams p, double time)
    {
        var colors = new Rgb[_keys.Length];
        var brightness = Math.Clamp(p.Brightness, 0, KeyboardConfig.MaxBrightness) / (double)KeyboardConfig.MaxBrightness;
        if (p.Effect == 0 || brightness <= 0)
            return colors;

        var speed = 0.4 + 0.4 * Math.Clamp(p.Speed, 0, KeyboardConfig.MaxSpeed);
        var t = time * speed;
        var support = LightingCatalog.Effects.FirstOrDefault(e => e.Id == p.Effect)?.Color ?? ColorSupport.Custom;
        var rainbow = support == ColorSupport.BuiltInRainbow || (support == ColorSupport.Custom && p.Rainbow);

        if (IsReactive(p.Effect))
            UpdatePresses(time, speed);

        for (var i = 0; i < _keys.Length; i++)
        {
            var k = _keys[i];
            var x = k.X / _width;
            if (p.Reverse)
                x = 1 - x;
            var y = k.Y / _height;

            var (level, hue) = Shade(p.Effect, i, k, x, y, t, time, speed, p.Reverse);
            Rgb baseColor = p.Effect == 17
                ? PressPaletteColor(i, time, speed)
                : rainbow ? Hsv(Frac(hue), 1, 1) : p.Color;
            colors[i] = Scale(baseColor, Math.Clamp(level, 0, 1) * brightness);
        }

        return colors;
    }

    /// <summary>Returns (intensity 0-1, rainbow hue 0-1) for one key.</summary>
    private (double Level, double Hue) Shade(int effect, int i, KeyPos k, double x, double y, double t, double time, double speed, bool reverse)
    {
        var cx = 0.5;
        var cy = 0.5;
        var dist = Math.Sqrt(Math.Pow((x - cx) * _width, 2) + Math.Pow((y - cy) * _height, 2));

        switch (effect)
        {
            case 1: // Wave: brightness wave travelling across the board
                return (0.2 + 0.8 * Half(Math.Sin(2 * Math.PI * (x * 1.2 - t * 0.5))), x - t * 0.2);
            case 2: // Colour cloud: drifting soft blobs
                var cloud = Half(Math.Sin(x * 5 + t * 0.9) * Math.Cos(y * 4 - t * 0.6) + 0.3 * Math.Sin((x + y) * 7 - t));
                return (0.3 + 0.7 * cloud, cloud * 0.6 + t * 0.05);
            case 3: // Vortex: colour spiralling around the centre
                var angle = Math.Atan2((y - cy) * _height, (x - cx) * _width) / (2 * Math.PI);
                var spiral = angle + t * 0.3 - dist * 0.06;
                return (0.35 + 0.65 * Half(Math.Sin(2 * Math.PI * spiral)), spiral);
            case 4: // Mix colour: the whole board cycles through the rainbow
                return (1, t * 0.1);
            case 5: // Breathe
                var breath = Half(Math.Sin(t * 1.5 - Math.PI / 2));
                return (breath * breath, t * 0.05);
            case 6: // Light: steady
                return (1, x);
            case 7: // Slowly off: pressed keys light up and fade
                return (PressFade(i, time, speed, 0.7), x + t * 0.1);
            case 8: // Stone: a ripple spreads from each press
                return (Ripple(k, time, speed), x + t * 0.1);
            case 9: // Laser: the pressed key's row and column flash
                return (Laser(k, time, speed), x + t * 0.1);
            case 10: // Starry: random twinkles
                var tw = Half(Math.Sin(2 * Math.PI * (time * speed / _twinklePeriod[i] + _twinklePhase[i])));
                return (Math.Pow(tw, 8), _twinklePhase[i]);
            case 11: // Flowers open: rainbow rings blooming from the centre
                return (1, dist * 0.08 - t * 0.25);
            case 12: // Traverse: rainbow bands sweeping top to bottom (as observed on the keyboard, V0112)
                return (1, (reverse ? 1 - y : y) * 0.8 - t * 0.3);
            case 13: // Wave bar: a vertical bar sweeping across
                var bar = Frac(t * 0.25) * 1.2 - 0.1;
                return (Math.Exp(-Math.Pow((x - bar) * _width, 2) / 1.5), x);
            case 14: // Meteor: bright heads with trails, one per row
                var head = Frac(t * 0.3 + _columnOffset[k.Row]) * 1.4 - 0.2;
                var behind = (head - x) * _width;
                return (behind is >= 0 and < 6 ? Math.Pow(1 - behind / 6, 2) : 0, x);
            case 15: // Rain: drops falling down the columns
                var drop = Frac(t * 0.35 + _columnOffset[(int)k.X % 64]) * 1.4 - 0.2;
                var above = (drop - y) * _height;
                return (above is >= 0 and < 2.5 ? 1 - above / 2.5 : 0, x * 0.3 + 0.55);
            case 16: // Scan: a bar bouncing left and right
                var scan = Math.Abs(Frac(t * 0.2) * 2 - 1);
                return (Math.Exp(-Math.Pow((x - scan) * _width, 2) / 0.8), x);
            case 17: // Trigger colour: pressed keys light in the 4-colour palette
                return (PressFade(i, time, speed, 0.9), 0);
            case 18: // Center spread: rings travelling outward
                return (0.35 + 0.65 * Half(Math.Sin(dist * 0.9 - t * 2.5)), dist * 0.05 - t * 0.1);
            default:
                return (1, x);
        }
    }

    private void UpdatePresses(double time, double speed)
    {
        _presses.RemoveAll(p => time - p.Time > PressLifetime / speed + 1);
        if (DemoPresses && time >= _nextDemoPress)
        {
            Press(_random.Next(_keys.Length), time);
            _nextDemoPress = time + (0.25 + _random.NextDouble() * 0.6) / speed;
        }
    }

    private double PressFade(int key, double time, double speed, double rate)
    {
        var level = 0.0;
        foreach (var p in _presses)
        {
            var age = time - p.Time;
            if (p.Key == key && age >= 0)
                level = Math.Max(level, Math.Exp(-age * speed * rate));
        }
        return level;
    }

    private Rgb PressPaletteColor(int key, double time, double speed)
    {
        KeyPress? latest = null;
        foreach (var p in _presses)
            if (p.Key == key && p.Time <= time && (latest is null || p.Time > latest.Value.Time))
                latest = p;
        return latest is { } l ? TriggerPalette[l.PaletteIndex] : TriggerPalette[0];
    }

    private double Ripple(KeyPos k, double time, double speed)
    {
        var level = 0.0;
        foreach (var p in _presses)
        {
            var age = time - p.Time;
            if (age < 0)
                continue;
            var origin = _keys[p.Key];
            var d = Math.Sqrt(Math.Pow(k.X - origin.X, 2) + Math.Pow(k.Y - origin.Y, 2));
            var radius = age * speed * 6;
            level = Math.Max(level, Math.Exp(-Math.Pow(d - radius, 2) / 0.8) * Math.Exp(-age * speed * 0.6));
        }
        return level;
    }

    private double Laser(KeyPos k, double time, double speed)
    {
        var level = 0.0;
        foreach (var p in _presses)
        {
            var age = time - p.Time;
            var origin = _keys[p.Key];
            if (age >= 0 && (Math.Abs(k.Y - origin.Y) < 0.5 || Math.Abs(k.X - origin.X) < 0.5))
                level = Math.Max(level, Math.Exp(-age * speed * 1.2));
        }
        return level;
    }

    private static double Half(double v) => 0.5 + 0.5 * v;

    private static double Frac(double v) => v - Math.Floor(v);

    private static Rgb Scale(Rgb c, double f) =>
        new((byte)Math.Round(c.R * f), (byte)Math.Round(c.G * f), (byte)Math.Round(c.B * f));

    /// <summary>HSV with all inputs 0-1.</summary>
    public static Rgb Hsv(double h, double s, double v)
    {
        var (r, g, b) = ColorMath.FromHueSat((byte)Math.Round(Frac(h) * 255), (byte)Math.Round(s * 255));
        return Scale(new Rgb(r, g, b), v);
    }

    private readonly record struct KeyPos(double X, double Y, int Row, int Col);

    private readonly record struct KeyPress(int Key, double Time, int PaletteIndex);
}
