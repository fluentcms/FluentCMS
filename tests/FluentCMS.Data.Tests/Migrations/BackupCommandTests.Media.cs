using System.IO.Compression;
using System.Security.Cryptography;
using FluentCMS.Data.Tests.Providers;
using FluentCMS.Data.Tests.Sqlite;
using FluentCMS.Media;
using FluentCMS.Plugins;
using FluentCMS.Plugins.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Migrations;

public sealed partial class BackupCommandTests
{
    private const string Original = "library/original.bin";
    private static readonly byte[] OldBytes = [1, 2, 3];
    private static readonly byte[] NewBytes = [4, 5, 6];

    [SqliteTheory("the local originals in a SQLite backup")]
    [InlineData("replace")]
    [InlineData("overwrite")]
    [InlineData("change-case")]
    public async Task BackupKeepsThePreviousArchiveWhenTheSnapshottedOriginalIsNotCopied(string change)
    {
        var root = _directory.CreateSubdirectory("media");
        using var host = MediaHost(root);
        (await RunAsync(host, "migrate")).ExitCode.ShouldBe(0);
        var asset = AddAsset(host, Original, OldBytes, deleted: change != "replace");
        root.CreateSubdirectory("library");
        var original = Path.Combine(root.FullName, Original);
        await File.WriteAllBytesAsync(original, OldBytes, Token);
        var first = await RunAsync(host, "backup", "--output", Archive);
        first.ExitCode.ShouldBe(0, first.Error);
        var previous = await File.ReadAllBytesAsync(Archive, Token);

        ((SqliteFileDatabase)_database).AfterSnapshot = () =>
        {
            if (change == "replace")
            {
                const string replacement = "library/replacement.bin";
                File.WriteAllBytes(Path.Combine(root.FullName, replacement), NewBytes);
                using var scope = host.Services.CreateScope();
                var media = scope.ServiceProvider.GetRequiredService<PluginDbContext<MediaPlugin>>();
                var live = media.Set<Asset>().Find(asset.Id)!;
                live.StorageKey = replacement;
                live.Checksum = Convert.ToHexStringLower(SHA256.HashData(NewBytes));
                media.SaveChanges();
                File.Delete(original);
            }
            else if (change == "overwrite")
            {
                File.WriteAllBytes(original, NewBytes);
            }
            else
            {
                File.Delete(original);
                File.WriteAllBytes(Path.Combine(root.FullName, "library/ORIGINAL.bin"), OldBytes);
            }
        };

        var (exitCode, output, error) = await RunAsync(host, "backup", "--output", Archive);

        exitCode.ShouldBe(1);
        output.ShouldBeEmpty();
        error.ShouldStartWith("FCMS3255: ");
        error.ShouldContain(Original);
        (await File.ReadAllBytesAsync(Archive, Token)).ShouldBe(previous);
        Directory.GetFiles(_directory.FullName).ShouldBe([Archive]);
    }

    [SqliteFact("the local originals in a SQLite backup")]
    public async Task BackupAllowsAnExtraUploadAndDoesNotRequireRemoteFiles()
    {
        var root = _directory.CreateSubdirectory("media");
        using var host = MediaHost(root);
        (await RunAsync(host, "migrate")).ExitCode.ShouldBe(0);
        AddAsset(host, Original, OldBytes);
        AddAsset(host, "remote-only.bin", NewBytes, storage: "remote");
        root.CreateSubdirectory("library");
        await File.WriteAllBytesAsync(Path.Combine(root.FullName, Original), OldBytes, Token);
        ((SqliteFileDatabase)_database).AfterSnapshot = () =>
        {
            File.WriteAllBytes(Path.Combine(root.FullName, "extra.bin"), NewBytes);
            AddAsset(host, "extra.bin", NewBytes);
        };

        var (exitCode, output, error) = await RunAsync(host, "backup", "--output", Archive);

        exitCode.ShouldBe(0, error);
        output.ShouldContain("2 media files");
        output.ShouldContain("New uploads go to the remote storage");
        using var archive = ZipFile.OpenRead(Archive);
        foreach (var (key, bytes) in new[] { (Original, OldBytes), ("extra.bin", NewBytes) })
        {
            await using var entry = await archive.GetEntry("media/" + key)!.OpenAsync(Token);
            using var copy = new MemoryStream();
            await entry.CopyToAsync(copy, Token);
            copy.ToArray().ShouldBe(bytes);
        }
    }

    [SqliteFact("named keys in a SQLite backup")]
    public async Task BackupCopiesANamedTmpOriginalButNotUnrelatedUploadScratchFiles()
    {
        const string key = "library/named.tmp";
        var root = _directory.CreateSubdirectory("media");
        using var host = MediaHost(root);
        (await RunAsync(host, "migrate")).ExitCode.ShouldBe(0);
        AddAsset(host, key, OldBytes, ownsFile: false);
        root.CreateSubdirectory("library");
        await File.WriteAllBytesAsync(Path.Combine(root.FullName, key), OldBytes, Token);
        await File.WriteAllBytesAsync(Path.Combine(root.FullName, "named.tmp"), NewBytes, Token);

        var (exitCode, output, error) = await RunAsync(host, "backup", "--output", Archive);

        exitCode.ShouldBe(0, error);
        output.ShouldContain("1 media file");
        using var archive = ZipFile.OpenRead(Archive);
        archive.GetEntry("media/named.tmp").ShouldBeNull();
        await using var entry = await archive.GetEntry("media/" + key)!.OpenAsync(Token);
        using var copy = new MemoryStream();
        await entry.CopyToAsync(copy, Token);
        copy.ToArray().ShouldBe(OldBytes);
    }

    private IHost MediaHost(DirectoryInfo root) => _database.CreateHost(ArticleSchemas.Titled, Production, Migrations.Value,
        new(NoMigrationOnStartup) { ["FluentCms:Media:Local:Root"] = root.FullName }, configure: cms =>
        {
            cms.AddPlugin<MediaPlugin>();
            cms.Providers.Use<IFileStorage, RemoteStorage>();
        });

    private static Asset AddAsset(IHost host, string key, byte[] bytes, string storage = "local", bool deleted = false, bool ownsFile = true)
    {
        using var scope = host.Services.CreateScope();
        var media = scope.ServiceProvider.GetRequiredService<PluginDbContext<MediaPlugin>>();
        var asset = new Asset
        {
            Id = Guid.NewGuid(), StorageKey = key, StorageProvider = storage, OwnsFile = ownsFile,
            Size = bytes.Length, Checksum = Convert.ToHexStringLower(SHA256.HashData(bytes)), DeletedAt = deleted ? DateTimeOffset.UtcNow : null,
        };
        media.Add(asset);
        media.SaveChanges();
        return asset;
    }

    private sealed class RemoteStorage : IFileStorage
    {
        public string Name => "remote";
        public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task WriteAsync(string key, Stream content, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FileStorageMetadata?> GetMetadataAsync(string key, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
