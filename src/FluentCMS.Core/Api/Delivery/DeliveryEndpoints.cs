using FluentCMS.Content;
using FluentCMS.Core.Api.Common;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Content.Query;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Schema;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Api.Delivery;

// The read-only routes of spec 4.10. Everything a principal may see is the engine's decision: an anonymous request is
// the Public role of its site. Drafts are the one rule of this API's own: only the preview and draft scopes read them
// here, where the engine would also let a managing principal.
internal static class DeliveryEndpoints
{
    public static IServiceCollection AddDeliveryApi(this IServiceCollection services) =>
        services.AddFluentCmsEndpoints(FluentCmsEndpointGroup.Delivery, Map);

    private static void Map(RouteGroupBuilder delivery)
    {
        var routes = delivery.MapGroup("").WithFluentCmsOperation(Origin.Api);
        routes.MapGet("/{type}", ListAsync);
        routes.MapGet("/{type}/{id}", GetAsync);
        routes.MapGet("/{type}/by/{field}/{value}", GetByFieldAsync);
    }

    private static Task<IResult> ListAsync(string type, HttpContext httpContext, [FromServices] IContentService content, [FromServices] SchemaModel schema) =>
        RunAsync(httpContext, type, byField: null, async () =>
        {
            var api = ApiRequest.Of(httpContext);
            if (schema.FindContentType(type) is { Kind: SchemaTypeKind.Singleton })
            {
                var query = Parse(httpContext, api, single: true) with { Page = new PageRequest { Size = 1, WithTotal = false } };
                var found = await content.QueryAsync(type, query, httpContext.RequestAborted);
                return found.Items.Count > 0 ? Results.Json(ContentJson.Entry(found.Items[0])) : throw NotFound("type", $"The {type} singleton has no entry.");
            }

            var result = await content.QueryAsync(type, Parse(httpContext, api, single: false), httpContext.RequestAborted);
            return Results.Json(ContentJson.List(result));
        });

    private static Task<IResult> GetAsync(string type, string id, HttpContext httpContext, [FromServices] IContentService content) =>
        RunAsync(httpContext, type, byField: null, async () =>
        {
            var query = Parse(httpContext, ApiRequest.Of(httpContext), single: true);
            if (!Guid.TryParseExact(id, "D", out var entryId))
            {
                throw new ValidationException([new ValidationError("id", QueryErrorCodes.InvalidValue, $"'{id}' is not an entry id.")]);
            }

            var entry = await content.GetAsync(type, entryId, RequestQuery.Options(query), httpContext.RequestAborted);
            return entry is not null ? Results.Json(ContentJson.Entry(entry)) : throw new EntryNotFoundException(type, entryId);
        });

    // The query runs before the field is checked, so that a type or field the principal cannot read is refused the way
    // an unknown one is, and a non-unique field is reported only to a principal that may filter on it.
    private static Task<IResult> GetByFieldAsync(string type, string field, string value, HttpContext httpContext, [FromServices] IContentService content, [FromServices] SchemaModel schema) =>
        RunAsync(httpContext, type, field, async () =>
        {
            var query = Parse(httpContext, ApiRequest.Of(httpContext), single: true) with
            {
                Filter = new FieldFilter(field, FilterOperator.Equal, value),
                Page = new PageRequest { Size = 1, WithTotal = false },
            };

            var found = await content.QueryAsync(type, query, httpContext.RequestAborted);
            if (schema.FindContentType(type)?.FindField(field) is not { Unique: true })
            {
                throw new ValidationException([new ValidationError(RequestErrors.Field, DeliveryErrorCodes.FieldNotUnique, $"'{field}' is not a unique field of {type}.")]);
            }

            return found.Items.Count > 0
                ? Results.Json(ContentJson.Entry(found.Items[0]))
                : throw NotFound(RequestErrors.Value, $"There is no {type} whose {field} is '{value}'.");
        });

    private static async Task<IResult> RunAsync(HttpContext httpContext, string type, string? byField, Func<Task<IResult>> handler)
    {
        try
        {
            return await handler();
        }
        catch (ValidationException exception)
        {
            throw RequestErrors.ForRequest(exception, httpContext.Request, type, byField);
        }
    }

    private static ContentQuery Parse(HttpContext httpContext, ApiRequest api, bool single)
    {
        var query = RequestQuery.Parse(httpContext.Request, api, single);
        if (query.Status == QueryStatus.Draft && !ReadsDrafts(api.Operation.Principal))
        {
            throw new ValidationException([new ValidationError("status", QueryErrorCodes.DraftNotAllowed, "Delivery reads drafts with a preview key or the draft scope.")]);
        }

        return query;
    }

    private static bool ReadsDrafts(Principal principal) =>
        (principal.Claim(TokenScopes.Claim) ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries) is var scopes
        && (scopes.Contains(TokenScopes.Preview, StringComparer.Ordinal) || scopes.Contains(TokenScopes.Draft, StringComparer.Ordinal));

    private static ValidationException NotFound(string path, string message) => new([new ValidationError(path, ApiErrorCodes.NotFound, message)]);
}
