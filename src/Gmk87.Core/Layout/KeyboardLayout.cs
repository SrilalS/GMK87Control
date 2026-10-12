using System.Text.Json;

namespace Gmk87.Core.Layout;

/// <summary>One physical key, in KLE units (1 = one standard key width).</summary>
public sealed record LayoutKey(int Row, int Col, double X, double Y, double Width, double Height, string Color);

/// <summary>Physical layout parsed from the VIA definition's KLE "keymap" array.</summary>
public sealed class KeyboardLayout
{
    private KeyboardLayout(string name, int rows, int cols, IReadOnlyList<LayoutKey> keys)
    {
        Name = name;
        Rows = rows;
        Cols = cols;
        Keys = keys;
        Width = keys.Count == 0 ? 0 : keys.Max(k => k.X + k.Width);
        Height = keys.Count == 0 ? 0 : keys.Max(k => k.Y + k.Height);
    }

    public string Name { get; }
    public int Rows { get; }
    public int Cols { get; }
    public IReadOnlyList<LayoutKey> Keys { get; }
    public double Width { get; }
    public double Height { get; }

    /// <summary>The GMK87 USB definition embedded in this assembly.</summary>
    public static KeyboardLayout LoadEmbedded()
    {
        using var stream = typeof(KeyboardLayout).Assembly.GetManifestResourceStream("Gmk87.Core.Layout.gmk87-usb.json")
                           ?? throw new InvalidOperationException("Embedded layout is missing.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    public static KeyboardLayout Parse(string viaDefinitionJson)
    {
        using var doc = JsonDocument.Parse(viaDefinitionJson);
        var root = doc.RootElement;
        var matrix = root.GetProperty("matrix");
        var keys = ParseKle(root.GetProperty("layouts").GetProperty("keymap"));
        return new KeyboardLayout(root.GetProperty("name").GetString() ?? "", matrix.GetProperty("rows").GetInt32(),
            matrix.GetProperty("cols").GetInt32(), keys);
    }

    internal static List<LayoutKey> ParseKle(JsonElement keymap)
    {
        var keys = new List<LayoutKey>();
        double y = 0;
        var color = "#cccccc";

        foreach (var row in keymap.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Array)
                continue;

            double x = 0, w = 1, h = 1;
            foreach (var item in row.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    if (item.TryGetProperty("x", out var px)) x += px.GetDouble();
                    if (item.TryGetProperty("y", out var py)) y += py.GetDouble();
                    if (item.TryGetProperty("w", out var pw)) w = pw.GetDouble();
                    if (item.TryGetProperty("h", out var ph)) h = ph.GetDouble();
                    if (item.TryGetProperty("c", out var pc)) color = pc.GetString() ?? color;
                    continue;
                }

                // VIA labels are "row,col", optionally followed by layout-option lines.
                var label = item.GetString() ?? "";
                var matrixPart = label.Split('\n')[0].Split(',');
                if (matrixPart.Length == 2 && int.TryParse(matrixPart[0], out var r) && int.TryParse(matrixPart[1], out var c))
                    keys.Add(new LayoutKey(r, c, x, y, w, h, color));

                x += w;
                w = 1;
                h = 1;
            }

            y += 1;
        }

        return keys;
    }
}
