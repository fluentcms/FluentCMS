using System.Globalization;
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

    public static RouteGroupBuilder MapFluentCmsApi(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup(Prefix).AllowAnonymous();
        api.MapGet("/content/{type}", ListAsync);
        api.MapGet("/entries/{id}", GetAsync);

        // Ids are parsed by the handlers and every other path under the prefix is answered here, so no API call falls through to the
        // admin's not-found page.
        api.MapGet("/{**path}", (string? path) => Problem(
            [new Error(DeliveryErrorCodes.EndpointNotFound, $"There is no delivery endpoint at {Prefix}/{path}; see docs/api/delivery.md.")]));
        return api;
    }

    private static async Task<IResult> ListAsync(
        string type,
        string? limit,
        string? after,
        string? references,
        IPublishedContent content,
        CancellationToken cancellationToken)
    {
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

    private static async Task<IResult> GetAsync(string id, IPublishedContent content, CancellationToken cancellationToken)
    {
        var errors = new List<Error>();
        if (Id(id, "id", "the id of an entry", errors) is not { } entryId)
        {
            return Problem(errors);
        }

        var item = await content.GetAsync(entryId, cancellationToken);
        return item.IsSuccess ? Results.Json(Item(item.Value)) : Problem(item.Errors);
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
            ContentErrorCodes.EntryNotFound or SchemaErrorCodes.ContentTypeNotFound or DeliveryErrorCodes.EndpointNotFound => StatusCodes.Status404NotFound,
            AgentPermissionErrorCodes.ScopeDenied => StatusCodes.Status403Forbidden,
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

    private sealed record ItemResponse(Guid Id, string Type, int Version, JsonElement Fields, IReadOnlyList<UnresolvedResponse> Unresolved);

    private sealed record UnresolvedResponse(string Path, Guid EntityId, string Field, string Reason);
}
