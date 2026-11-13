using System.IO.Compression;
using System.Text;
using FluentCMS.Content;
using FluentCMS.Core.Transfer;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Transfer;

public sealed class PackageEntryStreamTests
{
    private const string Name = "content/tag.jsonl";

    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;
    private static readonly byte[] Lines = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("{\"id\":\"0197a3c1-0000-7000-8000-000000000001\",\"locales\":[]}\n", 4)));

    [Theory]
    [InlineData(0u)]
    [InlineData(10u)]
    public async Task AStoredFileThatHoldsMoreThanItsEntryDeclaresIsRefusedWithItsCode(uint declared)
    {
        using var archive = Archive(CompressionLevel.NoCompression, declared);
        var entry = archive.Entries.Single();
        entry.Length.ShouldBe(declared);

        await using var stream = await PackageEntryStream.OpenAsync(entry, Token);
        var refused = await Should.ThrowAsync<ValidationException>(() => stream.CopyToAsync(Stream.Null, Token));

        refused.Errors.Single().Code.ShouldBe(TransferErrorCodes.InvalidPackage);
        refused.Errors.Single().Message.ShouldContain(Name);
    }

    // Why the import reads through, before any write, only a file whose compressed size is larger than its declared one.
    [Fact]
    public async Task ADeflatedFileStopsAtTheSizeItsEntryDeclares()
    {
        using var archive = Archive(CompressionLevel.Optimal, 10);
        var entry = archive.Entries.Single();
        entry.CompressedLength.ShouldBeGreaterThan(entry.Length);

        await using var stream = await PackageEntryStream.OpenAsync(entry, Token);
        using var read = new MemoryStream();
        await stream.CopyToAsync(read, Token);

        read.ToArray().ShouldBe(Lines[..10]);
    }

    [Theory]
    [InlineData(CompressionLevel.NoCompression)]
    [InlineData(CompressionLevel.Optimal)]
    public async Task AFileThatHoldsWhatItsEntryDeclaresIsReadWhole(CompressionLevel level)
    {
        using var archive = Archive(level, null);

        await using var stream = await PackageEntryStream.OpenAsync(archive.Entries.Single(), Token);
        using var read = new MemoryStream();
        await stream.CopyToAsync(read, Token);

        read.ToArray().ShouldBe(Lines);
    }

    private static ZipArchive Archive(CompressionLevel level, uint? declared)
    {
        using var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var stream = archive.CreateEntry(Name, level).Open();
            stream.Write(Lines);
        }

        var bytes = declared is { } size ? ZipCentralDirectory.Declare(zip.ToArray(), Name, size) : zip.ToArray();
        return new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
    }
}
