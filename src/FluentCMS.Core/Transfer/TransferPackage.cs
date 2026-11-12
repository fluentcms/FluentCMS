using System.Text.Json;
using System.Text.Json.Serialization;
using FluentCMS.Core.Content;

namespace FluentCMS.Core.Transfer;

// The layout of a package (spec 4.14): a zip with manifest.json, schema.json, content/{type}.jsonl, settings.json, the
// optional roles.json and plugins/{contributor}/. Versions and references.jsonl are not in it; the manifest lists the
// sections a package has, so that a later format can add them.
internal static class TransferPackage
{
    public const int FormatVersion = 1;

    public const string Manifest = "manifest.json";
    public const string Schema = "schema.json";
    public const string Settings = "settings.json";
    public const string Roles = "roles.json";
    public const string ContentFolder = "content/";
    public const string PluginsFolder = "plugins/";
    public const string LinesExtension = ".jsonl";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static readonly ReadOnlyMemory<byte> NewLine = "\n"u8.ToArray();

    public static string ContentPath(string type) => ContentFolder + type + LinesExtension;

    public static string PluginFolder(string contributor) => PluginsFolder + contributor + "/";

    // A path a contributor names inside its folder: relative, with forward slashes, and never out of the folder.
    public static string? CheckPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\\', StringComparison.Ordinal) || path.StartsWith('/') || path.Contains(':', StringComparison.Ordinal))
        {
            return null;
        }

        var segments = path.Split('/');
        return segments.Any(segment => segment is "" or "." or "..") ? null : path;
    }
}

internal sealed record TransferManifest(
    int FormatVersion,
    string FluentCmsVersion,
    string SchemaHash,
    TransferSite Site,
    IReadOnlyList<string> Locales,
    DateTimeOffset CreatedAt,
    TransferExportRequest Options,
    IReadOnlyList<string> Sections);

internal sealed record TransferSite(Guid Id, string Slug, string Name, string DefaultLocale);

// What an export is asked for, as its JSON body. Types left out is every content type; global types are in unless
// globalTypes is false; roles only when asked. Options are the contributors', such as mediaFiles.
internal sealed record TransferExportRequest
{
    public IReadOnlyList<string>? Types { get; init; }

    public bool GlobalTypes { get; init; } = true;

    public bool PublishedOnly { get; init; }

    public bool Roles { get; init; }

    public IReadOnlyDictionary<string, string>? Options { get; init; }
}

// What an import is asked for, from the query of its request. Types left out is every type of the package.
internal sealed record TransferImportRequest
{
    public bool DryRun { get; init; }

    public bool Overwrite { get; init; }

    public bool SkipMissingLocales { get; init; }

    public bool Webhooks { get; init; }

    public bool Roles { get; init; }

    public IReadOnlyList<string>? Types { get; init; }

    public IReadOnlyDictionary<string, string> Options { get; init; } = new Dictionary<string, string>();
}

// One line of content/{type}.jsonl.
internal sealed record TransferLine(Guid Id, IReadOnlyList<TransferEntryLocale> Locales)
{
    public static TransferLine Of(TransferEntry entry) => new(entry.Id, entry.Locales);

    public TransferEntry ToEntry() => new(Id, Locales);
}

// One document of settings.json: an area of the site, without its secrets.
internal sealed record TransferSetting(string Area, JsonElement Value);

// One role of roles.json.
internal sealed record TransferRole(Guid Id, string Name, string? Description, bool RequireTwoFactor, IReadOnlyList<TransferGrant> Grants);

internal sealed record TransferGrant(string Permission, string? Constraints);
