namespace FluentCMS.Core.Transfer;

// The output of an export, which the exporter cuts when the export stops before its package is whole: from then on nothing
// more is written, the central directory that the disposal of the archive writes included, so the bytes that went out end
// without the end of a zip and no reader takes them for a package. It leaves the output itself open.
internal sealed class ExportOutput(Stream inner) : Stream
{
    private volatile bool _cut;

    public bool IsCut => _cut;

    public override bool CanRead => false;

    public override bool CanSeek => inner.CanSeek;

    public override bool CanWrite => true;

    public override long Length => inner.Length;

    public override long Position
    {
        get => inner.Position;
        set => inner.Position = value;
    }

    public void Cut() => _cut = true;

    public override void Write(byte[] buffer, int offset, int count)
    {
        if (!_cut)
        {
            inner.Write(buffer, offset, count);
        }
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (!_cut)
        {
            inner.Write(buffer);
        }
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        _cut ? Task.CompletedTask : inner.WriteAsync(buffer, offset, count, cancellationToken);

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
        _cut ? ValueTask.CompletedTask : inner.WriteAsync(buffer, cancellationToken);

    public override void Flush()
    {
        if (!_cut)
        {
            inner.Flush();
        }
    }

    public override Task FlushAsync(CancellationToken cancellationToken) =>
        _cut ? Task.CompletedTask : inner.FlushAsync(cancellationToken);

    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

    public override void SetLength(long value) => inner.SetLength(value);

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
