using System.Globalization;
using Gmk87.App.Services;
using Gmk87.Core.Layout;
using Gmk87.Core.Model;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace Gmk87.App.Pages;

public sealed partial class KeymapPage : Page
{
    private const double KeyUnit = 58;
    private const double KeyGap = 4;

    private readonly KeyboardLayout _layout = KeyboardLayout.LoadEmbedded();
    private readonly Dictionary<(int Row, int Col), Button> _buttons = [];
    private Keymap? _keymap;
    private int _layerCount = 4;
    private LayoutKey? _selected;
    private ushort? _pending;
    private KeycodeSet _keys = KeycodeSet.Legacy;
    private int _protocol;

    public KeymapPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        BuildCanvas();
    }

    private int Layer => Math.Max(0, LayerPicker.SelectedIndex);

    /// <summary>Loads the keymap the first time the page is shown (reads only).</summary>
    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        if (_keymap is null && !Busy.IsActive)
            await RunAsync(ReadKeymapAsync);
    }

    private void BuildCanvas()
    {
        KeyCanvas.Width = _layout.Width * KeyUnit;
        KeyCanvas.Height = _layout.Height * KeyUnit;

        foreach (var key in _layout.Keys)
        {
            var button = new Button
            {
                Width = key.Width * KeyUnit - KeyGap,
                Height = key.Height * KeyUnit - KeyGap,
                Padding = new Thickness(2),
                Tag = key,
                IsEnabled = false,
                Content = new TextBlock
                {
                    Text = $"{key.Row},{key.Col}",
                    FontSize = 12,
                    TextWrapping = TextWrapping.WrapWholeWords,
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            button.Click += OnKeyClick;
            Canvas.SetLeft(button, key.X * KeyUnit);
            Canvas.SetTop(button, key.Y * KeyUnit);
            KeyCanvas.Children.Add(button);
            _buttons[(key.Row, key.Col)] = button;
        }
    }

    private void RefreshLabels()
    {
        foreach (var (pos, button) in _buttons)
        {
            var label = (TextBlock)button.Content;
            var key = (LayoutKey)button.Tag;
            button.IsEnabled = _keymap is not null;
            button.Style = key == _selected ? (Style)Application.Current.Resources["AccentButtonStyle"] : null;
            if (_keymap is null)
                continue;

            var code = _keymap[Layer, pos.Row, pos.Col];
            label.Text = _keys.GetLabel(code);
            label.Opacity = code is KeycodeSet.Transparent or KeycodeSet.VendorBlocked or KeycodeSet.None ? 0.45 : 1;
            ToolTipService.SetToolTip(button, $"Row {pos.Row}, col {pos.Col}: 0x{code:X4}");
        }

        UpdateEditor();
    }

    private async void OnReadClick(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        await ReadKeymapAsync();
        Show("Keymap read from the keyboard.", InfoBarSeverity.Success);
    });

    private async Task ReadKeymapAsync()
    {
        var (protocol, layers, map) = await App.Keyboard.WithViaAsync(async via =>
        {
            var p = await via.GetProtocolVersionAsync();
            var count = await via.GetLayerCountAsync();
            return (p, count, await via.ReadKeymapAsync(count, _layout.Rows, _layout.Cols));
        });
        _protocol = protocol;
        _keys = KeycodeSet.ForProtocol(protocol);
        map.ViaProtocol = protocol;
        SchemeInfo.Text = $"VIA protocol {protocol}: {_keys.Name} keycodes";
        _layerCount = layers;
        _keymap = map;
        SaveBackupButton.IsEnabled = RestoreButton.IsEnabled = true;
        RefreshLabels();
    }

    private void OnLayerChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_buttons.Count > 0)
            RefreshLabels();
    }

    private void OnKeyClick(object sender, RoutedEventArgs e)
    {
        _selected = (LayoutKey)((Button)sender).Tag;
        _pending = null;
        KeySearch.Text = "";
        HexBox.Text = "";
        RefreshLabels();
    }

    private void UpdateEditor()
    {
        var hasKey = _keymap is not null && _selected is not null;
        KeySearch.IsEnabled = HexBox.IsEnabled = hasKey;
        WriteKeyButton.IsEnabled = hasKey && _pending is not null;

        if (!hasKey)
        {
            PendingInfo.Text = "";
            return;
        }

        var current = _keymap![Layer, _selected!.Row, _selected.Col];
        SelectedInfo.Text = $"Layer {Layer}, row {_selected.Row}, col {_selected.Col}\nCurrent: {_keys.GetLabel(current)} (0x{current:X4})";
        PendingInfo.Text = _pending is { } p ? $"New: {_keys.GetLabel(p)} (0x{p:X4})" : "";
    }

    private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
            return;
        var text = sender.Text.Trim();
        sender.ItemsSource = text.Length == 0
            ? null
            : _keys.PickerEntries
                .Where(k => k.Label.Contains(text, StringComparison.OrdinalIgnoreCase) || k.Group.Contains(text, StringComparison.OrdinalIgnoreCase))
                .OrderBy(k => !k.Label.Equals(text, StringComparison.OrdinalIgnoreCase))
                .ThenBy(k => !k.Label.StartsWith(text, StringComparison.OrdinalIgnoreCase))
                .Take(40)
                .ToList();
    }

    private void OnSuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is KeycodeEntry entry)
        {
            _pending = entry.Code;
            HexBox.Text = $"0x{entry.Code:X4}";
            UpdateEditor();
        }
    }

    private void OnHexChanged(object sender, TextChangedEventArgs e)
    {
        var text = HexBox.Text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            text = text[2..];
        _pending = ushort.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code) && text.Length > 0 ? code : null;
        UpdateEditor();
    }

    private async void OnWriteKeyClick(object sender, RoutedEventArgs e)
    {
        if (_keymap is null || _selected is not { } key || _pending is not { } code)
            return;

        var layer = Layer;
        var old = _keymap[layer, key.Row, key.Col];
        if (!await Ui.ConfirmWriteAsync(XamlRoot, "Write one key?",
                $"Layer {layer}, row {key.Row}, col {key.Col}\n{_keys.GetLabel(old)} (0x{old:X4})  ->  {_keys.GetLabel(code)} (0x{code:X4})\n\nThe keyboard saves this immediately."))
            return;

        await RunAsync(async () =>
        {
            await App.Keyboard.WithViaAsync(async via =>
            {
                await via.SetKeycodeAsync(layer, key.Row, key.Col, code);
                return true;
            });
            await ReadKeymapAsync();
            var now = _keymap![layer, key.Row, key.Col];
            if (now == code)
                Show($"Written and verified: layer {layer}, {key.Row},{key.Col} is now {_keys.GetLabel(code)}.", InfoBarSeverity.Success);
            else
                Show($"Write sent, but read-back shows 0x{now:X4} instead of 0x{code:X4}.", InfoBarSeverity.Warning);
            _pending = null;
            UpdateEditor();
        });
    }

    private async void OnSaveBackupClick(object sender, RoutedEventArgs e)
    {
        if (_keymap is null)
            return;
        var path = await Ui.PickSaveFileAsync($"gmk87-keymap-{DateTime.Now:yyyyMMdd-HHmm}", "Keymap backup", ".json");
        if (path is null)
            return;
        await File.WriteAllTextAsync(path, _keymap.ToJson());
        Show($"Backup saved to {path}", InfoBarSeverity.Success);
    }

    private async void OnRestoreClick(object sender, RoutedEventArgs e)
    {
        if (_keymap is null)
            return;
        var path = await Ui.PickOpenFileAsync(".json");
        if (path is null)
            return;

        await RunAsync(async () =>
        {
            var backup = Keymap.FromJson(await File.ReadAllTextAsync(path));
            if (backup.Layers != _layerCount || backup.Rows != _layout.Rows || backup.Cols != _layout.Cols)
                throw new InvalidOperationException($"Backup is {backup.Layers}x{backup.Rows}x{backup.Cols}; this keyboard is {_layerCount}x{_layout.Rows}x{_layout.Cols}.");

            if (backup.ViaProtocol is { } bp && KeycodeSet.ForProtocol(bp) != _keys)
                throw new InvalidOperationException($"This backup was taken under VIA protocol {bp} ({KeycodeSet.ForProtocol(bp).Name}); the keyboard now uses protocol {_protocol}. Restoring it would write wrongly numbered keycodes.");

            var changes = _keymap.DiffTo(backup).ToList();
            if (changes.Count == 0)
            {
                Show("The keyboard already matches this backup.", InfoBarSeverity.Informational);
                return;
            }

            var unknownScheme = backup.ViaProtocol is null
                ? "\n\nThis backup doesn't record which keycode numbering it uses. Only restore it if it was taken on the current firmware."
                : "";
            if (!await Ui.ConfirmWriteAsync(XamlRoot, "Restore backup?", $"{changes.Count} key(s) differ from the backup and will be written.{unknownScheme}"))
                return;

            await App.Keyboard.WithViaAsync(async via =>
            {
                foreach (var (layer, row, col, code) in changes)
                    await via.SetKeycodeAsync(layer, row, col, code);
                return true;
            });
            await ReadKeymapAsync();
            var remaining = _keymap.DiffTo(backup).Count();
            Show(remaining == 0 ? $"Restored {changes.Count} key(s) and verified." : $"{remaining} key(s) still differ after restore.",
                remaining == 0 ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
        });
    }

    private async Task RunAsync(Func<Task> action)
    {
        Busy.IsActive = true;
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Show(ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            Busy.IsActive = false;
        }
    }

    private static void Show(string text, InfoBarSeverity severity) => Toast.Show(text, severity);
}
