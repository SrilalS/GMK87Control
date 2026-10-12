using Gmk87.Core.Layout;
using Gmk87.Core.Lighting;

namespace Gmk87.Core.Tests;

public class EffectSimulatorTests
{
    private static readonly Rgb Red = new(255, 0, 0);
    private static EffectSimulator Sim() => new(KeyboardLayout.LoadEmbedded().Keys);
    private static EffectParams P(int effect, int brightness = 9, bool rainbow = false) => new(effect, brightness, 2, false, rainbow, Red);

    [Fact]
    public void Off_and_zero_brightness_are_black()
    {
        var sim = Sim();
        Assert.All(sim.Render(P(0), 1.0), c => Assert.Equal(Rgb.Black, c));
        Assert.All(sim.Render(P(6, brightness: 0), 1.0), c => Assert.Equal(Rgb.Black, c));
    }

    [Fact]
    public void Light_is_the_chosen_colour_scaled_by_brightness()
    {
        var sim = Sim();
        Assert.All(sim.Render(P(6), 0), c => Assert.Equal(Red, c));
        Assert.All(sim.Render(P(6, brightness: 3), 0), c => Assert.Equal(new Rgb(85, 0, 0), c));
    }

    [Fact]
    public void Rainbow_flag_spreads_hues_on_custom_effects()
    {
        var colours = Sim().Render(P(6, rainbow: true), 0).Distinct().Count();
        Assert.True(colours > 10, $"only {colours} distinct colours");
    }

    [Theory]
    [InlineData(4)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(18)]
    public void Built_in_rainbow_effects_ignore_the_custom_colour(int effect)
    {
        var frames = Enumerable.Range(0, 20).SelectMany(i => Sim().Render(P(effect), i * 0.37));
        Assert.Contains(frames, c => c.G > 0 || c.B > 0);
    }

    [Fact]
    public void Every_effect_renders_one_colour_per_key_and_animates()
    {
        for (var effect = 1; effect <= 18; effect++)
        {
            var sim = Sim();
            var a = sim.Render(P(effect), 0.5);
            var b = sim.Render(P(effect), 2.3);
            Assert.Equal(88, a.Length);
            if (effect != 6)
                Assert.False(a.SequenceEqual(b), $"effect {effect} is static");
        }
    }

    [Fact]
    public void Clicking_a_key_lights_it_in_reactive_mode()
    {
        var sim = Sim();
        sim.DemoPresses = false;
        Assert.All(sim.Render(P(7), 1.0), c => Assert.Equal(Rgb.Black, c));

        sim.Press(10, 1.0);
        var colours = sim.Render(P(7), 1.05);
        Assert.True(colours[10].R > 200);
        Assert.Equal(Rgb.Black, colours[11]);
    }

    [Fact]
    public void Trigger_colour_uses_the_firmware_palette()
    {
        var sim = Sim();
        sim.DemoPresses = false;
        sim.Press(0, 0);
        sim.Press(1, 0);
        var colours = sim.Render(P(17), 0);
        Assert.Equal(EffectSimulator.TriggerPalette[0], colours[0]);
        Assert.Equal(EffectSimulator.TriggerPalette[1], colours[1]);
    }

    [Fact]
    public void Traverse_sweeps_top_to_bottom()
    {
        var keys = KeyboardLayout.LoadEmbedded().Keys;
        var colours = Sim().Render(P(12), 1.0);
        int Index(int row, int col) => keys.ToList().FindIndex(k => k.Row == row && k.Col == col);

        // Same row, far apart horizontally: same colour. Different rows: different colour.
        Assert.Equal(colours[Index(2, 1)], colours[Index(2, 12)]);
        Assert.NotEqual(colours[Index(1, 1)], colours[Index(4, 2)]);
    }

    [Fact]
    public void Same_seed_gives_same_frames()
    {
        Assert.Equal(Sim().Render(P(10), 3.1), Sim().Render(P(10), 3.1));
    }
}
