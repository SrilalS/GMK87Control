using System.Diagnostics;
using Gmk87.Core.Layout;
using Gmk87.Core.Lighting;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Gmk87.App.Controls;

/// <summary>Animated, simulated backlight on the keyboard's real layout. Click keys to trigger reactive effects.</summary>
public sealed partial class LightingPreview : UserControl
{
    private const double KeyUnit = 40;
    private const double KeyGap = 3;
    private const double FrameInterval = 1.0 / 30;
    private static readonly Color Keycap = Color.FromArgb(255, 30, 30, 34);

    private readonly EffectSimulator _simulator;
    private readonly SolidColorBrush[] _brushes;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _lastFrame = -1;
    private bool _animating;

    public LightingPreview()
    {
        var keys = KeyboardLayout.LoadEmbedded().Keys;
        _simulator = new EffectSimulator(keys);
        _brushes = new SolidColorBrush[keys.Count];

        var canvas = new Canvas
        {
            Width = keys.Max(k => k.X + k.Width) * KeyUnit + KeyUnit / 2,
            Height = keys.Max(k => k.Y + k.Height) * KeyUnit + KeyUnit / 2,
        };

        for (var i = 0; i < keys.Count; i++)
        {
            var key = keys[i];
            var index = i;
            _brushes[i] = new SolidColorBrush(Keycap);
            var cap = new Border
            {
                Width = key.Width * KeyUnit - KeyGap,
                Height = key.Height * KeyUnit - KeyGap,
                CornerRadius = new CornerRadius(4),
                Background = _brushes[i],
                BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
                BorderThickness = new Thickness(1),
            };
            cap.PointerPressed += (_, _) => _simulator.Press(index, _clock.Elapsed.TotalSeconds);
            Canvas.SetLeft(cap, key.X * KeyUnit + KeyUnit / 4);
            Canvas.SetTop(cap, key.Y * KeyUnit + KeyUnit / 4);
            canvas.Children.Add(cap);
        }

        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 12, 12, 14)),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(6),
            Child = new Viewbox { Child = canvas, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly },
        };

        Loaded += (_, _) => Start();
        Unloaded += (_, _) => Stop();
    }

    public EffectParams Params { get; set; } = new(6, 9, 2, false, false, new Rgb(255, 0, 0));

    public bool DemoPresses
    {
        get => _simulator.DemoPresses;
        set => _simulator.DemoPresses = value;
    }

    private void Start()
    {
        if (_animating)
            return;
        CompositionTarget.Rendering += OnRendering;
        _animating = true;
    }

    private void Stop()
    {
        if (!_animating)
            return;
        CompositionTarget.Rendering -= OnRendering;
        _animating = false;
    }

    private void OnRendering(object? sender, object e)
    {
        var now = _clock.Elapsed.TotalSeconds;
        if (now - _lastFrame < FrameInterval)
            return;
        _lastFrame = now;

        var colors = _simulator.Render(Params, now);
        for (var i = 0; i < colors.Length; i++)
        {
            var c = colors[i];
            // Light adds onto a dark keycap, like a backlit key.
            _brushes[i].Color = Color.FromArgb(255,
                (byte)Math.Min(255, Keycap.R + c.R),
                (byte)Math.Min(255, Keycap.G + c.G),
                (byte)Math.Min(255, Keycap.B + c.B));
        }
    }
}
