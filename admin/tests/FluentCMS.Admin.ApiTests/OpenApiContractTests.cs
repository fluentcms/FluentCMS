using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using FluentCMS.Admin.Api;
using FluentCMS.Plugins.Media.Admin.Api;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.ApiTests;

// The admin's records are its own (AQ6), so this holds them to the reference host's OpenAPI document (spec 7): every member
// a record reads is in the schema of the answer it reads, and every member it requires the schema requires too.
public sealed class OpenApiContractTests
{
    // Members the admin reads before the backend sends them, each with the issue that adds them; one that is in the document
    // is a gap, so the pull request of that issue removes its entry.
    private static readonly Dictionary<string, string> Awaited = new(StringComparer.Ordinal)
    {
        ["schema.types[].previewable"] = "#3432",
    };

    [Fact]
    public async Task EveryRecordTheAdminReadsMatchesTheOpenApiDocument()
    {
        var document = await OpenApiDocument.ReadAsync();
        var paths = document["paths"]!;
        List<string> gaps = [];

        Check(ManagementJsonContext.Default.MeResponse, Answer(paths, "/api/management/v1/me", "get"), "me", document, gaps);
        Check(ManagementJsonContext.Default.ItemListMySiteInfo, Answer(paths, "/api/management/v1/me/sites", "get"), "me/sites", document, gaps);
        Check(ManagementJsonContext.Default.MyAccessInfo, Answer(paths, "/api/management/v1/me/access", "get"), "me/access", document, gaps);
        Check(ManagementJsonContext.Default.SchemaDescription, Answer(paths, "/api/management/v1/schema", "get"), "schema", document, gaps);
        Check(ManagementJsonContext.Default.ItemListPluginInfo, Answer(paths, "/api/management/v1/plugins", "get"), "plugins", document, gaps);
        Check(ManagementJsonContext.Default.ContentPage, Answer(paths, "/api/management/v1/content/article", "get"), "content/article", document, gaps);
        Check(ManagementJsonContext.Default.ContentEntry, Answer(paths, "/api/management/v1/content/article/{id}", "get"), "content/article/{id}", document, gaps);
        Check(ManagementJsonContext.Default.ContentEntry, Answer(paths, "/api/management/v1/content/article", "post", "201"), "content/article post", document, gaps);
        Check(ManagementJsonContext.Default.ItemListEntryVersionInfo, Answer(paths, "/api/management/v1/content/article/{id}/versions", "get"), "content/article/{id}/versions", document, gaps);
        Check(ManagementJsonContext.Default.ContentEntry, Answer(paths, "/api/management/v1/content/article/{id}/versions/{number}", "get"), "content/article/{id}/versions/{number}", document, gaps);
        Check(ManagementJsonContext.Default.VersionDiffInfo, Answer(paths, "/api/management/v1/content/article/{id}/versions/{from}/diff/{to}", "get"), "content/article/{id}/versions/{from}/diff/{to}", document, gaps);
        Check(ManagementJsonContext.Default.ContentEntry, Answer(paths, "/api/management/v1/content/article/{id}/versions/{number}/restore", "post"), "content/article/{id}/versions/{number}/restore", document, gaps);
        Check(ManagementJsonContext.Default.ItemListMySession, Answer(paths, "/api/management/v1/me/sessions", "get"), "me/sessions", document, gaps);
        Check(ManagementJsonContext.Default.MyTwoFactor, Answer(paths, "/api/management/v1/me/two-factor", "get"), "me/two-factor", document, gaps);
        Check(ManagementJsonContext.Default.AuthenticatorSetup, Answer(paths, "/api/management/v1/me/two-factor/enrol", "post"), "me/two-factor/enrol", document, gaps);
        Check(ManagementJsonContext.Default.RecoveryCodeList, Answer(paths, "/api/management/v1/me/two-factor/verify", "post"), "me/two-factor/verify", document, gaps);
        Check(ManagementJsonContext.Default.RecoveryCodeList, Answer(paths, "/api/management/v1/me/two-factor/recovery-codes", "post"), "me/two-factor/recovery-codes", document, gaps);
        Check(ManagementJsonContext.Default.ItemListMyLogin, Answer(paths, "/api/management/v1/me/logins", "get"), "me/logins", document, gaps);
        Check(ManagementJsonContext.Default.MyPreferences, Answer(paths, "/api/management/v1/me/preferences", "get"), "me/preferences", document, gaps);
        Check(ManagementJsonContext.Default.MyPreferences, Answer(paths, "/api/management/v1/me/preferences", "put"), "me/preferences put", document, gaps);
        Check(ManagementJsonContext.Default.ApiProblem, document["components"]!["schemas"]!["problem"]!, "problem", document, gaps);

        gaps.ShouldBeEmpty();
    }

