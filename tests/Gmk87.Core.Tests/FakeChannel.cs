using Gmk87.Core.Hid;

namespace Gmk87.Core.Tests;

/// <summary>Records writes and answers each one through a responder function.</summary>
internal sealed class FakeChannel(int inputLength, int outputLength, Func<byte[], byte[]?> responder) : IHidChannel
{
    private readonly Queue<byte[]> _pending = new();

    public List<byte[]> Writes { get; } = [];
    public int InputReportLength => inputLength;
    public int OutputReportLength => outputLength;

    public Task WriteAsync(byte[] report, CancellationToken cancellationToken = default)
    {
        Assert.Equal(outputLength, report.Length);
        Writes.Add(report);
        if (responder(report) is { } reply)
            _pending.Enqueue(reply);
        return Task.CompletedTask;
    }

    public Task<byte[]?> ReadAsync(TimeSpan timeout, CancellationToken cancellationToken = default) =>
        Task.FromResult(_pending.Count > 0 ? _pending.Dequeue() : null);

    public void Dispose()
    {
    }
}
