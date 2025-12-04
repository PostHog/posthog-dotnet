using System.Text;

namespace PostHog.AI.OpenAI;

public class PostHogObservabilityStream : Stream
{
    private readonly Stream _innerStream;
    private readonly MemoryStream _capturedContent = new();
    private readonly Func<string, Task> _onComplete;
    private bool _disposed;

    public PostHogObservabilityStream(Stream innerStream, Func<string, Task> onComplete)
    {
        _innerStream = innerStream;
        _onComplete = onComplete;
    }

    public override bool CanRead => _innerStream.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _innerStream.Length;
    public override long Position
    {
        get => _innerStream.Position;
        set => throw new NotSupportedException();
    }

    public override void Flush() => _innerStream.Flush();

    public override int Read(byte[] buffer, int offset, int count)
    {
        var bytesRead = _innerStream.Read(buffer, offset, count);
        if (bytesRead > 0)
        {
            _capturedContent.Write(buffer, offset, bytesRead);
        }
        return bytesRead;
    }

    public override async Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken
    )
    {
        // Wrapper to satisfy CA1835 (Prefer Memory overloads)
        var bytesRead = await _innerStream.ReadAsync(
            new Memory<byte>(buffer, offset, count),
            cancellationToken
        );
        if (bytesRead > 0)
        {
            await _capturedContent.WriteAsync(
                new ReadOnlyMemory<byte>(buffer, offset, bytesRead),
                cancellationToken
            );
        }
        return bytesRead;
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default
    )
    {
        var bytesRead = await _innerStream.ReadAsync(buffer, cancellationToken);
        if (bytesRead > 0)
        {
            await _capturedContent.WriteAsync(buffer.Slice(0, bytesRead), cancellationToken);
        }
        return bytesRead;
    }

    public override async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            try
            {
                var text = Encoding.UTF8.GetString(_capturedContent.ToArray());
                await _onComplete(text);
            }
#pragma warning disable CA1031
            catch
            {
                // Swallow exceptions during dispose to prevent crashing the app
            }
#pragma warning restore CA1031

            await _capturedContent.DisposeAsync();
            await _innerStream.DisposeAsync();
        }
        await base.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            try
            {
                var text = Encoding.UTF8.GetString(_capturedContent.ToArray());
                // Fire and forget the processing task
                Task.Run(() => _onComplete(text));
            }
#pragma warning disable CA1031
            catch
            {
                // Swallow exceptions during dispose to prevent crashing the app
            }
#pragma warning restore CA1031

            _capturedContent.Dispose();
            _innerStream.Dispose();
        }
        base.Dispose(disposing);
    }
}