    // The media half's records are its own too (spec 5.1), held to the media routes of the same document (#3104).
    [Fact]
    public async Task EveryRecordTheMediaHalfReadsMatchesTheOpenApiDocument()
    {
        var document = await OpenApiDocument.ReadAsync();
        var paths = document["paths"]!;
        var media = MediaJsonContext.Default;
        List<string> gaps = [];

        Check(media.MediaAssetPage, Answer(paths, "/api/management/v1/media", "get"), "media", document, gaps);
        Check(media.ItemListMediaAssetSaved, Answer(paths, "/api/management/v1/media", "post", "201"), "media post", document, gaps);
        Check(media.MediaAssetSaved, Answer(paths, "/api/management/v1/media/{id}/move", "post"), "media/{id}/move", document, gaps);
        Check(media.MediaAssetInfo, Answer(paths, "/api/management/v1/media/{id}", "get"), "media/{id}", document, gaps);

        // A media field as a list populates it, which the field's cell reads: the alternative of the field with the file name.
        var hero = Resolve(document["components"]!["schemas"]!["entry-article"], document)!["properties"]!["hero"]!;
        var populated = hero["anyOf"]!.AsArray().Single(alternative => Resolve(alternative, document)?["properties"]?["fileName"] is not null)!;
        Check(media.MediaFieldAsset, populated, "article.hero", document, gaps);
        Check(media.ItemListMediaFolderInfo, Answer(paths, "/api/management/v1/media/folders", "get"), "media/folders", document, gaps);
        Check(media.MediaFolderInfo, Answer(paths, "/api/management/v1/media/folders", "post", "201"), "media/folders post", document, gaps);
        Check(media.MediaFolderInfo, Answer(paths, "/api/management/v1/media/folders/{id}", "patch"), "media/folders/{id}", document, gaps);
        Check(media.MediaFolderInfo, Answer(paths, "/api/management/v1/media/folders/{id}/move", "post"), "media/folders/{id}/move", document, gaps);
        Check(media.MediaAssetInfo, Answer(paths, "/api/management/v1/media/{id}", "get"), "media/{id}", document, gaps);
        Check(media.MediaAssetChanged, Answer(paths, "/api/management/v1/media/{id}", "patch"), "media/{id} patch", document, gaps);
        Check(media.MediaAssetChanged, Answer(paths, "/api/management/v1/media/{id}/replace", "post"), "media/{id}/replace", document, gaps);
        Check(media.ItemListMediaUsageInfo, Answer(paths, "/api/management/v1/media/{id}/usage", "get"), "media/{id}/usage", document, gaps);
        Check(media.MediaDownloadInfo, Answer(paths, "/api/management/v1/media/{id}/download-url", "post"), "media/{id}/download-url", document, gaps);
        Check(media.ItemListMediaPresetInfo, Answer(paths, "/api/management/v1/media/presets", "get"), "media/presets", document, gaps);
        Check(media.MediaPresetInfo, Answer(paths, "/api/management/v1/media/presets/{name}", "put"), "media/presets/{name}", document, gaps);
        Check(media.MediaSchema, Answer(paths, "/api/management/v1/schema", "get"), "schema", document, gaps);

        var body = paths["/api/management/v1/media"]!["post"]!["requestBody"]!["content"]!["multipart/form-data"]!["schema"]!;
        var properties = Resolve(body, document)!["properties"]!.AsObject();
        properties.ContainsKey("files").ShouldBeTrue();
        properties.ContainsKey("folderId").ShouldBeTrue();
        paths["/api/management/v1/media/folders/{id}"]!["delete"].ShouldNotBeNull();
        paths["/api/management/v1/media/{id}"]!["delete"].ShouldNotBeNull();
        paths["/api/management/v1/media/presets/{name}"]!["delete"].ShouldNotBeNull();
        var patch = Resolve(paths["/api/management/v1/media/{id}"]!["patch"]!["requestBody"]!["content"]!["application/json"]!["schema"], document)!["properties"]!.AsObject();
        foreach (var name in (string[])["title", "alt", "caption", "credit", "tags", "focalPoint", "isPrivate"])
        {
            patch.ContainsKey(name).ShouldBeTrue(name);
        }

        var replace = Resolve(paths["/api/management/v1/media/{id}/replace"]!["post"]!["requestBody"]!["content"]!["multipart/form-data"]!["schema"], document)!["properties"]!.AsObject();
        replace.ContainsKey("file").ShouldBeTrue();

        // A read gives the version as the ETag and a save of the metadata sends it back as If-Match (#3333).
        paths["/api/management/v1/media/{id}"]!["get"]!["responses"]!["200"]!["headers"]!["ETag"].ShouldNotBeNull();
        paths["/api/management/v1/media/{id}"]!["patch"]!["responses"]!["200"]!["headers"]!["ETag"].ShouldNotBeNull();
        paths["/api/management/v1/media/{id}"]!["patch"]!["parameters"]!.AsArray()
            .Select(parameter => parameter!["$ref"]?.GetValue<string>())
            .ShouldContain("#/components/parameters/if-match-optional");

        gaps.ShouldBeEmpty();
    }

