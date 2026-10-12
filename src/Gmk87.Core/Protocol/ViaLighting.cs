namespace Gmk87.Core.Protocol;

/// <summary>Backlight state as exposed by firmware V0112 on VIA V3 channel 3 (qmk_rgb_matrix).</summary>
public sealed record ViaLightingState(int Brightness, int Effect, int Speed, byte Hue, byte Saturation);

/// <summary>
/// VIA V3 lighting on channel 3. Value ids and observed ranges were read from the keyboard on 2026-10-12:
/// 1 = brightness (0-9), 2 = effect (0-18), 3 = speed, 4 = colour as hue + saturation bytes.
/// </summary>
public static class ViaLighting
{
    public const byte Channel = 3;
    public const byte BrightnessId = 1;
    public const byte EffectId = 2;
    public const byte SpeedId = 3;
    public const byte ColorId = 4;

    public static async Task<ViaLightingState> ReadAsync(ViaClient via, CancellationToken ct = default)
    {
        var brightness = (await via.GetCustomValueAsync(Channel, BrightnessId, 1, ct).ConfigureAwait(false))[0];
        var effect = (await via.GetCustomValueAsync(Channel, EffectId, 1, ct).ConfigureAwait(false))[0];
        var speed = (await via.GetCustomValueAsync(Channel, SpeedId, 1, ct).ConfigureAwait(false))[0];
        var color = await via.GetCustomValueAsync(Channel, ColorId, 2, ct).ConfigureAwait(false);
        return new ViaLightingState(brightness, effect, speed, color[0], color[1]);
    }

    /// <summary>WRITES every value that differs from <paramref name="current"/>, then saves the channel once.</summary>
    public static async Task WriteAsync(ViaClient via, ViaLightingState current, ViaLightingState desired, CancellationToken ct = default)
    {
        var changed = false;
        if (desired.Effect != current.Effect)
        {
            await via.SetCustomValueAsync(Channel, EffectId, [(byte)desired.Effect], ct).ConfigureAwait(false);
            changed = true;
        }
        if (desired.Brightness != current.Brightness)
        {
            await via.SetCustomValueAsync(Channel, BrightnessId, [(byte)desired.Brightness], ct).ConfigureAwait(false);
            changed = true;
        }
        if (desired.Speed != current.Speed)
        {
            await via.SetCustomValueAsync(Channel, SpeedId, [(byte)desired.Speed], ct).ConfigureAwait(false);
            changed = true;
        }
        if (desired.Hue != current.Hue || desired.Saturation != current.Saturation)
        {
            await via.SetCustomValueAsync(Channel, ColorId, [desired.Hue, desired.Saturation], ct).ConfigureAwait(false);
            changed = true;
        }
        if (changed)
            await via.SaveCustomAsync(Channel, ct).ConfigureAwait(false);
    }
}
