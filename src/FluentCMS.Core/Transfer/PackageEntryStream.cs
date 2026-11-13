using System.Globalization;
using System.IO.Compression;
using FluentCMS.Content;

namespace FluentCMS.Core.Transfer;

// One file of a package as an import reads it (spec 4.14). The import checks the sizes the zip declares, and a stored entry
// yields its compressed size whatever its declared one, so a file that yields more than it declares is refused with FCMS3200.
// Opened with the import's budget, a .jsonl file is refused too once a line is longer than FluentCms:Transfer:MaxLineLength,
// before a reader holds it whole, and a refusal is the budget's, which every later open throws again.
internal sealed class PackageEntryStream(Stream inner, string path, long declared, PackageBudget? budget, int? lineLimit) : Stream
{
    private long _read;
    private long _line;

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
        new(await entry.OpenAsync(cancellationToken), entry.FullName, entry.Length, null, null);

    public static async Task<PackageEntryStream> OpenAsync(ZipArchiveEntry entry, PackageBudget budget, CancellationToken cancellationToken)
    {
        budget.Open(entry);
        return new(await entry.OpenAsync(cancellationToken), entry.FullName, entry.Length, budget, budget.LineLimit(entry));
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        int read;
        try
        {
            read = inner.Read(buffer, offset, count);
        }
        catch (InvalidDataException exception)
        {
            throw Unreadable(exception);
        }

        return Count(buffer.AsSpan(offset, read));
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        int read;
        try
        {
            read = await inner.ReadAsync(buffer, cancellationToken);
        }
        catch (InvalidDataException exception)
        {
            throw Unreadable(exception);
        }

        return Count(buffer.Span[..read]);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

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

    private int Count(ReadOnlySpan<byte> read)
    {
        _read += read.Length;
        if (_read > declared)
        {
            throw Refuse($"{path} of the package holds more than the {declared.ToString(CultureInfo.InvariantCulture)} bytes its zip entry declares.");
        }

        if (lineLimit is { } limit)
        {
            var rest = read;
            int end;
            while ((end = rest.IndexOf((byte)'\n')) >= 0)
            {
                CheckLine(_line + end, limit);
                _line = 0;
                rest = rest[(end + 1)..];
            }

            _line += rest.Length;
            CheckLine(_line, limit);
        }

        return read.Length;
    }

    private void CheckLine(long length, int limit)
    {
        if (length > limit)
        {
            throw Refuse($"{path} of the package has a line longer than {limit.ToString(CultureInfo.InvariantCulture)} bytes, FluentCms:Transfer:MaxLineLength.");
        }
    }

    // Compressed data that cannot be read refuses the package as a bound does, so that the run stops with it too.
    private ValidationException Unreadable(InvalidDataException exception) => Refuse($"{path} of the package cannot be read: {exception.Message}");

    private ValidationException Refuse(string message) => budget?.Refuse(path, message) ?? TransferImporter.Invalid(message);
}
