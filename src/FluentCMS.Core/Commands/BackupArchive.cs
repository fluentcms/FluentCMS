using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using FluentCMS.Core.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Commands;

// The archive of `fluentcms backup` (spec 4.15): a zip with backup.json, the snapshot of the SQLite database and the files
// of the local media storage under media/, as they lie under its root. It is no export package: it holds everything in the
// database, users, keys and the activity log included, and only `fluentcms restore` reads it.
internal static class BackupArchive
{
    public const int FormatVersion = 1;

    public const string Manifest = "backup.json";
    public const string Database = "database/fluentcms.db";
    public const string MediaFolder = "media/";
    public const string Guide = "docs/backups.md";

    // The media plugin registers the DirectoryInfo of its local storage's root under this key, and the name of that storage.
    public const string LocalMediaRootKey = "FluentCMS.Media.LocalRoot";
    public const string LocalStorage = "local";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    // The version of FluentCMS.Core in the host, without the build metadata.
    public static string HostVersion { get; } = WithoutMetadata(
        typeof(BackupArchive).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0");

    // The database file of the host, or the refusal of a host whose provider has none, which names the guide.
    public static (IDatabaseFile File, string Path) DatabaseOf(IServiceProvider services, string command)
    {
        var provider = services.GetRequiredService<IDataProvider>();
        if (provider is not IDatabaseFile file)
        {
            throw new CommandException(
                TransferCommandErrorCodes.BackupNotSupported,
                $"'fluentcms {command}' is for a host on SQLite, and this host runs on {provider.Name}, whose own tools back up and restore its database while it runs; {Guide} says how, and what else belongs to a backup.");
        }

        return file.FilePath is { } path
            ? (file, path)
            : throw new CommandException(TransferCommandErrorCodes.NoDatabaseFile, $"the SQLite database of this host is in memory or named by a URI, so 'fluentcms {command}' has no file to work on.");
    }

    // Null when the host has no local media storage, which is when the media plugin is not in it.
    public static DirectoryInfo? LocalMediaRoot(IServiceProvider services) => services.GetKeyedService<DirectoryInfo>(LocalMediaRootKey);

    // The path under the root of a file of the archive's media/, or null for a name that would leave the root.
    public static string? MediaPath(DirectoryInfo root, string entryName)
    {
        var relative = entryName[MediaFolder.Length..];
        if (relative.Length == 0 || relative.Contains('\\', StringComparison.Ordinal) || relative.Contains(':', StringComparison.Ordinal)
            || relative.StartsWith('/') || relative.Split('/').Any(segment => segment is "" or "." or ".."))
        {
            return null;
        }

        var full = Path.GetFullPath(Path.Combine(root.FullName, relative));
        return full.StartsWith(Path.TrimEndingDirectorySeparator(root.FullName) + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? full : null;
    }

    public static async Task<BackupManifest?> ReadManifestAsync(ZipArchive archive, CancellationToken cancellationToken)
    {
        if (archive.GetEntry(Manifest) is not { } entry)
        {
            return null;
        }

        try
        {
            await using var stream = await entry.OpenAsync(cancellationToken);
            return await JsonSerializer.DeserializeAsync<BackupManifest>(stream, Json, cancellationToken);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            return null;
        }
    }

    // SemVer precedence: the numbers, then a release above its pre-releases, whose identifiers compare one by one. Null when
    // either is no version.
    public static int? Compare(string left, string right)
    {
        if (Parse(left) is not { } a || Parse(right) is not { } b)
        {
            return null;
        }

        for (var i = 0; i < 3; i++)
        {
            if (a.Numbers[i] != b.Numbers[i])
            {
                return a.Numbers[i].CompareTo(b.Numbers[i]);
            }
        }

        if (a.Pre.Length == 0 || b.Pre.Length == 0)
        {
            return (a.Pre.Length == 0).CompareTo(b.Pre.Length == 0);
        }

        for (var i = 0; i < Math.Min(a.Pre.Length, b.Pre.Length); i++)
        {
            var numeric = (long.TryParse(a.Pre[i], NumberStyles.None, CultureInfo.InvariantCulture, out var x), long.TryParse(b.Pre[i], NumberStyles.None, CultureInfo.InvariantCulture, out var y));
            var order = numeric switch
            {
                (true, true) => x.CompareTo(y),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.CompareOrdinal(a.Pre[i], b.Pre[i]),
            };
            if (order != 0)
            {
                return Math.Sign(order);
            }
        }

        return Math.Sign(a.Pre.Length.CompareTo(b.Pre.Length));
    }

    private static (long[] Numbers, string[] Pre)? Parse(string text)
    {
        var version = WithoutMetadata(text.Trim());
        var dash = version.IndexOf('-', StringComparison.Ordinal);
        var core = (dash < 0 ? version : version[..dash]).Split('.');
        var pre = dash < 0 ? [] : version[(dash + 1)..].Split('.');
        if (core.Length is < 1 or > 3 || pre.Any(identifier => identifier.Length == 0))
        {
            return null;
        }

        var numbers = new long[3];
        for (var i = 0; i < core.Length; i++)
        {
            if (!long.TryParse(core[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
            {
                return null;
            }
        }

        return (numbers, pre);
    }

    private static string WithoutMetadata(string version) => version.IndexOf('+', StringComparison.Ordinal) is var plus and >= 0 ? version[..plus] : version;
}

// backup.json. MediaStorage is the storage of new uploads, so a restore can say when the files are elsewhere.
internal sealed record BackupManifest(
    int FormatVersion,
    string FluentCmsVersion,
    string Provider,
    string SchemaHash,
    IReadOnlyList<BackupStream> Migrations,
    DateTimeOffset CreatedAt,
    string? MediaStorage,
    int MediaFiles);

internal sealed record BackupStream(string Name, IReadOnlyList<string> Applied);
