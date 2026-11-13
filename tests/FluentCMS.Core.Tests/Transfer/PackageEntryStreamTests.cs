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
    private static readonly int LineLength = Array.IndexOf(Lines, (byte)'\n');

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

    [Theory]
    [InlineData(CompressionLevel.NoCompression)]
    [InlineData(CompressionLevel.Optimal)]
    public async Task ALineOfAJsonlFileLongerThanTheLimitIsRefusedAndTheRefusalStays(CompressionLevel level)
    {
        using var archive = Archive(level, null);
        var entry = archive.Entries.Single();
        var budget = new PackageBudget(new TransferOptions { MaxLineLength = LineLength - 1 });

        await using var stream = await PackageEntryStream.OpenAsync(entry, budget, Token);
        using var reader = new StreamReader(stream);
        var refused = await Should.ThrowAsync<ValidationException>(() => reader.ReadLineAsync(Token).AsTask());

        refused.Errors.Single().Code.ShouldBe(TransferErrorCodes.InvalidPackage);
        refused.Errors.Single().Message.ShouldContain("FluentCms:Transfer:MaxLineLength");
        budget.Refusal.ShouldBeSameAs(refused);
        budget.RefusedPath.ShouldBe(Name);
        (await Should.ThrowAsync<ValidationException>(() => PackageEntryStream.OpenAsync(entry, budget, Token))).ShouldBeSameAs(refused);
    }

    [Fact]
    public async Task ALineAsLongAsTheLimitIsReadAndAFileThatIsNotJsonlHasNoLineLimit()
    {
        using var archive = Archive(CompressionLevel.Optimal, null);
        using var other = Archive(CompressionLevel.Optimal, null, "plugins/media/files/a/original.bin");

        (await ReadAsync(archive.Entries.Single(), new PackageBudget(new TransferOptions { MaxLineLength = LineLength }))).ShouldBe(Lines);
        (await ReadAsync(other.Entries.Single(), new PackageBudget(new TransferOptions { MaxLineLength = 1 }))).ShouldBe(Lines);
    }

    // A file opened again counts again, whatever was read of it.
    [Fact]
    public async Task EachOpenCountsTheCompressedAndTheUnpackedSizeOfTheFile()
    {
        using var archive = Archive(CompressionLevel.Optimal, null);
        var entry = archive.Entries.Single();
        var read = new PackageBudget(new TransferOptions { MaxReadSize = (2 * entry.CompressedLength) + 1 });
        var unpacked = new PackageBudget(new TransferOptions { MaxUnpackedSize = (2 * entry.Length) + 1 });

        foreach (var budget in new[] { read, unpacked })
        {
            await (await PackageEntryStream.OpenAsync(entry, budget, Token)).DisposeAsync();
            await (await PackageEntryStream.OpenAsync(entry, budget, Token)).DisposeAsync();
            await Should.ThrowAsync<ValidationException>(() => PackageEntryStream.OpenAsync(entry, budget, Token));
        }

        read.Refusal!.Errors.Single().Message.ShouldContain("FluentCms:Transfer:MaxReadSize");
        unpacked.Refusal!.Errors.Single().Message.ShouldContain("FluentCms:Transfer:MaxUnpackedSize");
    }

    private static async Task<byte[]> ReadAsync(ZipArchiveEntry entry, PackageBudget budget)
    {
        await using var stream = await PackageEntryStream.OpenAsync(entry, budget, Token);
        using var read = new MemoryStream();
        await stream.CopyToAsync(read, Token);
        return read.ToArray();
    }

    private static ZipArchive Archive(CompressionLevel level, uint? declared, string name = Name)
    {
        using var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var stream = archive.CreateEntry(name, level).Open();
            stream.Write(Lines);
        }

        var bytes = declared is { } size ? ZipCentralDirectory.Declare(zip.ToArray(), name, size) : zip.ToArray();
        return new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
    }
}
