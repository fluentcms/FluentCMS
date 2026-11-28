using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using FluentCMS.Core.Diagnostics;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Storage;
using FluentCMS.Media;
using FluentCMS.Schema;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Commands;

// A backup of a host on SQLite (spec 4.15): an online snapshot of the database, which is consistent while the host runs,
// then the files of the local media storage. A file written after the snapshot is only one more; an original missing
// from the archive may have been replaced in the meantime, so the backup must be retried.
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
        var root = BackupArchive.LocalMediaRoot(services);
        var mediaFiles = BackupDestination.Check(path, databasePath, root, cancellationToken);
        if (!File.Exists(databasePath))
        {
            throw new CommandException(TransferCommandErrorCodes.NoDatabaseFile, $"{databasePath} does not exist, so there is nothing to back up.");
        }

        var streams = await services.GetRequiredService<MigrationRunner>().ListAsync(cancellationToken);
        var storage = services.GetService<IFileStorage>()?.Name;

        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        // Beside the archive rather than in the temporary folder, which may be small or read-only in a container.
        var snapshot = $"{path}.{Guid.NewGuid():N}.db.tmp";
        int files;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // SQLite writes the snapshot into the empty file, which keeps its mode.
            await OwnerOnly(snapshot).DisposeAsync();
            database.Snapshot(snapshot);
            var createdAt = DateTimeOffset.UtcNow;
            var originals = database.LocalOriginals(snapshot).ToArray();
            var namedTemporaryKeys = originals.Where(original => original.Key.EndsWith(".tmp", StringComparison.Ordinal))
                .Select(original => original.Key).ToHashSet(StringComparer.Ordinal);

            await using (var stream = OwnerOnly(temporary))
            await using (var archive = await ZipArchive.CreateAsync(stream, ZipArchiveMode.Create, leaveOpen: false, entryNameEncoding: null, cancellationToken))
            {
                await AddFileAsync(archive, BackupArchive.Database, snapshot, cancellationToken);
                files = root is null ? 0 : await AddMediaAsync(archive, root, mediaFiles, namedTemporaryKeys, cancellationToken);

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

            await VerifyOriginalsAsync(temporary, originals, cancellationToken);
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

    // The archive holds password hashes, key hashes, tokens and the key ring, so it and the snapshot are made readable by their
    // owner only where files have a Unix mode; on Windows they take the permissions of their folder, which the guide covers.
    // The move over the target keeps the mode.
    private static FileStream OwnerOnly(string path)
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, BufferSize = 81920, Options = FileOptions.Asynchronous };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        return new FileStream(path, options);
    }

    // Leave out upload scratch files, but an import can name a real original with a .tmp key.
    private static async Task<int> AddMediaAsync(ZipArchive archive, DirectoryInfo root, IEnumerable<FileInfo> files, HashSet<string> namedTemporaryKeys, CancellationToken cancellationToken)
    {
        var count = 0;
        foreach (var file in files)
        {
            var key = Path.GetRelativePath(root.FullName, file.FullName).Replace(Path.DirectorySeparatorChar, '/');
            if (key.EndsWith(".tmp", StringComparison.Ordinal) && !namedTemporaryKeys.Contains(key))
            {
                continue;
            }

            try
            {
                await AddFileAsync(archive, BackupArchive.MediaFolder + key, file.FullName, cancellationToken);
                count++;
            }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
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

    private static async Task VerifyOriginalsAsync(string path, IEnumerable<SnapshotOriginal> originals, CancellationToken cancellationToken)
    {
        await using var archive = await ZipFile.OpenReadAsync(path, cancellationToken);
        foreach (var original in originals)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = archive.GetEntry(BackupArchive.MediaFolder + original.Key);
            if (entry is not null && entry.Length == original.Size)
            {
                await using var stream = await entry.OpenAsync(cancellationToken);
                var hash = await SHA256.HashDataAsync(stream, cancellationToken);
                if (string.Equals(Convert.ToHexStringLower(hash), original.Checksum, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            throw new CommandException(TransferCommandErrorCodes.IncompleteBackupMedia,
                $"The archive does not contain the local original '{PrintableText.Escape(original.Key)}' with the bytes named by the database snapshot. Check the media file and retry the backup.");
        }
    }
}
