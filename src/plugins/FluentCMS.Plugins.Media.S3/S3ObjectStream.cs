using Amazon.S3;
using Amazon.S3.Model;

namespace FluentCMS.Plugins.Media.S3;

// An object read through the body of one GET while it is read in order, and through a ranged GET of the same version after a
// seek, so that an original is served in ranges. An object replaced while it is read fails the read rather than mixing two files.
internal sealed class S3ObjectStream : Stream
{
    private readonly IAmazonS3 _client;
    private readonly string _bucket;
    private readonly string _key;
    private readonly string _etag;
    private GetObjectResponse? _body;
    private long _bodyPosition;
    private long _position;
    private bool _disposed;

    public S3ObjectStream(IAmazonS3 client, string bucket, string key, GetObjectResponse response)
    {
        _client = client;
        _bucket = bucket;
        _key = key;
        _etag = response.ETag;
        _body = response;
        Length = response.ContentLength;
    }

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => false;

    public override long Length { get; }

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

    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (buffer.IsEmpty || _position >= Length)
        {
            return 0;
        }

        if (_body is null || _bodyPosition != _position)
        {
            OpenAtPositionAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        return Advance(_body!.ResponseStream.Read(buffer));
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
            await OpenAtPositionAsync(cancellationToken);
        }

        return Advance(await _body!.ResponseStream.ReadAsync(buffer, cancellationToken));
    }

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

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

    private async Task OpenAtPositionAsync(CancellationToken cancellationToken)
    {
        _body?.Dispose();
        _body = null;
        _body = await _client.GetObjectAsync(
            new GetObjectRequest { BucketName = _bucket, Key = _key, ByteRange = new ByteRange(_position, Length - 1), EtagToMatch = _etag },
            cancellationToken);
        _bodyPosition = _position;
    }

    private int Advance(int read)
    {
        _position += read;
        _bodyPosition += read;
        return read;
    }
}
