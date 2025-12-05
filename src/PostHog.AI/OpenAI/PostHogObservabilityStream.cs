using System.Text;

namespace PostHog.AI.OpenAI;

internal sealed class PostHogObservabilityStream : Stream
{
    private readonly Stream _innerStream;
    private readonly MemoryStream _captureStream;
    private readonly Func<string, Task> _onComplete;
    private bool _isDisposed;
    private readonly int _maxCaptureSize;

    public PostHogObservabilityStream(
        Stream innerStream,
        Func<string, Task> onComplete,
        int maxCaptureSize
    )
    {
        _innerStream = innerStream ?? throw new ArgumentNullException(nameof(innerStream));
        _onComplete = onComplete ?? throw new ArgumentNullException(nameof(onComplete));
        _maxCaptureSize = maxCaptureSize;
        _captureStream = new MemoryStream();
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
            CaptureBytes(buffer, offset, bytesRead);
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
#if NETSTANDARD2_1
        var bytesRead = await _innerStream
            .ReadAsync(buffer, offset, count, cancellationToken)
            .ConfigureAwait(false);
        if (bytesRead > 0)
        {
            CaptureBytes(buffer, offset, bytesRead);
        }
        return bytesRead;
#else
        var bytesRead = await _innerStream
            .ReadAsync(buffer.AsMemory(offset, count), cancellationToken)
            .ConfigureAwait(false);
        if (bytesRead > 0)
        {
            CaptureBytes(buffer, offset, bytesRead);
        }
        return bytesRead;
#endif
    }

#if !NETSTANDARD2_1
    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default
    )
    {
        var bytesRead = await _innerStream
            .ReadAsync(buffer, cancellationToken)
            .ConfigureAwait(false);
        if (bytesRead > 0)
        {
            CaptureBytes(buffer.Span.Slice(0, bytesRead));
        }
        return bytesRead;
    }
#endif

    private void CaptureBytes(byte[] buffer, int offset, int count)
    {
        if (_captureStream.Length < _maxCaptureSize)
        {
            var remaining = _maxCaptureSize - (int)_captureStream.Length;
            var toWrite = Math.Min(remaining, count);
            _captureStream.Write(buffer, offset, toWrite);
        }
    }

#if !NETSTANDARD2_1
    private void CaptureBytes(ReadOnlySpan<byte> buffer)
    {
        if (_captureStream.Length < _maxCaptureSize)
        {
            var remaining = _maxCaptureSize - (int)_captureStream.Length;
            var toWrite = Math.Min(remaining, buffer.Length);
            _captureStream.Write(buffer.Slice(0, toWrite));
        }
    }
#endif

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (!_isDisposed)
        {
            if (disposing)
            {
                ProcessCapturedDataAsync().GetAwaiter().GetResult();
                _captureStream.Dispose();
                _innerStream.Dispose();
            }
            _isDisposed = true;
        }
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (!_isDisposed)
        {
            await ProcessCapturedDataAsync().ConfigureAwait(false);
            await _captureStream.DisposeAsync().ConfigureAwait(false);
            await _innerStream.DisposeAsync().ConfigureAwait(false);
            _isDisposed = true;
        }
        await base.DisposeAsync().ConfigureAwait(false);
    }

    private async Task ProcessCapturedDataAsync()
    {
        try
        {
            _captureStream.Position = 0;
            using var reader = new StreamReader(
                _captureStream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 1024,
                leaveOpen: true
            );
            var content = await reader.ReadToEndAsync().ConfigureAwait(false);
            await _onComplete(content).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Do not catch general exception types
        catch
        {
            // Ignore errors during processing to avoid affecting the original stream
        }
#pragma warning restore CA1031 // Do not catch general exception types
    }
}
