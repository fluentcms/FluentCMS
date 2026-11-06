using FluentCMS.Content;
using FluentCMS.Core.Api.Common;
using FluentCMS.Core.Content.Query;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Localization;
using FluentCMS.Publishing;
using FluentCMS.Schema;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Api.Management;

// The content group of spec 4.10: every operation of IContentService over HTTP, as the request's principal. The routes
// carry identity and intent only, and the content service decides the rest. Reads default to the draft, which is what
// an editor works on, and answer an unknown type and one the principal cannot read with the same 404, as Delivery does.
// A write leaves an unreadable type to the engine, since a principal may hold a write without the read.
internal static class ContentEndpoints
{
    private static readonly string Prefix = FluentCmsEndpointGroups.Prefixes.Single(item => item.Group == FluentCmsEndpointGroup.Management).Prefix;

    public static IServiceCollection AddManagementContentApi(this IServiceCollection services) =>
        services.AddFluentCmsEndpoints(FluentCmsEndpointGroup.Management, Map);

    private static void Map(RouteGroupBuilder management)
    {
        var content = management.MapGroup("/content/{type}").WithFluentCmsOperation(Origin.Api).RequireManagementScope();

        content.MapGet("", ListAsync);
        content.MapPost("", CreateAsync);
        content.MapPost("/bulk", BulkAsync);
        content.MapGet("/trash", ListTrashAsync);
        content.MapPost("/trash/{id}/restore", RestoreAsync);
        content.MapDelete("/trash/{id}", PurgeAsync);

        content.MapGet("/{id}", GetAsync);
        content.MapPut("/{id}", UpdateAsync);
        content.MapPatch("/{id}", PatchAsync);
        content.MapDelete("/{id}", DeleteAsync);

        content.MapPost("/{id}/publish", PublishAsync);
        content.MapPost("/{id}/unpublish", UnpublishAsync);
        content.MapPost("/{id}/discard", DiscardAsync);
        content.MapPost("/{id}/schedule", ScheduleAsync);
        content.MapDelete("/{id}/schedule/{action}", CancelScheduleAsync);

        content.MapGet("/{id}/versions", VersionsAsync);
        content.MapGet("/{id}/versions/{number}", VersionAsync);
        content.MapPost("/{id}/versions/{number}/restore", RestoreVersionAsync);
        content.MapGet("/{id}/versions/{from}/diff/{to}", DiffAsync);
    }

    private static Task<IResult> ListAsync(string type, HttpContext httpContext, [FromServices] IContentService content) =>
        ReadAsync(httpContext, type, async () =>
            Results.Json(ContentJson.List(await content.QueryAsync(type, Query(httpContext, single: false), httpContext.RequestAborted))));

    private static Task<IResult> GetAsync(string type, string id, HttpContext httpContext, [FromServices] IContentService content) =>
        ReadAsync(httpContext, type, async () =>
        {
            var options = RequestQuery.Options(Query(httpContext, single: true));
            var entryId = EntryId(id);
            var entry = await content.GetAsync(type, entryId, options, httpContext.RequestAborted) ?? throw new EntryNotFoundException(type, entryId);
            return Entry(httpContext, entry, versioned: options.Status == QueryStatus.Draft);
        });

    private static Task<IResult> CreateAsync(string type, HttpContext httpContext, [FromServices] IContentService content, [FromServices] SchemaModel schema) =>
        WriteAsync(httpContext, type, async () =>
        {
            var body = RequestBody.Document(await RequestBody.ObjectAsync(httpContext.Request));
            var created = await content.CreateAsync(type, body, httpContext.RequestAborted);

            var name = schema.FindContentType(type)!.Name;
            httpContext.Response.Headers.Location = $"{httpContext.Request.PathBase}{Prefix}/content/{Uri.EscapeDataString(name)}/{created.Get<Guid>("id"):D}";
            return Entry(httpContext, created, statusCode: StatusCodes.Status201Created);
        });

    private static Task<IResult> UpdateAsync(string type, string id, HttpContext httpContext, [FromServices] IContentService content) =>
        WriteAsync(httpContext, type, async () =>
        {
            var entryId = EntryId(id);
            var expected = IfMatch.Required(httpContext.Request);
            var body = RequestBody.Document(await RequestBody.ObjectAsync(httpContext.Request));
            return Entry(httpContext, await content.UpdateAsync(type, entryId, body, expected, httpContext.RequestAborted));
        });

    private static Task<IResult> PatchAsync(string type, string id, HttpContext httpContext, [FromServices] IContentService content) =>
        WriteAsync(httpContext, type, async () =>
        {
            var entryId = EntryId(id);
            var expected = IfMatch.Required(httpContext.Request);
            var patch = RequestBody.Document(await RequestBody.ObjectAsync(httpContext.Request));
            return Entry(httpContext, await content.PatchAsync(type, entryId, patch, expected, httpContext.RequestAborted));
        });