    [Fact]
    public async Task TheHeadersTheClientSendsAreTheOnesTheDocumentNames()
    {
        var document = await OpenApiDocument.ReadAsync();
        var parameters = document["components"]!["parameters"]!.AsObject();

        string Header(string name) => parameters[name]!["name"]!.GetValue<string>();

        Header("site").ShouldBe("site");
        Header("if-match").ShouldBe(ManagementHeaders.IfMatch);
        Header("idempotency-key").ShouldBe(ManagementHeaders.IdempotencyKey);
        parameters["site"]!["description"]!.GetValue<string>().ShouldContain(ManagementHeaders.Site);
        parameters["locale"]!["description"]!.GetValue<string>().ShouldContain(ManagementHeaders.Locale);
        parameters["if-match"]!["description"]!.GetValue<string>().ShouldContain(ApiRequest.NewLocale);
    }

    private static JsonNode Answer(JsonNode paths, string path, string method, string status = "200") =>
        paths[path]?[method]?["responses"]?[status]?["content"]?["application/json"]?["schema"]
        ?? throw new InvalidOperationException($"{method} {path} answers no {status} in the document.");

    private static void Check(JsonTypeInfo record, JsonNode schema, string where, JsonObject document, List<string> gaps) =>
        Check(record, schema, where, document, gaps, []);

    // A record is checked where it first comes, so that one inside itself, as a condition of conditions, ends.
    private static void Check(JsonTypeInfo record, JsonNode schema, string where, JsonObject document, List<string> gaps, HashSet<Type> checkedRecords)
    {
        var resolved = Resolve(schema, document);
        if (record.Type == typeof(ContentEntry))
        {
            CheckEntry(resolved, where, gaps);
            return;
        }

        switch (record.Kind)
        {
            case JsonTypeInfoKind.Object when checkedRecords.Add(record.Type):
                if (resolved?["properties"] is not JsonObject properties)
                {
                    gaps.Add($"{where} is not an object in the document");
                    return;
                }

                var required = resolved["required"]?.AsArray().Select(name => name!.GetValue<string>()).ToHashSet(StringComparer.Ordinal) ?? [];
                // A member with no setter is computed by the record and never read from an answer.
                foreach (var property in record.Properties.Where(property => property.Set is not null))
                {
                    var path = $"{where}.{property.Name}";
                    if (Awaited.TryGetValue(path, out var issue))
                    {
                        if (properties[property.Name] is not null)
                        {
                            gaps.Add($"{path} is in the document: {issue} is merged, so it is awaited no longer");
                        }

                        continue;
                    }

                    if (properties[property.Name] is not { } member)
                    {
                        gaps.Add($"{path} is not in the document");
                        continue;
                    }

                    if (property.IsRequired && !required.Contains(property.Name))
                    {
                        gaps.Add($"{path} is required by the admin but not by the document");
                    }

                    Check(record.Options.GetTypeInfo(property.PropertyType), member, path, document, gaps, checkedRecords);
                }

                break;
            case JsonTypeInfoKind.Enumerable when record.ElementType is { } element:
                if (resolved?["items"] is not { } items)
                {
                    gaps.Add($"{where} is not an array in the document");
                    return;
                }

                Check(record.Options.GetTypeInfo(element), items, $"{where}[]", document, gaps, checkedRecords);
                break;
            case JsonTypeInfoKind.Dictionary when record.ElementType is { } value:
                if (resolved?["additionalProperties"] is not JsonObject values)
                {
                    gaps.Add($"{where} is not a map in the document");
                    return;
                }

                Check(record.Options.GetTypeInfo(value), values, $"{where}{{}}", document, gaps, checkedRecords);
                break;
        }
    }

    // An entry is read by its own converter: its system members are in the schema of the type's entries.
    private static void CheckEntry(JsonNode? schema, string where, List<string> gaps)
    {
        var properties = schema?["properties"] as JsonObject;
        var members = typeof(ContentEntry).GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.Name != nameof(ContentEntry.Fields))
            .Select(property => JsonNamingPolicy.CamelCase.ConvertName(property.Name));
        foreach (var name in members)
        {
            if (properties?[name] is null)
            {
                gaps.Add($"{where}.{name} is not in the document");
            }
        }
    }

    // A $ref, or the one alternative of a nullable oneOf that is not null.
    private static JsonNode? Resolve(JsonNode? schema, JsonObject document)
    {
        while (true)
        {
            if (schema?["$ref"]?.GetValue<string>() is { } reference)
            {
                schema = document["components"]!["schemas"]![reference["#/components/schemas/".Length..]];
                continue;
            }

            if (schema?["oneOf"] is JsonArray alternatives)
            {
                schema = alternatives.Single(alternative => alternative?["type"]?.GetValue<string>() != "null");
                continue;
            }

            return schema;
        }
    }
}
