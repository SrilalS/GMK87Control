namespace Gmk87.Core.Model;

/// <summary>RGB to/from VIA's colour format: hue and saturation as 0-255 bytes (QMK style), full value.</summary>
public static class ColorMath
{
    public static (byte Hue, byte Saturation) ToHueSat(byte red, byte green, byte blue)
    {
        double r = red / 255.0, g = green / 255.0, b = blue / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var delta = max - Math.Min(r, Math.Min(g, b));

        double degrees = 0;
        if (delta > 0)
        {
            degrees = max == r ? 60 * ((g - b) / delta % 6)
                : max == g ? 60 * ((b - r) / delta + 2)
                : 60 * ((r - g) / delta + 4);
            if (degrees < 0)
                degrees += 360;
        }

        var saturation = max == 0 ? 0 : delta / max;
        return ((byte)Math.Min(255, Math.Round(degrees / 360 * 255)), (byte)Math.Round(saturation * 255));
    }

    public static (byte Red, byte Green, byte Blue) FromHueSat(byte hue, byte saturation)
    {
        var h = hue / 255.0 * 360;
        var c = saturation / 255.0;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = 1 - c;
        var (r, g, b) = h switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        return ((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }
}
