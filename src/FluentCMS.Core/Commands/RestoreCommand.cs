using System.Globalization;
using System.IO.Compression;
using FluentCMS.Core.Diagnostics;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Storage;
using FluentCMS.Schema;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Commands;

// Puts a backup back into a host on SQLite (spec 4.15) with the host stopped: the database file, then the media files into
// the local storage's root. Everything that refuses is asked before the database is touched: an archive that is no backup or
// is of a newer FluentCMS, a backup of another schema or of migrations the host does not have and a database that exists,
// each unless --force is given, one that something has open, media files with no local storage to take them, and a database
// in the archive that SQLite does not read whole. Then it says which migrations the host has that the backup had not.
internal sealed class RestoreCommand : HostCommand
{
    public const string Force = "--force";

    public override string Name => "restore";

    public override string Description => "Restores a backup of 'fluentcms backup' into a host on SQLite that is stopped: the database and the local media files.";

    public override IReadOnlyList<string> Arguments => ["file"];

    public override IReadOnlyList<CommandOption> Options { get; } =
    [
        new(Force, "Replaces the database and the media files the host has, and restores a backup of another schema or migrations."),
    ];

    public override async Task<int> RunAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var services = context.Services;
        var (database, databasePath) = BackupArchive.DatabaseOf(services, Name);
        var path = Path.GetFullPath(context.Arguments.Positional[0]);
        var force = context.Arguments.Has(Force);

        FileStream stream;
        try
        {
            stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new CommandException(TransferCommandErrorCodes.FileNotRead, $"{path} could not be read: {exception.Message}");
        }

        await using (stream)
        {
            ZipArchive archive;
            try
            {
                archive = await ZipArchive.CreateAsync(stream, ZipArchiveMode.Read, leaveOpen: true, entryNameEncoding: null, cancellationToken);
            }
            catch (InvalidDataException)
            {
                throw NoBackup(path, "it is not a zip archive");
            }

            await using (archive)
            {
                var manifest = await BackupArchive.ReadManifestAsync(archive, cancellationToken)
                    ?? throw NoBackup(path, $"it has no {BackupArchive.Manifest} that can be read");
                var snapshot = archive.GetEntry(BackupArchive.Database);
                var provider = services.GetRequiredService<IDataProvider>().Name;
                if (manifest.FormatVersion != BackupArchive.FormatVersion || snapshot is null || manifest.Provider != provider)
                {
                    throw NoBackup(path, $"it is not a backup of the format {BackupArchive.FormatVersion} of a host on {provider} with its database");
                }

                switch (BackupArchive.Compare(manifest.FluentCmsVersion ?? "", BackupArchive.HostVersion))
                {
                    case null:
                        throw NoBackup(path, $"'{manifest.FluentCmsVersion}' is not a version of FluentCMS");
                    case > 0:
                        throw new CommandException(
                            TransferCommandErrorCodes.ArchiveNewer,
                            $"{path} is a backup of FluentCMS {manifest.FluentCmsVersion}, and this host runs {BackupArchive.HostVersion}; a backup is restored into the same or a newer version, so upgrade the host first.");
                }

                await CompareAsync(context, manifest, path, force);

                var media = archive.Entries.Where(entry => entry.FullName.StartsWith(BackupArchive.MediaFolder, StringComparison.Ordinal) && entry.Name.Length > 0).ToList();
                var root = BackupArchive.LocalMediaRoot(services);
                if (media.Count > 0 && root is null)
                {
                    throw new CommandException(
                        TransferCommandErrorCodes.NoLocalMedia,
                        $"{path} has {media.Count} media files and this host has no local media storage to put them in; add the media plugin to the host first.");
                }

                var targets = media.Select(entry => (Entry: entry, Path: BackupArchive.MediaPath(root!, entry.FullName) ?? throw NoBackup(path, $"{entry.FullName} names a path outside the media folder"))).ToList();
                if (File.Exists(databasePath) && !force)
                {
                    throw new CommandException(
                        TransferCommandErrorCodes.DatabaseExists,
                        $"{databasePath} exists; pass {Force} to replace it with the backup, which loses everything written to it since. Stop every host that runs on it first: one that keeps the rollback journal holds no lock while it is idle, so it is not seen, and would go on writing to the database it has open.");
                }

                if (database.IsInUse())
                {
                    throw new CommandException(
                        TransferCommandErrorCodes.DatabaseInUse,
                        $"{databasePath} is open in another process, such as a running host; stop it and run 'fluentcms restore' again.");
                }

                if (targets.Count > 0 && !force && root!.Exists && root.EnumerateFiles("*", SearchOption.AllDirectories).Any())
                {
                    throw new CommandException(
                        TransferCommandErrorCodes.MediaNotEmpty,
                        $"{root.FullName} has files; pass {Force} to write the media files of the backup over them.");
                }

                await RestoreDatabaseAsync(database, databasePath, snapshot, path, cancellationToken);
                var written = 0;
                try
                {
                    foreach (var (entry, target) in targets)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        await entry.ExtractToFileAsync(target, overwrite: true, cancellationToken);
                        written++;
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    throw new CommandException(
                        CommandErrorCodes.OutputNotWritten,
                        $"the database is restored and {written} of {targets.Count} media files, then {exception.Message}; run 'fluentcms restore' again with {Force} once the cause is fixed.");
                }

                await context.Output.WriteLineAsync(targets.Count == 0
                    ? $"Restored {databasePath} from the backup of {manifest.CreatedAt.ToString("u", CultureInfo.InvariantCulture)}."
                    : $"Restored {databasePath} and {(targets.Count == 1 ? "1 media file" : $"{targets.Count} media files")} into {root!.FullName} from the backup of {manifest.CreatedAt.ToString("u", CultureInfo.InvariantCulture)}.");
                if (manifest.MediaStorage is { } storage && storage != BackupArchive.LocalStorage)
                {
                    await context.Output.WriteLineAsync($"The backup was taken with new uploads going to the {storage} storage; its files are not in the backup.");
                }
            }
        }

