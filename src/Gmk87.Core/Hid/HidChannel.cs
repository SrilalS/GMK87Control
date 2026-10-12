using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Gmk87.Core.Hid;

/// <summary>A report-level pipe to one HID collection. Abstracted so protocol code can be tested without hardware.</summary>
public interface IHidChannel : IDisposable
{
    int InputReportLength { get; }
    int OutputReportLength { get; }

    /// <summary>Writes one output report. <paramref name="report"/> includes the report ID at index 0.</summary>
    Task WriteAsync(byte[] report, CancellationToken cancellationToken = default);

    /// <summary>Reads one input report (report ID at index 0), or returns null on timeout.</summary>
    Task<byte[]?> ReadAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
}

public sealed class HidChannel : IHidChannel
{
    private readonly FileStream _stream;

    private HidChannel(FileStream stream, int inputLength, int outputLength)
    {
        _stream = stream;
        InputReportLength = inputLength;
        OutputReportLength = outputLength;
    }

    public int InputReportLength { get; }
    public int OutputReportLength { get; }

    public static HidChannel Open(HidInterfaceInfo info)
    {
        var handle = NativeMethods.CreateFileW(info.Path,
            NativeMethods.GENERIC_READ | NativeMethods.GENERIC_WRITE,
            NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE, IntPtr.Zero,
            NativeMethods.OPEN_EXISTING, NativeMethods.FILE_FLAG_OVERLAPPED, IntPtr.Zero);
        if (handle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not open HID interface {info.InterfaceTag}.");

        var stream = new FileStream(handle, FileAccess.ReadWrite, bufferSize: 0, isAsync: true);
        return new HidChannel(stream, info.InputReportLength, info.OutputReportLength);
    }

    public async Task WriteAsync(byte[] report, CancellationToken cancellationToken = default)
    {
        if (report.Length != OutputReportLength)
            throw new ArgumentException($"Report must be exactly {OutputReportLength} bytes, got {report.Length}.", nameof(report));
        await _stream.WriteAsync(report, cancellationToken).ConfigureAwait(false);
    }

    public async Task<byte[]?> ReadAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var buffer = new byte[InputReportLength];
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        try
        {
            var read = await _stream.ReadAsync(buffer, cts.Token).ConfigureAwait(false);
            return read > 0 ? buffer : null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    public void Dispose() => _stream.Dispose();
}

public static class HidChannelExtensions
{
    /// <summary>Discards input reports already queued by the driver (stale replies from another app or an earlier session).</summary>
    public static async Task DrainAsync(this IHidChannel channel, CancellationToken cancellationToken = default)
    {
        for (var i = 0; i < 64; i++)
        {
            if (await channel.ReadAsync(TimeSpan.FromMilliseconds(25), cancellationToken).ConfigureAwait(false) is null)
                return;
        }
    }
}
