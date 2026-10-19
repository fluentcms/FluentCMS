using System.Globalization;
using System.Text;
using System.Text.Json;
using FluentCMS.Core.Agents;
using FluentCMS.Core.Content;
using FluentCMS.Core.Delivery;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FluentCMS.Api;

// The read-only delivery API (API-01). Anyone may read published content, so no endpoint asks who the caller is.
public static class DeliveryEndpoints
{
    public const string Prefix = "/api/v1";

    private const string Markdown = EntryMarkdown.MediaType + "; charset=utf-8";

    public static RouteGroupBuilder MapFluentCmsApi(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup(Prefix).AllowAnonymous().RequireRateLimiting(DeliveryRateLimit.PolicyName);
        api.MapGet("/content/{type}", ListAsync);
        api.MapGet("/entries/{id:guid}", GetAsync);
        api.MapGet("/entries/{id:guid}.md", GetMarkdownAsync);
        api.MapGet("/index", IndexAsync);
        api.MapGet("/index.md", IndexMarkdownAsync);
        api.MapGet("/preview/entries/{id:guid}", PreviewEntryAsync);
        api.MapGet("/preview/changes", PreviewChangesAsync);

        // Where agents look for a site's index first, by the llms.txt convention.
        endpoints.MapGet("/llms.txt", IndexMarkdownAsync).AllowAnonymous().RequireRateLimiting(DeliveryRateLimit.PolicyName);
        return api;
    }

    public static string EntryUrl(Guid id)
    {
        return $"{Prefix}/entries/{id}";
    }

    public static string MarkdownUrl(Guid id)
    {
        return $"{Prefix}/entries/{id}.md";
    }

    private static async Task<IResult> ListAsync(
        string type,
        string? limit,
        string? after,
        string? references,
        IPublishedContent content,
        CancellationToken cancellationToken)
    {
        // Not a slug, so no content type has it, and the database is not asked.
        if (!SchemaNames.IsSlug(type))
        {
            return Problem([new Error(SchemaErrorCodes.ContentTypeNotFound, $"There is no content type '{type}' in this site.")]);
        }

        var errors = new List<Error>();
        var size = PublishedQuery.DefaultLimit;
        if (limit is not null && !int.TryParse(limit, NumberStyles.None, CultureInfo.InvariantCulture, out size))
        {
            errors.Add(new Error(DeliveryErrorCodes.LimitInvalid, $"'limit' is '{limit}'; it must be a whole number from 1 to {PublishedQuery.MaxLimit}."));
        }

        var cursor = Id(after, "after", "the next cursor of a previous page", errors);
        var target = Id(references, "references", "the id of an entry, entity or media asset", errors);
        if (errors.Count > 0)
        {
            return Problem(errors);
        }

        var page = await content.ListAsync(new PublishedQuery(type, size, cursor, target), cancellationToken);
        return page.IsSuccess
            ? Results.Json(new PageResponse([.. page.Value.Items.Select(Item)], page.Value.Next))
            : Problem(page.Errors);
    }

    // Unpublished content is never cached on the way, so it disappears with the token (API-04).
    private static async Task<IResult> PreviewEntryAsync(Guid id, HttpContext http, IPreviewContent preview, CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        var item = await preview.GetAsync(BearerToken(http), id, cancellationToken);
        return item.IsSuccess ? Results.Json(Item(item.Value)) : Problem(item.Errors);
    }

    private static async Task<IResult> PreviewChangesAsync(HttpContext http, IPreviewContent preview, CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        var changes = await preview.ChangesAsync(BearerToken(http), cancellationToken);
        return changes.IsSuccess
            ? Results.Json(new ChangesResponse(changes.Value.ChangeSetId, [.. changes.Value.Items.Select(Item)]))
            : Problem(changes.Errors);
    }

    // The token travels in the Authorization header, never in the URL, so it stays out of logs and browser history.
    private static string BearerToken(HttpContext http)
    {
        var header = http.Request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header["Bearer ".Length..].Trim() : string.Empty;
    }

    private static async Task<IResult> GetAsync(Guid id, IPublishedContent content, CancellationToken cancellationToken)
    {
        var item = await content.GetAsync(id, cancellationToken);
        return item.IsSuccess ? Results.Json(Item(item.Value)) : Problem(item.Errors);
    }

