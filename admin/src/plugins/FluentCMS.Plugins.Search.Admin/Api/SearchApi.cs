using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FluentCMS.Admin.Api;

namespace FluentCMS.Plugins.Search.Admin.Api;

// The answer of a rebuild: the job that does it, which GET /jobs/{id} shows to a caller who holds jobs.manage for the instance.
internal sealed record SearchRebuildQueued
{
    public required Guid JobId { get; init; }
}

// The call of the half (spec 5.7), which needs search.manage in the site the shell shows. It is not about content, so it sends
// no content locale.
internal static class SearchApi
{
    // A type of null rebuilds every type with a searchable field.
    public static async Task<Guid> RebuildAsync(this IManagementApi api, string? type, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(api);
        var request = new ApiRequest(HttpMethod.Post, "search/rebuild") { Body = new JsonObject { ["type"] = type }, NoLocale = true };
        return (await api.SendAsync(request, SearchJsonContext.Default.SearchRebuildQueued, cancellationToken)).Value.JobId;
    }
}

// The answers of the route the page tells apart, by status and code only (spec 2.7).
internal static class SearchProblems
{
    public const string NotSearchable = "FCMS1700";
    public const string InvalidRebuild = "FCMS1701";

    public static bool IsFailure(Exception exception) =>
        exception is ApiProblemException or HttpRequestException or TaskCanceledException or JsonException;

    public static bool Has(Exception exception, string code) =>
        exception is ApiProblemException { Problem: var problem } && (problem.Code == code || problem.Errors.Any(error => error.Code == code));

    public static bool IsForbidden(Exception exception) => exception is ApiProblemException { Problem.Status: 403 };
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(SearchRebuildQueued))]
internal sealed partial class SearchJsonContext : JsonSerializerContext;
