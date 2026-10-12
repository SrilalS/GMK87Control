namespace Gmk87.Core.Model;

/// <summary>
/// The 48-byte settings block behind the 0xFF1C interface. The firmware keeps the same layout in RAM
/// at gp+0xE0, which is how the field meanings and ranges below were confirmed.
/// Unknown bytes are carried through untouched on every write.
/// </summary>
public sealed class KeyboardConfig
{
    public const int Length = 48;

    private const int EffectOffset = 1;
    private const int BrightnessOffset = 2;
    private const int SpeedOffset = 3;
    private const int DirectionOffset = 4;
    private const int RainbowOffset = 5;
    private const int RedOffset = 6;
    private const int GreenOffset = 7;
    private const int BlueOffset = 8;
    private const int WinLockOffset = 21;
    private const int LogoModeOffset = 28;
    private const int LogoBrightnessOffset = 29;
    private const int LogoRainbowOffset = 31;
    private const int LogoColorOffset = 32;
    private const int ScreenOffset = 33;
    private const int Slot1FramesOffset = 34;
    private const int ClockOffset = 35;
    private const int FrameDelayOffset = 43;
    private const int Slot2FramesOffset = 46;

    public const int MaxEffect = 18;
    public const int MaxBrightness = 9;
    public const int MaxSpeed = 4;
    public const int MaxLogoMode = 4;
    public const int MaxLogoBrightness = 9;
    public const int MaxLogoColor = 8;
    public const int MinFrameDelayMs = 60;

    private readonly byte[] _raw;

    public KeyboardConfig(ReadOnlySpan<byte> raw)
    {
        if (raw.Length != Length)
            throw new ArgumentException($"Config block must be {Length} bytes.", nameof(raw));
        _raw = raw.ToArray();
    }

    public byte[] ToArray() => (byte[])_raw.Clone();

    public KeyboardConfig Clone() => new(_raw);

    public int Effect { get => _raw[EffectOffset]; set => _raw[EffectOffset] = Clamp(value, MaxEffect); }
    public int Brightness { get => _raw[BrightnessOffset]; set => _raw[BrightnessOffset] = Clamp(value, MaxBrightness); }
    public int Speed { get => _raw[SpeedOffset]; set => _raw[SpeedOffset] = Clamp(value, MaxSpeed); }

    /// <summary>Animation direction; gmk87-node calls 0 left-to-right and 1 right-to-left.</summary>
    public bool ReverseDirection { get => _raw[DirectionOffset] != 0; set => _raw[DirectionOffset] = (byte)(value ? 1 : 0); }

    /// <summary>
    /// Rainbow instead of the fixed colour. Writes 0xFF, the value the firmware's own hue key stores
    /// (and the value V0112 had after flashing); gmk87-node used 1.
    /// </summary>
    public bool Rainbow
    {
        get => _raw[RainbowOffset] != 0;
        set
        {
            if (value != Rainbow)
                _raw[RainbowOffset] = (byte)(value ? 0xFF : 0);
        }
    }

    public byte Red { get => _raw[RedOffset]; set => _raw[RedOffset] = value; }
    public byte Green { get => _raw[GreenOffset]; set => _raw[GreenOffset] = value; }
    public byte Blue { get => _raw[BlueOffset]; set => _raw[BlueOffset] = value; }

    public bool WinLock { get => _raw[WinLockOffset] != 0; set => _raw[WinLockOffset] = (byte)(value ? 1 : 0); }

    public int LogoMode { get => _raw[LogoModeOffset]; set => _raw[LogoModeOffset] = Clamp(value, MaxLogoMode); }
    /// <summary>Logo light bar brightness, 0 = off. gmk87-node called this byte "saturation"; tested on the keyboard 2026-10-12.</summary>
    public int LogoBrightness { get => _raw[LogoBrightnessOffset]; set => _raw[LogoBrightnessOffset] = Clamp(value, MaxLogoBrightness); }
    public bool LogoRainbow { get => _raw[LogoRainbowOffset] != 0; set => _raw[LogoRainbowOffset] = (byte)(value ? 1 : 0); }
    public int LogoColor { get => _raw[LogoColorOffset]; set => _raw[LogoColorOffset] = Clamp(value, MaxLogoColor); }

    public ScreenMode Screen { get => (ScreenMode)_raw[ScreenOffset]; set => _raw[ScreenOffset] = (byte)value; }
    public int Slot1Frames { get => _raw[Slot1FramesOffset]; set => _raw[Slot1FramesOffset] = (byte)value; }
    public int Slot2Frames { get => _raw[Slot2FramesOffset]; set => _raw[Slot2FramesOffset] = (byte)value; }

    public int FrameDelayMs
    {
        get => _raw[FrameDelayOffset] | (_raw[FrameDelayOffset + 1] << 8);
        set
        {
            var ms = Math.Clamp(value, MinFrameDelayMs, ushort.MaxValue);
            _raw[FrameDelayOffset] = (byte)ms;
            _raw[FrameDelayOffset + 1] = (byte)(ms >> 8);
        }
    }

    /// <summary>
    /// The keyboard clock as stored (BCD). Day of week is 0 = Sunday, which matches what the
    /// keyboard reported on 2026-09-06, a Sunday.
    /// </summary>
    public DateTime? Clock
    {
        get
        {
            try
            {
                return new DateTime(2000 + FromBcd(_raw[ClockOffset + 6]), FromBcd(_raw[ClockOffset + 5]), FromBcd(_raw[ClockOffset + 4]),
                    FromBcd(_raw[ClockOffset + 2]), FromBcd(_raw[ClockOffset + 1]), FromBcd(_raw[ClockOffset]));
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }
    }

    public void SetClock(DateTime time)
    {
        _raw[ClockOffset] = ToBcd(time.Second);
        _raw[ClockOffset + 1] = ToBcd(time.Minute);
        _raw[ClockOffset + 2] = ToBcd(time.Hour);
        _raw[ClockOffset + 3] = (byte)time.DayOfWeek;
        _raw[ClockOffset + 4] = ToBcd(time.Day);
        _raw[ClockOffset + 5] = ToBcd(time.Month);
        _raw[ClockOffset + 6] = ToBcd(time.Year % 100);
    }

    internal static byte ToBcd(int value)
    {
        if (value is < 0 or > 99)
            throw new ArgumentOutOfRangeException(nameof(value));
        return (byte)(((value / 10) << 4) | (value % 10));
    }

    internal static int FromBcd(byte value) => (value >> 4) * 10 + (value & 0x0F);

    private static byte Clamp(int value, int max) => (byte)Math.Clamp(value, 0, max);
}

public enum ScreenMode : byte
{
    Clock = 0,
    Slot1 = 1,
    Slot2 = 2,
}
