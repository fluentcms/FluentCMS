namespace FluentCMS.Infrastructure.Media;

// Gives back bytes already read from a stream, then the rest of it, so a caller can look at a file's start before passing it on.
internal sealed class PrefixedStream(ReadOnlyMemory<byte> prefix, Stream inner) : Stream
{
    private ReadOnlyMemory<byte> _prefix = prefix;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return Read(buffer.AsSpan(offset, count));
    }

    public override int Read(Span<byte> buffer)
    {
        return _prefix.IsEmpty ? inner.Read(buffer) : TakePrefix(buffer);
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        return _prefix.IsEmpty ? inner.ReadAsync(buffer, cancellationToken) : ValueTask.FromResult(TakePrefix(buffer.Span));
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    private int TakePrefix(Span<byte> buffer)
    {
        var count = Math.Min(buffer.Length, _prefix.Length);
        _prefix.Span[..count].CopyTo(buffer);
        _prefix = _prefix[count..];
        return count;
    }
}