    private static Task<IResult> DeleteAsync(string type, string id, HttpContext httpContext, [FromServices] IContentService content) =>
        WriteAsync(httpContext, type, async () =>
        {
            await content.DeleteAsync(type, EntryId(id), httpContext.RequestAborted);
            return Results.NoContent();
        },
        oneLocale: false);

    private static Task<IResult> PublishAsync(string type, string id, HttpContext httpContext, [FromServices] IContentService content) =>
        WriteAsync(httpContext, type, async () =>
        {
            var entryId = EntryId(id);
            var result = await content.PublishAsync(type, entryId, locale: null, IfMatch.Optional(httpContext.Request), httpContext.RequestAborted);
            SetETag(httpContext, result.Entry);
            return Results.Json(ManagementJson.Published(result));
        });

    private static Task<IResult> UnpublishAsync(string type, string id, HttpContext httpContext, [FromServices] IContentService content) =>
        WriteAsync(httpContext, type, async () =>
            Entry(httpContext, await content.UnpublishAsync(type, EntryId(id), locale: null, httpContext.RequestAborted)));

    private static Task<IResult> DiscardAsync(string type, string id, HttpContext httpContext, [FromServices] IContentService content) =>
        WriteAsync(httpContext, type, async () =>
        {
            var entryId = EntryId(id);
            return Entry(httpContext, await content.DiscardDraftAsync(type, entryId, locale: null, IfMatch.Optional(httpContext.Request), httpContext.RequestAborted));
        });

    private static Task<IResult> ScheduleAsync(string type, string id, HttpContext httpContext, [FromServices] IContentService content) =>
        WriteAsync(httpContext, type, async () =>
        {
            var entryId = EntryId(id);
            var request = ContentRequests.Schedule(await RequestBody.ObjectAsync(httpContext.Request));
            return Results.Json(ManagementJson.Schedule(await content.ScheduleAsync(type, entryId, request, locale: null, httpContext.RequestAborted)));
        });

    private static Task<IResult> CancelScheduleAsync(string type, string id, string action, HttpContext httpContext, [FromServices] IContentService content) =>
        WriteAsync(httpContext, type, async () =>
        {
            var entryId = EntryId(id);
            await content.CancelScheduleAsync(type, entryId, ContentRequests.Action(action), locale: null, httpContext.RequestAborted);
            return Results.NoContent();
        });

    private static Task<IResult> VersionsAsync(string type, string id, HttpContext httpContext, [FromServices] IContentService content) =>
        VersionReadAsync(httpContext, type, async () =>
            Results.Json(ManagementJson.Versions(await content.GetVersionsAsync(type, EntryId(id), locale: null, httpContext.RequestAborted))));

    private static Task<IResult> VersionAsync(string type, string id, string number, HttpContext httpContext, [FromServices] IContentService content) =>
        VersionReadAsync(httpContext, type, async () =>
        {
            var entryId = EntryId(id);
            var version = Number(number, nameof(number));
            var found = await content.GetVersionAsync(type, entryId, version, locale: null, httpContext.RequestAborted)
                ?? throw new ValidationException([new ValidationError(nameof(number), ApiErrorCodes.NotFound, $"The entry has no version {version} in this locale.")]);
            return Results.Json(ContentJson.Entry(found));
        });

    private static Task<IResult> RestoreVersionAsync(string type, string id, string number, HttpContext httpContext, [FromServices] IContentService content) =>
        WriteAsync(httpContext, type, async () =>
        {
            var entryId = EntryId(id);
            var version = Number(number, nameof(number));
            var expected = IfMatch.Optional(httpContext.Request);
            return Entry(httpContext, await content.RestoreVersionAsync(type, entryId, version, locale: null, expected, httpContext.RequestAborted));
        });

    private static Task<IResult> DiffAsync(string type, string id, string from, string to, HttpContext httpContext, [FromServices] IContentService content) =>
        VersionReadAsync(httpContext, type, async () =>
        {
            var entryId = EntryId(id);
            var (earlier, later) = (Number(from, nameof(from)), Number(to, nameof(to)));
            var changes = await content.DiffAsync(type, entryId, earlier, later, locale: null, httpContext.RequestAborted);
            return Results.Json(ManagementJson.Diff(earlier, later, changes));
        });

