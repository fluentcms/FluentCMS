using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json.Nodes;
using FluentCMS.Core.Commands;
using FluentCMS.Data.Tests.Providers;
using FluentCMS.Data.Tests.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
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

    private string DatabasePath => new SqliteConnectionStringBuilder(_database.ConnectionString).DataSource;

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

    [SqliteTheory("paths of a SQLite backup")]
    [InlineData("")]
    [InlineData("-wal")]
    [InlineData("-shm")]
    [InlineData("-journal")]
    public async Task BackupRefusesTheDatabaseAndItsSideFilesBeforeOpeningThem(string suffix)
    {
        using var host = Host(_database);
        string[] paths = [DatabasePath, DatabasePath + "-wal", DatabasePath + "-shm", DatabasePath + "-journal"];
        foreach (var path in paths)
        {
            await File.WriteAllTextAsync(path, path, Token);
        }

        var (exitCode, output, error) = await RunAsync(host, "backup", "--output", DatabasePath + suffix);

        exitCode.ShouldBe(1);
        output.ShouldBeEmpty();
        error.ShouldStartWith("FCMS3254: ");
        foreach (var path in paths)
        {
            (await File.ReadAllTextAsync(path, Token)).ShouldBe(path);
        }
    }

    [SqliteTheory("paths of a SQLite backup")]
    [InlineData(".")]
    [InlineData("new/sub/backup.zip")]
    public async Task BackupRefusesTheMediaRootAndItsDescendantsBeforeCreatingAnything(string destination)
    {
        var media = Path.Combine(_directory.FullName, "media");
        using var host = Host(_database, media);

        var (exitCode, _, error) = await RunAsync(host, "backup", "--output", Path.Combine(media, destination));

        exitCode.ShouldBe(1);
        error.ShouldStartWith("FCMS3254: ");
        Directory.Exists(media).ShouldBeFalse();
        File.Exists(DatabasePath).ShouldBeFalse();
    }

    [SqliteFact("paths of a SQLite backup")]
    public async Task BackupChecksTheMediaRootAndDatabaseThroughDirectoryLinks()
    {
        var storage = _directory.CreateSubdirectory("storage");
        var media = storage.CreateSubdirectory("media");
        var alias = Path.Combine(_directory.FullName, "linked");
        await LinkDirectoryAsync(alias, storage.FullName);
        var linkedDatabaseFolder = Path.Combine(_directory.FullName, "linked-db");
        await LinkDirectoryAsync(linkedDatabaseFolder, Path.GetDirectoryName(DatabasePath)!);
        try
        {
            using var host = Host(_database, Path.Combine(alias, "media"));
            foreach (var path in new[] { Path.Combine(media.FullName, "new", "backup.zip"), Path.Combine(linkedDatabaseFolder, Path.GetFileName(DatabasePath) + "-wal") })
            {
                var (exitCode, _, error) = await RunAsync(host, "backup", "--output", path);
                exitCode.ShouldBe(1);
                error.ShouldStartWith("FCMS3254: ");
            }

            media.EnumerateFileSystemInfos().ShouldBeEmpty();
            File.Exists(DatabasePath).ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(alias);
            Directory.Delete(linkedDatabaseFolder);
        }
    }

    [SqliteFact("paths of a SQLite backup")]
    public async Task BackupCanReplaceAnArchiveInASiblingOfTheMediaRoot()
    {
        var media = _directory.CreateSubdirectory("media");
        using var host = Host(_database, media.FullName);
        (await RunAsync(host, "migrate")).ExitCode.ShouldBe(0);
        var path = Path.Combine(_directory.CreateSubdirectory("media-backups").FullName, "backup.zip");
        await File.WriteAllTextAsync(path, "previous archive", Token);

        var (exitCode, _, error) = await RunAsync(host, "backup", "--output", path);

        exitCode.ShouldBe(0, error);
        using var archive = ZipFile.OpenRead(path);
        archive.GetEntry(BackupArchive.Database).ShouldNotBeNull();
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
        existing.Error.ShouldContain("one that keeps the rollback journal holds no lock while it is idle, so it is not seen");

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

    // A backup of another schema, or with a migration the host does not have, would leave tables that a start refuses.
    // --force replaces the database only, so it does not allow that.
    [SqliteFact("the backup of a SQLite database")]
    public async Task RestoreRefusesABackupOfAnotherSchemaOrOfMigrationsTheHostDoesNotHaveUnlessAllowed()
    {
        using var host = Host(_database);
        (await RunAsync(host, "migrate")).ExitCode.ShouldBe(0);
        (await RunAsync(host, "backup", "--output", Archive)).ExitCode.ShouldBe(0);
        _database.Execute("CREATE TABLE restore_probe (value TEXT);");

        await EditManifestAsync(manifest => manifest["schemaHash"] = new string('0', 64));
        var schema = await RunAsync(host, "restore", Archive, "--force");
        schema.ExitCode.ShouldBe(1);
        schema.Error.ShouldStartWith("FCMS3249: ");
        schema.Error.ShouldContain($"the schema hash is {new string('0', 64)} in the backup and ");
        schema.Error.ShouldContain("--allow-schema-mismatch");
        _database.Tables().ShouldContain("restore_probe");

        // A refusal after the check says nothing of restoring.
        using (var running = new Microsoft.Data.Sqlite.SqliteConnection(_database.ConnectionString))
        {
            running.Open();
            using var read = running.CreateCommand();
            read.CommandText = "SELECT count(*) FROM sqlite_master";
            read.ExecuteScalar();
            var inUse = await RunAsync(host, "restore", Archive, "--force", "--allow-schema-mismatch");
            inUse.ExitCode.ShouldBe(1);
            inUse.Error.ShouldStartWith("FCMS3245: ");
            inUse.Error.ShouldNotContain("FCMS3249");
        }

        await EditManifestAsync(manifest =>
        {
            manifest["schemaHash"] = null;
            var streams = manifest["migrations"]!.AsArray();
            streams.Add(new JsonObject { ["name"] = "acme.gone", ["applied"] = new JsonArray("20260101000000_Initial") });
            streams.Single(stream => stream!["name"]!.GetValue<string>() == "system")!["applied"]!.AsArray().Add("29990101000000_FromTheFuture");
        });
        var migrations = await RunAsync(host, "restore", Archive, "--force", "--allow-schema-mismatch");
        migrations.ExitCode.ShouldBe(0, migrations.Error);
        migrations.Error.ShouldStartWith("FCMS3249: warning: ");
        migrations.Error.ShouldContain("the schema hash is missing in the backup");
        migrations.Error.ShouldContain("the backup has the stream acme.gone, which the host does not have");
        migrations.Error.ShouldContain("stream system has applied 29990101000000_FromTheFuture, which the host does not have");
        migrations.Error.ShouldContain("restored because of --allow-schema-mismatch");
        _database.Tables().ShouldNotContain("restore_probe");
    }

    // Restoring into a newer host is what restore is for: a migration the host has and the backup had not is pending.
    [SqliteFact("the backup of a SQLite database")]
    public async Task AMigrationTheHostHasAndTheBackupHadNotIsNoDifference()
    {
        using var host = Host(_database);
        (await RunAsync(host, "migrate")).ExitCode.ShouldBe(0);
        (await RunAsync(host, "backup", "--output", Archive)).ExitCode.ShouldBe(0);
        await EditManifestAsync(manifest =>
        {
            foreach (var stream in manifest["migrations"]!.AsArray())
            {
                stream!["applied"] = new JsonArray();
            }
        });

        var (exitCode, _, error) = await RunAsync(host, "restore", Archive, "--force");

        exitCode.ShouldBe(0, error);
        error.ShouldNotContain("FCMS3249");
    }

    // The database of the archive is read whole before the database it replaces is moved, which stays as it was.
    [SqliteTheory("the backup of a SQLite database")]
    [InlineData("not a database, but long enough to look like the start of a file of SQLite")]
    [InlineData("")]
    public async Task RestoreRefusesADatabaseThatSqliteDoesNotReadWholeAndKeepsTheOneItHas(string database)
    {
        using var host = Host(_database);
        (await RunAsync(host, "migrate")).ExitCode.ShouldBe(0);
        (await RunAsync(host, "backup", "--output", Archive)).ExitCode.ShouldBe(0);
        _database.Execute("CREATE TABLE restore_probe (value TEXT);");
        await using (var zip = await ZipArchive.CreateAsync(File.Open(Archive, FileMode.Open), ZipArchiveMode.Update, leaveOpen: false, entryNameEncoding: null, Token))
        {
            zip.GetEntry(BackupArchive.Database)!.Delete();
            await using var write = await zip.CreateEntry(BackupArchive.Database).OpenAsync(Token);
            await write.WriteAsync(System.Text.Encoding.UTF8.GetBytes(database), Token);
        }

        var (exitCode, _, error) = await RunAsync(host, "restore", Archive, "--force");

        exitCode.ShouldBe(1);
        error.ShouldStartWith("FCMS3242: ");
        error.ShouldContain("its database is not a whole SQLite database");
        _database.Tables().ShouldContain("restore_probe");
        var path = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(_database.ConnectionString).DataSource;
        Directory.EnumerateFiles(Path.GetDirectoryName(path)!).Where(file => file.Contains(".restore", StringComparison.Ordinal) || file.Contains(".replaced", StringComparison.Ordinal)).ShouldBeEmpty();
    }

    // The archive holds password hashes, key hashes, tokens and the key ring.
    [SqliteFact("the backup of a SQLite database")]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public async Task TheArchiveIsReadableByItsOwnerOnly()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows has no file mode; the archive takes the permissions of its folder.");
        using var host = Host(_database);
        (await RunAsync(host, "migrate")).ExitCode.ShouldBe(0);
        File.WriteAllText(Archive, "an older backup");
        File.SetUnixFileMode(Archive, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

        (await RunAsync(host, "backup", "--output", Archive)).ExitCode.ShouldBe(0);

        File.GetUnixFileMode(Archive).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private Task SetVersionAsync(string version) => EditManifestAsync(manifest => manifest["fluentCmsVersion"] = version);

    private static async Task LinkDirectoryAsync(string link, string directory)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(link, directory);
            return;
        }

        // A junction, unlike a symbolic link, needs no special permission on Windows.
        using var mklink = Process.Start(new ProcessStartInfo("cmd.exe", $"/d /c mklink /j \"{link}\" \"{directory}\"") { UseShellExecute = false })!;
        await mklink.WaitForExitAsync(Token);
        mklink.ExitCode.ShouldBe(0);
    }

    private async Task EditManifestAsync(Action<JsonNode> edit)
    {
        await using var zip = await ZipArchive.CreateAsync(File.Open(Archive, FileMode.Open), ZipArchiveMode.Update, leaveOpen: false, entryNameEncoding: null, Token);
        var entry = zip.GetEntry(BackupArchive.Manifest)!;
        JsonNode manifest;
        await using (var read = await entry.OpenAsync(Token))
        {
            manifest = (await JsonNode.ParseAsync(read, cancellationToken: Token))!;
        }

        edit(manifest);
        entry.Delete();
        await using var write = await zip.CreateEntry(BackupArchive.Manifest).OpenAsync(Token);
        await write.WriteAsync(System.Text.Encoding.UTF8.GetBytes(manifest.ToJsonString()), Token);
    }

    private static IHost Host(TestDatabase database, string? mediaRoot = null) =>
        database.CreateHost(ArticleSchemas.Titled, Production, Migrations.Value, NoMigrationOnStartup, services =>
        {
            if (mediaRoot is not null)
            {
                services.AddKeyedSingleton(BackupArchive.LocalMediaRootKey, new DirectoryInfo(mediaRoot));
            }
        });

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(IHost host, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await CommandRunner.RunAsync(host.Services, [CommandRunner.Prefix, .. args], output, error, Token);
        return (exitCode!.Value, output.ToString(), error.ToString());
    }
}
