namespace Gmk87.Core.Model;

public enum ColorSupport
{
    /// <summary>Lighting off.</summary>
    None,

    /// <summary>Uses the stored RGB colour, or rainbow when the rainbow flag is set.</summary>
    Custom,

    /// <summary>Always rainbow; the firmware's hue key skips these (mask 0x41810 at 0x2000E4D4).</summary>
    BuiltInRainbow,

    /// <summary>Has its own 4-colour palette (table at file offset 0x1DC64).</summary>
    Palette,
}

public sealed record LightingEffect(int Id, string Name, ColorSupport Color);

public sealed record LogoColor(int Id, string Name);

public static class LightingCatalog
{
    /// <summary>Names follow the vendor VIA definition; colour support is from static analysis, not yet confirmed on the device.</summary>
    public static IReadOnlyList<LightingEffect> Effects { get; } =
    [
        new(0, "Off", ColorSupport.None),
        new(1, "Wave", ColorSupport.Custom),
        new(2, "Colour cloud", ColorSupport.Custom),
        new(3, "Vortex", ColorSupport.Custom),
        new(4, "Mix colour", ColorSupport.BuiltInRainbow),
        new(5, "Breathe", ColorSupport.Custom),
        new(6, "Light", ColorSupport.Custom),
        new(7, "Slowly off", ColorSupport.Custom),
        new(8, "Stone", ColorSupport.Custom),
        new(9, "Laser", ColorSupport.Custom),
        new(10, "Starry", ColorSupport.Custom),
        new(11, "Flowers open", ColorSupport.BuiltInRainbow),
        new(12, "Traverse", ColorSupport.BuiltInRainbow),
        new(13, "Wave bar", ColorSupport.Custom),
        new(14, "Meteor", ColorSupport.Custom),
        new(15, "Rain", ColorSupport.Custom),
        new(16, "Scan", ColorSupport.Custom),
        new(17, "Trigger colour", ColorSupport.Palette),
        new(18, "Center spread", ColorSupport.BuiltInRainbow),
    ];

    /// <summary>Logo LED colour presets, per gmk87-node.</summary>
    public static IReadOnlyList<LogoColor> LogoColors { get; } =
    [
        new(0, "Red"),
        new(1, "Orange"),
        new(2, "Yellow"),
        new(3, "Green"),
        new(4, "Teal"),
        new(5, "Blue"),
        new(6, "Purple"),
        new(7, "White"),
        new(8, "Off"),
    ];

    /// <summary>Logo LED modes, names per gmk87-node (unverified).</summary>
    public static IReadOnlyList<string> LogoModes { get; } =
    [
        "Blinking, one colour",
        "Pulse rainbow",
        "Blinking, one colour (alt)",
        "Fixed colour",
        "Fixed colour (alt)",
    ];
}
