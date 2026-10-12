namespace Gmk87.Core.Imaging;

/// <summary>Pixel work for the 240x135 screen. Inputs are 8-bit BGRA (premultiplied or straight both work against black).</summary>
public static class DisplayImage
{
    public const int Width = 240;
    public const int Height = 135;

    /// <summary>Each frame is RGB565 big-endian, padded to the next 32 KB boundary (64,800 -> 65,536 bytes).</summary>
    public const int FrameBytes = ((Width * Height * 2) + 0x7FFF) & ~0x7FFF;

    /// <summary>Total frames across both slots. gmk87-node enforces 36; the Python reference allows 90. We keep the safer limit.</summary>
    public const int MaxTotalFrames = 36;

    public static ushort ToRgb565(byte r, byte g, byte b) => (ushort)(((r >> 3) << 11) | ((g >> 2) << 5) | (b >> 3));

    /// <summary>Encodes one 240x135 BGRA frame into the keyboard's frame format, compositing alpha over black.</summary>
    public static byte[] EncodeFrame(ReadOnlySpan<byte> bgra)
    {
        if (bgra.Length != Width * Height * 4)
            throw new ArgumentException($"Expected {Width}x{Height} BGRA ({Width * Height * 4} bytes).", nameof(bgra));

        var frame = new byte[FrameBytes];
        for (var i = 0; i < Width * Height; i++)
        {
            var a = bgra[i * 4 + 3];
            var b = (byte)(bgra[i * 4] * a / 255);
            var g = (byte)(bgra[i * 4 + 1] * a / 255);
            var r = (byte)(bgra[i * 4 + 2] * a / 255);
            var value = ToRgb565(r, g, b);
            frame[i * 2] = (byte)(value >> 8);
            frame[i * 2 + 1] = (byte)value;
        }
        return frame;
    }

    /// <summary>A blank (black) frame, used to fill a slot that has no image.</summary>
    public static byte[] BlankFrame() => new byte[FrameBytes];

    /// <summary>Scales a BGRA image to cover 240x135 (keeping aspect ratio) and centre-crops it. Bilinear.</summary>
    public static byte[] ResizeCover(ReadOnlySpan<byte> src, int srcWidth, int srcHeight)
    {
        if (src.Length != srcWidth * srcHeight * 4)
            throw new ArgumentException("Source size does not match its dimensions.", nameof(src));

        var scale = Math.Max((double)Width / srcWidth, (double)Height / srcHeight);
        var offsetX = (srcWidth * scale - Width) / 2;
        var offsetY = (srcHeight * scale - Height) / 2;
        var dst = new byte[Width * Height * 4];

        for (var y = 0; y < Height; y++)
        {
            var sy = Math.Clamp((y + offsetY + 0.5) / scale - 0.5, 0, srcHeight - 1);
            var y0 = (int)sy;
            var y1 = Math.Min(y0 + 1, srcHeight - 1);
            var fy = sy - y0;

            for (var x = 0; x < Width; x++)
            {
                var sx = Math.Clamp((x + offsetX + 0.5) / scale - 0.5, 0, srcWidth - 1);
                var x0 = (int)sx;
                var x1 = Math.Min(x0 + 1, srcWidth - 1);
                var fx = sx - x0;

                for (var ch = 0; ch < 4; ch++)
                {
                    var top = src[(y0 * srcWidth + x0) * 4 + ch] * (1 - fx) + src[(y0 * srcWidth + x1) * 4 + ch] * fx;
                    var bottom = src[(y1 * srcWidth + x0) * 4 + ch] * (1 - fx) + src[(y1 * srcWidth + x1) * 4 + ch] * fx;
                    dst[(y * Width + x) * 4 + ch] = (byte)Math.Round(top * (1 - fy) + bottom * fy);
                }
            }
        }

        return dst;
    }

    /// <summary>Builds the full upload buffer: slot-1 frames then slot-2 frames. An empty slot gets one black frame.</summary>
    public static byte[] BuildUpload(IReadOnlyList<byte[]> slot1Frames, IReadOnlyList<byte[]> slot2Frames, out int slot1Count, out int slot2Count)
    {
        var s1 = slot1Frames.Count > 0 ? slot1Frames : [BlankFrame()];
        var s2 = slot2Frames.Count > 0 ? slot2Frames : [BlankFrame()];
        if (s1.Count + s2.Count > MaxTotalFrames)
            throw new ArgumentException($"{s1.Count + s2.Count} frames in total; the limit is {MaxTotalFrames}.");

        slot1Count = s1.Count;
        slot2Count = s2.Count;
        var data = new byte[(s1.Count + s2.Count) * FrameBytes];
        var offset = 0;
        foreach (var frame in s1.Concat(s2))
        {
            if (frame.Length != FrameBytes)
                throw new ArgumentException("Frames must come from EncodeFrame.");
            frame.CopyTo(data, offset);
            offset += FrameBytes;
        }
        return data;
    }
}
