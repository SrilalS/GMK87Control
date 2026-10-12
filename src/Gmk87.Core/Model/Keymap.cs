using System.Text.Json;

namespace Gmk87.Core.Model;

/// <summary>A dynamic keymap in VIA buffer layout: [layer][row][col], 16-bit big-endian keycodes.</summary>
public sealed class Keymap
{
    private readonly ushort[] _codes;

    public Keymap(int layers, int rows, int cols)
    {
        Layers = layers;
        Rows = rows;
        Cols = cols;
        _codes = new ushort[layers * rows * cols];
    }

    public int Layers { get; }
    public int Rows { get; }
    public int Cols { get; }

    /// <summary>VIA protocol the codes were read under (9 = legacy numbering, 11 = modern). Null for old backups.</summary>
    public int? ViaProtocol { get; set; }

    public ushort this[int layer, int row, int col]
    {
        get => _codes[Index(layer, row, col)];
        set => _codes[Index(layer, row, col)] = value;
    }

    public static Keymap FromBuffer(ReadOnlySpan<byte> buffer, int layers, int rows, int cols)
    {
        var map = new Keymap(layers, rows, cols);
        if (buffer.Length != map._codes.Length * 2)
            throw new ArgumentException($"Expected {map._codes.Length * 2} bytes, got {buffer.Length}.", nameof(buffer));
        for (var i = 0; i < map._codes.Length; i++)
            map._codes[i] = (ushort)((buffer[i * 2] << 8) | buffer[i * 2 + 1]);
        return map;
    }

    public byte[] ToBuffer()
    {
        var buffer = new byte[_codes.Length * 2];
        for (var i = 0; i < _codes.Length; i++)
        {
            buffer[i * 2] = (byte)(_codes[i] >> 8);
            buffer[i * 2 + 1] = (byte)_codes[i];
        }
        return buffer;
    }

    public Keymap Clone()
    {
        var copy = new Keymap(Layers, Rows, Cols) { ViaProtocol = ViaProtocol };
        _codes.CopyTo(copy._codes, 0);
        return copy;
    }

    /// <summary>Positions where <paramref name="other"/> differs from this map, as (layer, row, col, otherCode).</summary>
    public IEnumerable<(int Layer, int Row, int Col, ushort Code)> DiffTo(Keymap other)
    {
        if (other.Layers != Layers || other.Rows != Rows || other.Cols != Cols)
            throw new ArgumentException("Keymaps have different dimensions.", nameof(other));
        for (var l = 0; l < Layers; l++)
            for (var r = 0; r < Rows; r++)
                for (var c = 0; c < Cols; c++)
                    if (this[l, r, c] != other[l, r, c])
                        yield return (l, r, c, other[l, r, c]);
    }

    public string ToJson() => JsonSerializer.Serialize(new KeymapFile(Layers, Rows, Cols, Convert.ToHexString(ToBuffer()), ViaProtocol),
        new JsonSerializerOptions { WriteIndented = true });

    public static Keymap FromJson(string json)
    {
        var file = JsonSerializer.Deserialize<KeymapFile>(json) ?? throw new FormatException("Empty keymap file.");
        var map = FromBuffer(Convert.FromHexString(file.Hex), file.Layers, file.Rows, file.Cols);
        map.ViaProtocol = file.ViaProtocol;
        return map;
    }

    private int Index(int layer, int row, int col)
    {
        if ((uint)layer >= Layers || (uint)row >= Rows || (uint)col >= Cols)
            throw new ArgumentOutOfRangeException(nameof(layer), $"({layer},{row},{col}) is outside {Layers}x{Rows}x{Cols}.");
        return (layer * Rows + row) * Cols + col;
    }

    private sealed record KeymapFile(int Layers, int Rows, int Cols, string Hex, int? ViaProtocol = null);
}
