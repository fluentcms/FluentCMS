using System.Globalization;
using System.IO.Compression;
using FluentCMS.Content;

namespace FluentCMS.Core.Transfer;

// One file of a package as an import reads it (spec 4.14). The import checks the sizes the zip declares, and a stored entry
// yields its compressed size whatever its declared one, so a file that yields more than it declares is refused with FCMS3200.
internal sealed class PackageEntryStream(Stream inner, string path, long declared) : Stream
{
    private long _read;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public static async Task<PackageEntryStream> OpenAsync(ZipArchiveEntry entry, CancellationToken cancellationToken) =>
        new(await entry.OpenAsync(cancellationToken), entry.FullName, entry.Length);

    public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        Count(await inner.ReadAsync(buffer, cancellationToken));

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        Count(await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken));

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
        }

        base.Dispose(disposing);
    }

    private int Count(int read)
    {
        _read += read;
        return _read > declared
            ? throw new ValidationException([new ValidationError("", TransferErrorCodes.InvalidPackage, $"{path} of the package holds more than the {declared.ToString(CultureInfo.InvariantCulture)} bytes its zip entry declares.")])
            : read;
    }
}
