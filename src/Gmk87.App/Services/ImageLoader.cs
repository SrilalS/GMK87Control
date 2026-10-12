using Gmk87.Core.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Gmk87.App.Services;

/// <summary>An image or animation already scaled to the screen: opaque 240x135 BGRA frames.</summary>
public sealed record ScreenAnimation(string FileName, IReadOnlyList<byte[]> Frames, int SuggestedDelayMs);

public static class ImageLoader
{
    /// <summary>
    /// Decodes PNG/JPEG/BMP/GIF with the Windows codecs. GIF frames are composited (position offsets and
    /// "restore to background" disposal), then each frame is cover-scaled to 240x135 over black.
    /// </summary>
    public static async Task<ScreenAnimation> LoadAsync(string path, int maxFrames)
    {
        var file = await StorageFile.GetFileFromPathAsync(path);
        using IRandomAccessStream stream = await file.OpenReadAsync();
        var decoder = await BitmapDecoder.CreateAsync(stream);

        var isGif = decoder.DecoderInformation.CodecId == BitmapDecoder.GifDecoderId;
        var (canvasWidth, canvasHeight) = isGif ? await GetGifScreenSizeAsync(decoder) : ((int)decoder.PixelWidth, (int)decoder.PixelHeight);
        var canvas = new byte[canvasWidth * canvasHeight * 4];
        var frames = new List<byte[]>();
        var delayMs = 100;

        var count = (int)Math.Min(decoder.FrameCount, (uint)maxFrames);
        for (uint i = 0; i < count; i++)
        {
            var frame = await decoder.GetFrameAsync(i);
            var pixels = await frame.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, new BitmapTransform(),
                ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb);
            var data = pixels.DetachPixelData();
            var frameWidth = (int)frame.OrientedPixelWidth;
            var frameHeight = (int)frame.OrientedPixelHeight;

            var (left, top, disposal, delay) = isGif ? await GetGifFrameInfoAsync(frame) : (0, 0, 0, 0);
            if (i == 0 && delay > 0)
                delayMs = delay;

            DrawOver(canvas, canvasWidth, canvasHeight, data, frameWidth, frameHeight, left, top);

            // Premultiplied colour over black is the colour itself; mark it opaque.
            var opaque = (byte[])canvas.Clone();
            for (var p = 3; p < opaque.Length; p += 4)
                opaque[p] = 255;
            frames.Add(DisplayImage.ResizeCover(opaque, canvasWidth, canvasHeight));

            if (disposal == 2)
                ClearRect(canvas, canvasWidth, canvasHeight, left, top, frameWidth, frameHeight);
        }

        return new ScreenAnimation(Path.GetFileName(path), frames, delayMs);
    }

    private static void DrawOver(byte[] canvas, int cw, int ch, byte[] src, int sw, int sh, int left, int top)
    {
        for (var y = 0; y < sh; y++)
        {
            var cy = y + top;
            if (cy < 0 || cy >= ch)
                continue;
            for (var x = 0; x < sw; x++)
            {
                var cx = x + left;
                if (cx < 0 || cx >= cw)
                    continue;
                var s = (y * sw + x) * 4;
                var d = (cy * cw + cx) * 4;
                var a = src[s + 3];
                if (a == 255)
                {
                    System.Buffer.BlockCopy(src, s, canvas, d, 4);
                }
                else if (a > 0)
                {
                    for (var c = 0; c < 4; c++)
                        canvas[d + c] = (byte)(src[s + c] + canvas[d + c] * (255 - a) / 255);
                }
            }
        }
    }

    private static void ClearRect(byte[] canvas, int cw, int ch, int left, int top, int w, int h)
    {
        for (var y = Math.Max(0, top); y < Math.Min(ch, top + h); y++)
            Array.Clear(canvas, (y * cw + Math.Max(0, left)) * 4, (Math.Min(cw, left + w) - Math.Max(0, left)) * 4);
    }

    private static async Task<(int Width, int Height)> GetGifScreenSizeAsync(BitmapDecoder decoder)
    {
        try
        {
            var props = await decoder.BitmapContainerProperties.GetPropertiesAsync(["/logscrdesc/Width", "/logscrdesc/Height"]);
            return (Convert.ToInt32(props["/logscrdesc/Width"].Value), Convert.ToInt32(props["/logscrdesc/Height"].Value));
        }
        catch
        {
            return ((int)decoder.PixelWidth, (int)decoder.PixelHeight);
        }
    }

    private static async Task<(int Left, int Top, int Disposal, int DelayMs)> GetGifFrameInfoAsync(BitmapFrame frame)
    {
        int Read(BitmapPropertySet set, string key) => set.TryGetValue(key, out var v) ? Convert.ToInt32(v.Value) : 0;

        try
        {
            var props = await frame.BitmapProperties.GetPropertiesAsync(["/imgdesc/Left", "/imgdesc/Top"]);
            int disposal = 0, delay = 0;
            try
            {
                var ctl = await frame.BitmapProperties.GetPropertiesAsync(["/grctlext/Disposal", "/grctlext/Delay"]);
                disposal = Read(ctl, "/grctlext/Disposal");
                delay = Read(ctl, "/grctlext/Delay") * 10;
            }
            catch
            {
                // Frames without a graphic control extension.
            }
            return (Read(props, "/imgdesc/Left"), Read(props, "/imgdesc/Top"), disposal, delay);
        }
        catch
        {
            return (0, 0, 0, 0);
        }
    }
}
