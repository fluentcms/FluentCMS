using System.IO.Compression;
using System.Text;
using FluentCMS.Core.Transfer;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Transfer;

// The number of files a package declares, read before ZipArchive holds its entries, is the number ZipArchive then reads.
public sealed class PackageDirectoryTests
{
    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(0, null)]
    [InlineData(3, null)]
    [InlineData(3, "a comment of the archive")]
    [InlineData(ushort.MaxValue + 10, null)]
    public async Task TheDeclaredNumberIsTheNumberOfEntries(int files, string? comment)
    {
        using var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            for (var file = 0; file < files; file++)
            {
                archive.CreateEntry($"content/{file}.jsonl", CompressionLevel.NoCompression);
            }

            archive.Comment = comment ?? "";
        }

        var declared = await PackageDirectory.DeclaredEntriesAsync(zip, Token);

        using var read = await ZipArchive.CreateAsync(zip, ZipArchiveMode.Read, leaveOpen: true, entryNameEncoding: null, Token);
        declared.ShouldBe(read.Entries.Count);
        declared.ShouldBe(files);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a zip at all, but longer than the end record of one")]
    public async Task AFileWithoutTheEndOfACentralDirectoryDeclaresNothing(string text)
    {
        using var file = new MemoryStream(Encoding.UTF8.GetBytes(text));

        (await PackageDirectory.DeclaredEntriesAsync(file, Token)).ShouldBeNull();
    }
}
