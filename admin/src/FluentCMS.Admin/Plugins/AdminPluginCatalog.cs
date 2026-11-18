using System.Text.Json;
using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace FluentCMS.Admin.Plugins;

internal enum PluginProblemKind
{
    // The gathered file could not be read at all, so no half is loaded.
    Unreadable,

    // A manifest this version of the contract does not read, such as one of a later version.
    Invalid,

    // The half names no AdminRange, or one that does not hold the running FluentCMS.Admin.Abstractions.
    OutOfRange,

    // The half is named after a first segment of the admin's own routes (spec 3.2).
    ReservedName,

    // Two halves name the same server plugin.
    Duplicate,
}

// A half the shell does not load, which the page of plugin problems lists. Name and Assembly are null when unknown.
internal sealed record PluginProblem(PluginProblemKind Kind, string? Name = null, string? Assembly = null, string? AdminRange = null);

// The admin halves the app was built with, from the file its build gathers from their manifests (spec 2.6), read at start
// with one request. A half that is invalid, outside the admin range or otherwise unusable is left out and listed, and the
// others still load.
internal sealed class AdminPluginCatalog
{
    public const string FileName = "fluentcms-admin.plugins.json";

    // The first segments of the admin's own routes, which no plugin may be named after.
    private static readonly HashSet<string> ReservedSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        "content", "settings", "account", "users", "roles", "api-keys", "members", "sites", "plugins", "jobs", "system", "authentication",
    };

    public AdminPluginCatalog(IReadOnlyList<AdminPluginManifest> halves, IReadOnlyList<PluginProblem> problems)
    {
        Halves = halves;
        Problems = problems;
    }

    public static AdminPluginCatalog Empty { get; } = new([], []);

    // The running version of the contract, which a half's AdminRange must hold.
    public static Version RunningVersion { get; } = typeof(AdminPluginManifest).Assembly.GetName().Version ?? new Version(0, 0, 0);

    public IReadOnlyList<AdminPluginManifest> Halves { get; }

    public IReadOnlyList<PluginProblem> Problems { get; }

    // The half whose plugin's name is the first segment of a route relative to the app's base, if any.
    public AdminPluginManifest? HalfOfRoute(string route)
    {
        ArgumentNullException.ThrowIfNull(route);
        var end = route.IndexOfAny(['?', '#']);
        var path = (end < 0 ? route : route[..end]).TrimStart('/');
        var slash = path.IndexOf('/', StringComparison.Ordinal);
        var first = Uri.UnescapeDataString(slash < 0 ? path : path[..slash]);
        return first.Length == 0 ? null : Halves.FirstOrDefault(half => string.Equals(half.Name, first, StringComparison.OrdinalIgnoreCase));
    }

    // The file is the app's own and changes with each build, so it is read as config.json is, never from the cache. A file
    // the app does not have, as in an app built before halves were gathered, means no half.
    public static async Task<AdminPluginCatalog> LoadAsync(HttpClient http, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(http);

        using var request = new HttpRequestMessage(HttpMethod.Get, FileName);
        request.SetBrowserRequestCache(BrowserRequestCache.NoCache);
        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return Empty;
            }

            return response.IsSuccessStatusCode
                ? Read(await response.Content.ReadAsStringAsync(cancellationToken), RunningVersion)
                : new([], [new PluginProblem(PluginProblemKind.Unreadable)]);
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return new([], [new PluginProblem(PluginProblemKind.Unreadable)]);
        }
    }

    // Each manifest is read on its own, so that one this version cannot read leaves the others.
    public static AdminPluginCatalog Read(string json, Version running)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(running);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException)
        {
            return new([], [new PluginProblem(PluginProblemKind.Unreadable)]);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return new([], [new PluginProblem(PluginProblemKind.Unreadable)]);
            }

            var halves = new List<AdminPluginManifest>();
            var problems = new List<PluginProblem>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                AdminPluginManifest? manifest;
                try
                {
                    manifest = element.Deserialize(AdminPluginManifestJson.Manifest);
                }
                catch (JsonException)
                {
                    manifest = null;
                }

                if (manifest is null || !manifest.IsValid || string.IsNullOrWhiteSpace(manifest.Name) || string.IsNullOrWhiteSpace(manifest.Assembly))
                {
                    problems.Add(new PluginProblem(PluginProblemKind.Invalid, manifest?.Name ?? Member(element, "name"), manifest?.Assembly ?? Member(element, "assembly")));
                }
                else if (!AdminVersionRange.TryParse(manifest.AdminRange, out var range) || !range.Contains(running))
                {
                    problems.Add(new PluginProblem(PluginProblemKind.OutOfRange, manifest.Name, manifest.Assembly, manifest.AdminRange));
                }
                else if (ReservedSegments.Contains(manifest.Name))
                {
                    problems.Add(new PluginProblem(PluginProblemKind.ReservedName, manifest.Name, manifest.Assembly));
                }
                else if (halves.Any(half => string.Equals(half.Name, manifest.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    problems.Add(new PluginProblem(PluginProblemKind.Duplicate, manifest.Name, manifest.Assembly));
                }
                else
                {
                    halves.Add(manifest);
                }
            }

            return new(halves, problems);
        }
    }

    private static string? Member(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
