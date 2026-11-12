using System.IO.Compression;
using System.Text.Json.Nodes;
using FluentCMS.Core.Commands;
using FluentCMS.Data.Tests.Providers;
using FluentCMS.Data.Tests.Sqlite;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Migrations;

// backup and restore on the provider of the run: a SQLite host takes a snapshot and gets it back, and a server database
// refuses both with the code that names the guide.
public sealed class BackupCommandTests : IDisposable
{
    private const string Production = "Production";

    private static readonly Dictionary<string, string?> NoMigrationOnStartup = new() { ["FluentCms:Database:MigrateOnStartup"] = "false" };

    // One set for every host, so that a restored database has the migrations of the host it is restored into.
    private static readonly Lazy<System.Reflection.Assembly> Migrations = new(() => GeneratedMigrations.CompileFirst(ArticleSchemas.Titled));

    private readonly TestDatabase _database = TestDatabase.Create();
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("fluentcms-backup-tests-");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string Archive => Path.Combine(_directory.FullName, "backup.zip");

    public void Dispose()
    {
        _database.Dispose();
        _directory.Delete(recursive: true);
    }

    [Fact]
    public async Task OnAServerDatabaseBackupAndRestoreRefuseWithTheirCodeAndNameTheGuide()
    {
        Assert.SkipWhen(TestProvider.IsSqlite, "SQLite is backed up by the commands.");
        using var host = Host(_database);

        var backup = await RunAsync(host, "backup", "--output", Archive);
        var restore = await RunAsync(host, "restore", Archive);

        foreach (var (exitCode, output, error) in new[] { backup, restore })
        {
            exitCode.ShouldBe(1);
            output.ShouldBeEmpty();
            error.ShouldStartWith("FCMS3240: ");
            error.ShouldContain("docs/backups.md");
        }

        File.Exists(Archive).ShouldBeFalse();
    }

    [SqliteFact("the backup of a SQLite database")]
    public async Task ABackupRestoredIntoAHostWithNoDatabaseHasItsTablesAndRowsAndNothingPending()
    {
        using var source = Host(_database);
        (await RunAsync(source, "migrate")).ExitCode.ShouldBe(0);
        _database.Execute("CREATE TABLE backup_probe (value TEXT); INSERT INTO backup_probe VALUES ('kept');");

        var (exitCode, output, error) = await RunAsync(source, "backup", "--output", Archive);

        exitCode.ShouldBe(0, error);
        output.ShouldBe($"Wrote {Archive} with the database and 0 media files.{Environment.NewLine}");
        using var target = new SqliteFileDatabase();
        using var restoring = Host(target);
        (exitCode, output, error) = await RunAsync(restoring, "restore", Archive);

        exitCode.ShouldBe(0, error);
        output.ShouldEndWith($"No migration is pending.{Environment.NewLine}", Case.Sensitive, output);
        target.Tables().ShouldBe(_database.Tables());
        target.ReadStrings("SELECT value FROM backup_probe").ShouldBe(["kept"]);
    }

    [SqliteFact("the backup of a SQLite database")]
    public async Task RestoreRefusesAnExistingDatabaseWithoutForceANewerVersionAnArchiveThatIsNoBackupAndMediaWithNoStorage()
    {
        using var host = Host(_database);
        (await RunAsync(host, "migrate")).ExitCode.ShouldBe(0);
        (await RunAsync(host, "backup", "--output", Archive)).ExitCode.ShouldBe(0);

        var existing = await RunAsync(host, "restore", Archive);
        existing.ExitCode.ShouldBe(1);
        existing.Error.ShouldStartWith("FCMS3244: ");
        existing.Error.ShouldContain("--force");

        await SetVersionAsync("999.0.0");
        var newer = await RunAsync(host, "restore", Archive, "--force");
        newer.ExitCode.ShouldBe(1);
        newer.Error.ShouldStartWith("FCMS3243: ");
        newer.Error.ShouldContain("999.0.0");

        var other = Path.Combine(_directory.FullName, "other.zip");
        await using (var zip = await ZipArchive.CreateAsync(File.Create(other), ZipArchiveMode.Create, leaveOpen: false, entryNameEncoding: null, Token))
        {
            zip.CreateEntry("manifest.json");
        }

        var noBackup = await RunAsync(host, "restore", other, "--force");
        noBackup.ExitCode.ShouldBe(1);
        noBackup.Error.ShouldStartWith("FCMS3242: ");

        await SetVersionAsync("0.0.1");
        var withMedia = Path.Combine(_directory.FullName, "with-media.zip");
        File.Copy(Archive, withMedia);
        await using (var zip = await ZipArchive.CreateAsync(File.Open(withMedia, FileMode.Open), ZipArchiveMode.Update, leaveOpen: false, entryNameEncoding: null, Token))
        {
            zip.CreateEntry("media/site/asset/original.png");
        }

        var noMediaStorage = await RunAsync(host, "restore", withMedia, "--force");
        noMediaStorage.ExitCode.ShouldBe(1);
        noMediaStorage.Error.ShouldStartWith("FCMS3247: ");

        var forced = await RunAsync(host, "restore", Archive, "--force");
        forced.ExitCode.ShouldBe(0, forced.Error);
    }

    [SqliteFact("the backup of a SQLite database")]
    public async Task RestoreRefusesADatabaseThatAnotherConnectionHasOpen()
    {
        using var host = Host(_database);
        (await RunAsync(host, "migrate")).ExitCode.ShouldBe(0);
        (await RunAsync(host, "backup", "--output", Archive)).ExitCode.ShouldBe(0);
        using var running = new Microsoft.Data.Sqlite.SqliteConnection(_database.ConnectionString);
        running.Open();
        using (var read = running.CreateCommand())
        {
            read.CommandText = "SELECT count(*) FROM sqlite_master";
            read.ExecuteScalar();
        }

        var (exitCode, _, error) = await RunAsync(host, "restore", Archive, "--force");

        exitCode.ShouldBe(1);
        error.ShouldStartWith("FCMS3245: ");
    }

    private async Task SetVersionAsync(string version)
    {
        await using var zip = await ZipArchive.CreateAsync(File.Open(Archive, FileMode.Open), ZipArchiveMode.Update, leaveOpen: false, entryNameEncoding: null, Token);
        var entry = zip.GetEntry(BackupArchive.Manifest)!;
        JsonNode manifest;
        await using (var read = await entry.OpenAsync(Token))
        {
            manifest = (await JsonNode.ParseAsync(read, cancellationToken: Token))!;
        }

        manifest["fluentCmsVersion"] = version;
        entry.Delete();
        await using var write = await zip.CreateEntry(BackupArchive.Manifest).OpenAsync(Token);
        await write.WriteAsync(System.Text.Encoding.UTF8.GetBytes(manifest.ToJsonString()), Token);
    }

    private static IHost Host(TestDatabase database) =>
        database.CreateHost(ArticleSchemas.Titled, Production, Migrations.Value, NoMigrationOnStartup);

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(IHost host, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await CommandRunner.RunAsync(host.Services, [CommandRunner.Prefix, .. args], output, error, Token);
        return (exitCode!.Value, output.ToString(), error.ToString());
    }
}
