using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FluentCMS.Admin.Api;
using FluentCMS.Admin.Configuration;

namespace FluentCMS.Admin.RichText;

// What the editor shows of an asset: its preview, or a placeholder when the media plugin is not enabled in the site or the
// asset is gone. The JSON names are those the module reads.
public sealed record MediaPreview(string State, string? Preview, string? Name, string? Alt)
{
    public const string Ready = "ready";
    public const string Missing = "missing";
    public const string Unavailable = "unavailable";

    public static MediaPreview Gone { get; } = new(Missing, null, null, null);

    public static MediaPreview Disabled { get; } = new(Unavailable, null, null, null);
}

// An entry a link of the document goes to, as the module reports it.
public sealed record EntryReference(string Id, string Type);

// Reads what the editor shows of the media and entries a document refers to, through the Management API: GET /media/{id}
// for an asset's URLs (#3100) and a list of the entries of a type by their ids for their titles. Neither is part of the value.
internal sealed class RichTextReferences(IManagementApi api, AdminConfiguration? configuration)
{
    public async Task<MediaPreview> ReadMediaAsync(Guid id, string? locale, CancellationToken cancellationToken)
    {
        try
        {
            var request = new ApiRequest(HttpMethod.Get, $"media/{id:D}") { Locale = locale };
            var asset = (await api.SendAsync(request, RichTextJsonContext.Default.JsonObject, cancellationToken)).Value;
            var urls = asset["urls"] as JsonObject;
            var mimeType = Text(asset, "mimeType") ?? "";
            var preview = Text(urls, "thumbnail") ?? (mimeType.StartsWith("image/", StringComparison.Ordinal) ? Text(urls, "original") : null);
            var name = Text(asset, "title") is { Length: > 0 } title ? title : Text(asset, "fileName");
            return new MediaPreview(MediaPreview.Ready, preview is null ? null : Resolve(preview), name, Text(asset, "alt"));
        }
        catch (ApiProblemException problem) when (problem.Problem.Status == (int)HttpStatusCode.NotFound)
        {
            return MediaPreview.Gone;
        }
    }

    // The titles of the entries of one type that the caller may read, by id; the others are left out.
    public async Task<IReadOnlyDictionary<Guid, string>> ReadTitlesAsync(SchemaTypeDescription type, IReadOnlyCollection<Guid> ids, string? locale, CancellationToken cancellationToken)
    {
        Dictionary<Guid, string> titles = [];
        var label = LabelField(type);
        foreach (var chunk in ids.Chunk(100))
        {
            var fields = label is null ? "id" : $"id,{label}";
            var path = string.Create(CultureInfo.InvariantCulture,
                $"content/{type.Name}?filter[id][in]={string.Join(',', chunk.Select(id => id.ToString("D")))}&fields={fields}&pageSize={chunk.Length}&withTotal=false");
            var request = new ApiRequest(HttpMethod.Get, path) { Locale = type.Localized ? locale : null };
            var page = (await api.SendAsync(request, RichTextJsonContext.Default.JsonObject, cancellationToken)).Value;
            foreach (var entry in (page["items"] as JsonArray ?? []).OfType<JsonObject>())
            {
                if (Guid.TryParse(Text(entry, "id"), out var entryId))
                {
                    titles[entryId] = label is not null && Text(entry, label) is { Length: > 0 } text ? text : entryId.ToString("D");
                }
            }
        }

        return titles;
    }

    // The entry's title field, or its first text field shown, as the admin labels an entry (spec 4.1).
    public static string? LabelField(SchemaTypeDescription type)
    {
        if (!string.IsNullOrEmpty(type.TitleField) && type.Fields.Any(field => field.Name == type.TitleField && !field.Hidden))
        {
            return type.TitleField;
        }

        return type.Fields.FirstOrDefault(field => field.Type == "text" && !field.Hidden)?.Name;
    }

    public static string? Title(SchemaTypeDescription type, ContentEntry entry) =>
        LabelField(type) is { } label && entry.Fields[label] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text)
            ? text
            : null;

    // The type's display name, or its name as words, as the admin names a type (spec 4.1).
    public static string TypeLabel(SchemaTypeDescription type)
    {
        if (!string.IsNullOrWhiteSpace(type.DisplayName))
        {
            return type.DisplayName;
        }

        var words = type.Name.Replace('-', ' ').Trim();
        return words.Length == 0 ? type.Name : char.ToUpperInvariant(words[0]) + words[1..];
    }

    // An address of the Management API made absolute against its base, as the media half does; none of another scheme.
    public string? Resolve(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute) && !url.StartsWith('/'))
        {
            return absolute.Scheme is "http" or "https" ? absolute.AbsoluteUri : null;
        }

        if (configuration is null)
        {
            return url;
        }

        var root = configuration.ApiBaseUrl.AbsoluteUri.EndsWith('/') ? configuration.ApiBaseUrl : new Uri(configuration.ApiBaseUrl.AbsoluteUri + "/");
        return new Uri(root, url.TrimStart('/')).AbsoluteUri;
    }

    private static string? Text(JsonObject? item, string name) =>
        item?[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}

[JsonSerializable(typeof(JsonObject))]
internal sealed partial class RichTextJsonContext : JsonSerializerContext;
