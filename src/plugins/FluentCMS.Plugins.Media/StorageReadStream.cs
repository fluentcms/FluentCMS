namespace FluentCMS.Plugins.Media;

// A file of a storage that is read over the network, for the storage packages: its length and version are known when it
// opens, and its body is fetched at the first read from where the stream stands, and again after a seek. Reads in order share
// one body, so a whole read is one request, a range one request from its start, and an open that is never read, as for HEAD,
// none. The body is of the version opened, so a file replaced while it is read fails the read rather than mixing two files.
public abstract class StorageReadStream(long length) : Stream
{
    private Stream? _body;
    private long _bodyPosition;
    private long _position;
    private bool _disposed;

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => false;

    public override long Length { get; } = length;

    public override long Position
    {
        get => _position;
        set => Seek(value, SeekOrigin.Begin);
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        var position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => Length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };
        ArgumentOutOfRangeException.ThrowIfNegative(position, nameof(offset));
        return _position = position;
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    // Synchronous reads are for callers that chose them; the plugin reads asynchronously, and the image processor reads such
    // a stream into memory first, so blocking on the request here does not block a request of the plugin's.
    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (buffer.IsEmpty || _position >= Length)
        {
            return 0;
        }

        if (_body is null || _bodyPosition != _position)
        {
            ReopenAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        return Advance(_body!.Read(buffer));
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (buffer.IsEmpty || _position >= Length)
        {
            return 0;
        }

        if (_body is null || _bodyPosition != _position)
        {
            await ReopenAsync(cancellationToken);
        }

        return Advance(await _body!.ReadAsync(buffer, cancellationToken));
    }

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    // The body of the version opened, from the position to the end. The position is below the length.
    protected abstract Task<Stream> OpenBodyAsync(long position, CancellationToken cancellationToken);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _body?.Dispose();
            _body = null;
            _disposed = true;
        }

        base.Dispose(disposing);
    }

    private async Task ReopenAsync(CancellationToken cancellationToken)
    {
        if (_body is not null)
        {
            await _body.DisposeAsync();
            _body = null;
        }

        _body = await OpenBodyAsync(_position, cancellationToken);
        _bodyPosition = _position;
    }

    private int Advance(int read)
    {
        _position += read;
        _bodyPosition += read;
        return read;
    }
}
