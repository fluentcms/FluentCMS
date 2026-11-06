using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentCMS.Content;
using FluentCMS.Core.Api.Common;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Schema;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Api.Management;

// The schema group of spec 4.10. SchemaIntrospection checks schema.read and leaves out what the caller may not read, so a
// type it may not read is the same 404 as an unknown one, as TypeAccess answers on the content routes.
internal static class SchemaEndpoints
{
    private static readonly JsonSerializerOptions Options = new(AdminJson.Options)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static IServiceCollection AddManagementSchemaApi(this IServiceCollection services) =>
        services.AddFluentCmsEndpoints(FluentCmsEndpointGroup.Management, Map);

    private static void Map(RouteGroupBuilder management)
    {
        var schema = management.MapGroup("/schema").WithFluentCmsOperation(Origin.Api).RequireManagementScope();
        schema.MapGet("", DescribeAsync);
        schema.MapGet("/types/{type}", DescribeTypeAsync);
        schema.MapGet("/snapshot", SnapshotAsync);
    }

    private static async Task<IResult> DescribeAsync(HttpContext httpContext, [FromServices] SchemaIntrospection introspection)
    {
        ManagementQuery.Only(httpContext.Request);
        return Results.Json(await introspection.DescribeAsync(httpContext.RequestAborted), Options);
    }

    private static async Task<IResult> DescribeTypeAsync(string type, HttpContext httpContext, [FromServices] SchemaIntrospection introspection)
    {
        ManagementQuery.Only(httpContext.Request);
        var described = await introspection.DescribeTypeAsync(type, httpContext.RequestAborted) ?? throw RequestErrors.TypeNotFound(type);
        return Results.Json(described, Options);
    }

    // The body is the canonical text as written, so that its SHA-256 is the hash in the ETag.
    private static async Task<IResult> SnapshotAsync(HttpContext httpContext, [FromServices] SchemaIntrospection introspection)
    {
        ManagementQuery.Only(httpContext.Request);
        var snapshot = await introspection.SnapshotAsync(httpContext.RequestAborted);
        httpContext.Response.Headers.ETag = $"\"{snapshot.Hash}\"";
        return Results.Text(snapshot.Json, "application/json", Encoding.UTF8);
    }
}