        await WritePendingAsync(context, cancellationToken);
        return CommandExitCodes.Success;
    }

    // The schema hash of the backup and the migrations of every stream against the host's: another schema or a migration
    // the host does not have leaves tables that a start refuses (FCMS1301) or content that nothing reads. A migration the
    // host has and the backup had not is pending, which is what restoring into a newer host is for.
    private static async Task CompareAsync(CommandContext context, BackupManifest manifest, string path, bool force)
    {
        var differences = new List<string>();
        var hash = SchemaSnapshot.Of(context.Services.GetRequiredService<SchemaModel>()).Hash;
        if (!string.Equals(manifest.SchemaHash, hash, StringComparison.Ordinal))
        {
            differences.Add($"the schema hash is {manifest.SchemaHash ?? "missing"} in the backup and {hash} in the host");
        }

        var runner = context.Services.GetRequiredService<MigrationRunner>();
        foreach (var stream in manifest.Migrations ?? [])
        {
            if (!runner.Streams.Contains(stream.Name, StringComparer.Ordinal))
            {
                differences.Add($"the backup has the stream {stream.Name}, which the host does not have");
                continue;
            }

            var known = runner.MigrationsOf(stream.Name);
            var unknown = (stream.Applied ?? []).Where(id => !known.Contains(id, StringComparer.Ordinal)).ToList();
            if (unknown.Count > 0)
            {
                differences.Add($"stream {stream.Name} has applied {Listed(unknown)}, which the host does not have");
            }
        }

        if (differences.Count == 0)
        {
            return;
        }

        var difference = PrintableText.Escape(string.Join("; ", differences));
        if (!force)
        {
            throw new CommandException(
                TransferCommandErrorCodes.BackupOfAnotherSchema,
                $"{path} is a backup of another schema or of migrations this host does not have: {difference}. Restore it into a host of the same schema, or pass {Force} to restore it anyway; a start may then refuse a missing table or column with FCMS1301.");
        }

        await context.Error.WriteLineAsync(
            $"{TransferCommandErrorCodes.BackupOfAnotherSchema}: warning: {path} is a backup of another schema or of migrations this host does not have: {difference}; restored because of {Force}.");
    }

    private static string Listed(List<string> ids) => ids.Count <= 3 ? string.Join(", ", ids) : $"{string.Join(", ", ids.Take(3))} and {ids.Count - 3} more";

    // Unpacked beside the database, so that it is moved into place on the same volume, and checked before the database is
    // touched, so that an entry that is not a SQLite database, or one cut off, never replaces it.
    private static async Task RestoreDatabaseAsync(IDatabaseFile database, string databasePath, ZipArchiveEntry snapshot, string archive, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        var unpacked = $"{databasePath}.{Guid.NewGuid():N}.restore";
        try
        {
            try
            {
                await snapshot.ExtractToFileAsync(unpacked, overwrite: false, cancellationToken);
            }
            catch (InvalidDataException exception)
            {
                throw NoBackup(archive, $"its database cannot be read: {exception.Message}");
            }

            if (database.Check(unpacked) is { } problem)
            {
                throw NoBackup(archive, $"its database is not a whole SQLite database ({PrintableText.Escape(problem)}), so {databasePath} was left as it was");
            }

            database.Restore(unpacked);
        }
        finally
        {
            File.Delete(unpacked);
        }
    }

    private static async Task WritePendingAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var pending = await context.Services.GetRequiredService<MigrationRunner>().GetPendingMigrationsAsync(cancellationToken);
        if (pending.Count == 0)
        {
            await context.Output.WriteLineAsync("No migration is pending.");
            return;
        }

        foreach (var stream in pending.GroupBy(migration => migration.Stream))
        {
            await context.Output.WriteLineAsync(stream.Key);
            foreach (var migration in stream)
            {
                await context.Output.WriteLineAsync($"  {migration.Id}");
            }
        }

        await context.Output.WriteLineAsync($"{MigrateCommand.Count(pending.Count)} pending; run 'fluentcms migrate' to apply them, or start the host if it migrates on start.");
    }

    private static CommandException NoBackup(string path, string reason) =>
        new(TransferCommandErrorCodes.InvalidArchive, $"{path} is not a backup of 'fluentcms backup': {reason}.");
}