    // The URL is the entry's id, so it stays the same across versions and always shows the published one (API-03).
    private static async Task<IResult> GetMarkdownAsync(Guid id, IPublishedContent content, CancellationToken cancellationToken)
    {
        var item = await content.GetAsync(id, cancellationToken);
        return item.IsSuccess
            ? Results.Text(EntryMarkdown.Render(item.Value, MarkdownUrl(id), MarkdownUrl), Markdown)
            : Problem(item.Errors);
    }

    private static async Task<IResult> IndexAsync(IPublishedContent content, CancellationToken cancellationToken)
    {
        var index = await content.IndexAsync(cancellationToken);
        return Results.Json(new IndexResponse([.. index.Select(entry => new IndexEntryResponse(
            entry.Id,
            entry.ContentType,
            entry.Version,
            entry.Title,
            EntryUrl(entry.Id),
            MarkdownUrl(entry.Id)))]));
    }

    private static async Task<IResult> IndexMarkdownAsync(IPublishedContent content, CancellationToken cancellationToken)
    {
        var index = await content.IndexAsync(cancellationToken);
        var markdown = new StringBuilder()
            .Append("# Site index\n\n")
            .Append(CultureInfo.InvariantCulture, $"Every published entry of this site, by content type, linked to its Markdown. The same index in JSON is at {Prefix}/index.\n");

        foreach (var type in index.GroupBy(entry => (entry.ContentType, entry.ContentTypeName)))
        {
            markdown.Append(CultureInfo.InvariantCulture, $"\n## {type.Key.ContentTypeName}\n\n");
            foreach (var entry in type)
            {
                var title = (entry.Title ?? $"{entry.ContentTypeName} {entry.Id}").Replace("[", @"\[", StringComparison.Ordinal).Replace("]", @"\]", StringComparison.Ordinal);
                markdown.Append(CultureInfo.InvariantCulture, $"- [{title}]({MarkdownUrl(entry.Id)})\n");
            }
        }

        return Results.Text(markdown.ToString(), Markdown);
    }

    private static Guid? Id(string? value, string name, string form, List<Error> errors)
    {
        if (value is null)
        {
            return null;
        }

        if (Guid.TryParse(value, out var id))
        {
            return id;
        }

        errors.Add(new Error(DeliveryErrorCodes.ParameterInvalid, $"'{name}' is '{value}'; it must be {form}, a UUID."));
        return null;
    }

    private static ItemResponse Item(PublishedItem item)
    {
        return new ItemResponse(
            item.Id,
            item.ContentType,
            item.Version,
            item.Fields,
            MarkdownUrl(item.Id),
            [.. item.Unresolved.Select(reference => new UnresolvedResponse(
                reference.Path,
                reference.EntityId,
                reference.Field,
                JsonNamingPolicy.CamelCase.ConvertName(reference.Reason.ToString())))]);
    }

    // RFC 9457 problem details with every error, so a body is always written and the admin's not-found page never answers instead.
    private static IResult Problem(IReadOnlyList<Error> errors)
    {
        var status = errors[0].Code switch
        {
            ContentErrorCodes.EntryNotFound or SchemaErrorCodes.ContentTypeNotFound => StatusCodes.Status404NotFound,
            AgentPermissionErrorCodes.ScopeDenied => StatusCodes.Status403Forbidden,
            DeliveryErrorCodes.PreviewTokenInvalid => StatusCodes.Status401Unauthorized,
            _ => StatusCodes.Status400BadRequest,
        };

        return Results.Problem(
            statusCode: status,
            detail: string.Join(" ", errors.Select(error => error.Message)),
            extensions: new Dictionary<string, object?>
            {
                ["errors"] = errors.Select(error => new { error.Code, error.Message }).ToList(),
            });
    }

    private sealed record PageResponse(IReadOnlyList<ItemResponse> Items, Guid? Next);

    private sealed record ChangesResponse(Guid ChangeSet, IReadOnlyList<ItemResponse> Items);

    private sealed record ItemResponse(Guid Id, string Type, int Version, JsonElement Fields, string Markdown, IReadOnlyList<UnresolvedResponse> Unresolved);

    private sealed record IndexResponse(IReadOnlyList<IndexEntryResponse> Items);

    private sealed record IndexEntryResponse(Guid Id, string Type, int Version, string? Title, string Url, string Markdown);

    private sealed record UnresolvedResponse(string Path, Guid EntityId, string Field, string Reason);
}
