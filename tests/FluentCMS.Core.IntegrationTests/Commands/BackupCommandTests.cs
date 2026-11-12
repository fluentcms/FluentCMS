using System.Net;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Storage;
using FluentCMS.Plugins.Media;
using FluentCMS.Samples.Blog;
using FluentCMS.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Commands;

// `fluentcms backup` of a running host of the Blog sample on a SQLite file, restored into an empty folder by a host that has
// not started, as `fluentcms restore` runs: the restored host has the users, keys, content, versions and media files.
public sealed class BackupCommandTests : IDisposable
{
    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("fluentcms-backup-commands-");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _directory.Delete(recursive: true);
    }

    private string Folder(string name) => Directory.CreateDirectory(Path.Combine(_directory.FullName, name)).FullName;

    private static FluentCmsTestHost BlogHost(string folder) => new FluentCmsTestHost()
        .UseSqlite(Path.Combine(folder, "cms.db"))
        .WithContentTypes([.. BlogSample.ContentTypes])
        .WithServices(services => services.PostConfigure<MediaOptions>(options => options.Local.Root = Path.Combine(folder, "media")))
        .Configure(cms => cms.AddBlogSample());

    [Fact]
    public async Task ABackupOfARunningHostRestoredIntoAnEmptyFolderGivesAHostWithTheSameUsersKeysContentVersionsAndMediaFiles()
    {
        var sourceFolder = Folder("source");
        var targetFolder = Folder("target");
        var archive = Path.Combine(_directory.FullName, "backup.zip");

        await using var source = BlogHost(sourceFolder);
        var user = await source.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        var key = await source.CreateApiKeyAsync(ApiKeyClass.Management, BuiltInRoles.SuperAdminName, cancellationToken: Token);
        var admin = source.As(await source.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token));
        var author = (await admin.Content<Author>().QueryAsync(new ContentQuery { Status = QueryStatus.Draft }, Token)).Items[0];
        author.Name = "A second version";
        await admin.Content<Author>().UpdateAsync(author.Id, author, author.Version, Token);

        var media = Files(Path.Combine(sourceFolder, "media"));
        media.ShouldNotBeEmpty();

        var backup = await source.RunCommandAsync("backup", "--output", archive);

        backup.ExitCode.ShouldBe(0, backup.Error);
        backup.Output.ShouldStartWith($"Wrote {archive} with the database and ");
        backup.Output.ShouldContain($"{media.Count} media files");

        await using var target = BlogHost(targetFolder);
        var restore = await target.RunCommandAsync("restore", archive);

        restore.ExitCode.ShouldBe(0, restore.Error);
        restore.Output.ShouldContain($"and {media.Count} media files into {Path.Combine(targetFolder, "media")}");
        restore.Output.ShouldEndWith($"No migration is pending.{Environment.NewLine}");

        Files(Path.Combine(targetFolder, "media")).ShouldBe(media);
        foreach (var file in media.Keys)
        {
            (await File.ReadAllBytesAsync(Path.Combine(targetFolder, "media", file), Token)).ShouldBe(await File.ReadAllBytesAsync(Path.Combine(sourceFolder, "media", file), Token), file);
        }

        Rows(target).ShouldBe(Rows(source));
        await using (var scope = target.Services.CreateAsyncScope())
        {
            (await scope.ServiceProvider.GetRequiredService<CmsUserManager>().FindByIdAsync(user.Id.ToString()))!.Email.ShouldBe(user.Email);
        }

        using var restored = target.AsKey(key);
        using var original = source.AsKey(key);
        var path = $"/api/management/v1/content/author/{author.Id:D}/versions";
        using var response = await restored.GetAsync(new Uri(path, UriKind.Relative), Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Token)).ShouldBe(await (await original.GetAsync(new Uri(path, UriKind.Relative), Token)).Content.ReadAsStringAsync(Token));
    }

    // What the content of a host is: its entries with their states and its versions.
    private static string Rows(FluentCmsTestHost host)
    {
        using var scope = host.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
        var documents = context.Documents.IgnoreQueryFilters().AsNoTracking().AsEnumerable()
            .Select(document => $"{document.Type} {document.DocumentId} {document.Locale} {document.Status} {document.UpdatedAt.UtcTicks}")
            .Order(StringComparer.Ordinal);
        var versions = context.Versions.AsNoTracking().AsEnumerable()
            .Select(version => $"{version.DocumentId} {version.Locale} {version.Number} {version.Snapshot}")
            .Order(StringComparer.Ordinal);
        return string.Join('\n', documents.Concat(versions));
    }

    private static Dictionary<string, long> Files(string root) =>
        Directory.Exists(root)
            ? Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToDictionary(file => Path.GetRelativePath(root, file), file => new FileInfo(file).Length, StringComparer.Ordinal)
            : [];
}
