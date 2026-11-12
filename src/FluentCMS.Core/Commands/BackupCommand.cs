using System.IO.Compression;
using System.Text.Json;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Storage;
using FluentCMS.Media;
using FluentCMS.Schema;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Commands;

// A backup of a host on SQLite (spec 4.15): an online snapshot of the database, which is consistent while the host runs,
// then the files of the local media storage, in that order, since a deleted asset keeps its file for a while and a file
// written after the snapshot is only one more. Server databases are refused with the guide.
internal sealed class BackupCommand : HostCommand
{
    public const string Output = "--output";

    public override string Name => "backup";

    public override string Description => "Writes a backup of a host on SQLite: an online snapshot of the database and the files of the local media storage, in one archive.";

    public override IReadOnlyList<CommandOption> Options { get; } =
    [
        new(Output, "The archive to write.", "file"),
    ];

    public override async Task<int> RunAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var file = context.Arguments.Value(Output)
            ?? throw new CommandException(CommandErrorCodes.BadUsage, $"'fluentcms {Name}' needs {Output}", CommandExitCodes.BadUsage);
        var path = Path.GetFullPath(file);
        var services = context.Services;
        var (database, databasePath) = BackupArchive.DatabaseOf(services, Name);
        if (!File.Exists(databasePath))
        {
            throw new CommandException(TransferCommandErrorCodes.NoDatabaseFile, $"{databasePath} does not exist, so there is nothing to back up.");
        }

        var streams = await services.GetRequiredService<MigrationRunner>().ListAsync(cancellationToken);
        var root = BackupArchive.LocalMediaRoot(services);
        var storage = services.GetService<IFileStorage>()?.Name;

        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        // Beside the archive rather than in the temporary folder, which may be small or read-only in a container.
        var snapshot = $"{path}.{Guid.NewGuid():N}.db.tmp";
        int files;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            database.Snapshot(snapshot);
            var createdAt = DateTimeOffset.UtcNow;

            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            await using (var archive = await ZipArchive.CreateAsync(stream, ZipArchiveMode.Create, leaveOpen: false, entryNameEncoding: null, cancellationToken))
            {
                await AddFileAsync(archive, BackupArchive.Database, snapshot, cancellationToken);
                files = root is null ? 0 : await AddMediaAsync(archive, root, cancellationToken);

                var manifest = new BackupManifest(
                    BackupArchive.FormatVersion,
                    BackupArchive.HostVersion,
                    services.GetRequiredService<IDataProvider>().Name,
                    SchemaSnapshot.Of(services.GetRequiredService<SchemaModel>()).Hash,
                    [.. streams.Select(stream => new BackupStream(stream.Stream, stream.Applied))],
                    createdAt,
                    storage,
                    files);
                var entry = archive.CreateEntry(BackupArchive.Manifest, CompressionLevel.Fastest);
                await using var json = await entry.OpenAsync(cancellationToken);
                await JsonSerializer.SerializeAsync(json, manifest, BackupArchive.Json, cancellationToken);
            }

            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new CommandException(CommandErrorCodes.OutputNotWritten, $"{path} could not be written: {exception.Message}");
        }
        finally
        {
            File.Delete(temporary);
            File.Delete(snapshot);
        }

        await context.Output.WriteLineAsync($"Wrote {path} with the database and {(files == 1 ? "1 media file" : $"{files} media files")}.");
        if (storage is not null && storage != BackupArchive.LocalStorage)
        {
            await context.Output.WriteLineAsync(
                $"New uploads go to the {storage} storage, whose files are not in the backup; back them up with that storage's own tools ({BackupArchive.Guide}).");
        }

        return CommandExitCodes.Success;
    }

    // A file the local storage is still writing is a temporary file beside its target, and is left out.
    private static async Task<int> AddMediaAsync(ZipArchive archive, DirectoryInfo root, CancellationToken cancellationToken)
    {
        if (!root.Exists)
        {
            return 0;
        }

        var count = 0;
        foreach (var file in root.EnumerateFiles("*", SearchOption.AllDirectories).Where(file => !file.Name.EndsWith(".tmp", StringComparison.Ordinal)))
        {
            var name = BackupArchive.MediaFolder + Path.GetRelativePath(root.FullName, file.FullName).Replace(Path.DirectorySeparatorChar, '/');
            try
            {
                await AddFileAsync(archive, name, file.FullName, cancellationToken);
                count++;
            }
            catch (FileNotFoundException)
            {
                // Deleted since it was listed.
            }
        }

        return count;
    }

    private static async Task AddFileAsync(ZipArchive archive, string name, string path, CancellationToken cancellationToken)
    {
        await using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
        await using var target = await entry.OpenAsync(cancellationToken);
        await source.CopyToAsync(target, cancellationToken);
    }
}