    // The filter of a bulk request is the list's, in the query string; the rest is the body.
    private static Task<IResult> BulkAsync(string type, HttpContext httpContext, [FromServices] IContentService content) =>
        WriteAsync(httpContext, type, async () =>
        {
            var filter = ContentRequests.BulkFilter(httpContext.Request, ApiRequest.Of(httpContext));
            var request = ContentRequests.Bulk(await RequestBody.ObjectAsync(httpContext.Request), filter);
            var result = await content.BulkAsync(type, request, httpContext.RequestAborted);
            return Results.Json(ManagementJson.Bulk(result, httpContext.User.Identity?.IsAuthenticated == true));
        },
        bulk: true);

    private static Task<IResult> ListTrashAsync(string type, HttpContext httpContext, [FromServices] IContentService content) =>
        ReadAsync(httpContext, type, async () =>
            Results.Json(ContentJson.List(await content.QueryTrashAsync(type, Query(httpContext, single: false), httpContext.RequestAborted))));

    private static Task<IResult> RestoreAsync(string type, string id, HttpContext httpContext, [FromServices] IContentService content) =>
        WriteAsync(httpContext, type, async () => Entry(httpContext, await content.RestoreAsync(type, EntryId(id), httpContext.RequestAborted)), oneLocale: false);

    private static Task<IResult> PurgeAsync(string type, string id, HttpContext httpContext, [FromServices] IContentService content) =>
        WriteAsync(httpContext, type, async () =>
        {
            await content.PurgeAsync(type, EntryId(id), httpContext.RequestAborted);
            return Results.NoContent();
        },
        oneLocale: false);

    // A read with the list grammar, whose errors name the parameters the client sent.
    private static async Task<IResult> ReadAsync(HttpContext httpContext, string type, Func<Task<IResult>> handler)
    {
        await TypeAccess.EnsureReadableAsync(httpContext, type);
        try
        {
            return await handler();
        }
        catch (ValidationException exception)
        {
            throw RequestErrors.ForRequest(exception, httpContext.Request);
        }
    }

    // Versions are of one locale and take no query parameters but the site and the locale.
    private static async Task<IResult> VersionReadAsync(HttpContext httpContext, string type, Func<Task<IResult>> handler)
    {
        await TypeAccess.EnsureReadableAsync(httpContext, type);
        ContentRequests.OnlyParameters(httpContext.Request, filter: false);
        EnsureOneLocale(httpContext);
        return await handler();
    }

    // A bulk request checks its own parameters, since it takes a filter. Delete, restore and purge act on every locale.
    private static Task<IResult> WriteAsync(HttpContext httpContext, string type, Func<Task<IResult>> handler, bool bulk = false, bool oneLocale = true)
    {
        TypeAccess.EnsureKnown(httpContext, type);
        if (!bulk)
        {
            ContentRequests.OnlyParameters(httpContext.Request, filter: false);
        }

        if (oneLocale)
        {
            EnsureOneLocale(httpContext);
        }

        return handler();
    }

    // The bridge reads locale=all as the default locale, which a write would then change without saying so.
    private static void EnsureOneLocale(HttpContext httpContext)
    {
        if (ApiRequest.Of(httpContext).Locale == ILocaleResolver.All)
        {
            throw new ValidationException([new ValidationError(RequestBridge.LocaleParameter, QueryErrorCodes.InvalidParameter, "The request needs one locale.")]);
        }
    }

    private static ContentQuery Query(HttpContext httpContext, bool single)
    {
        var query = RequestQuery.Parse(httpContext.Request, ApiRequest.Of(httpContext), single);
        return query with { Status = query.Status ?? QueryStatus.Draft };
    }

    // An entry read as its draft carries its version as the ETag, which a client sends back as If-Match.
    private static IResult Entry(HttpContext httpContext, ContentDocument entry, bool versioned = true, int statusCode = StatusCodes.Status200OK)
    {
        if (versioned)
        {
            SetETag(httpContext, entry);
        }

        return Results.Json(ContentJson.Entry(entry), statusCode: statusCode);
    }

    // A read that took values through the fallback chain may carry another locale's version, which is no precondition for
    // this one; fallback=none reads what the locale holds.
    private static void SetETag(HttpContext httpContext, ContentDocument entry)
    {
        if (entry.TryGetValue("version", out var value) && value is int version && !entry.ContainsKey("fallbacks"))
        {
            IfMatch.SetETag(httpContext.Response, version);
        }
    }

    private static Guid EntryId(string id) =>
        Guid.TryParseExact(id, "D", out var entryId)
            ? entryId
            : throw new ValidationException([new ValidationError("id", QueryErrorCodes.InvalidValue, $"'{id}' is not an entry id.")]);

    private static int Number(string value, string path) =>
        int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number) && number > 0
            ? number
            : throw new ValidationException([new ValidationError(path, QueryErrorCodes.InvalidValue, $"'{value}' is not a version number.")]);
}
