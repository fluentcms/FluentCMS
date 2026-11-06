using FluentCMS.Core.Content.Query;
using FluentCMS.Schema;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Api.Common;

// The type named by a route, asked about before the engine runs, so that the answer does not depend on the error it
// happens to give first.
internal static class TypeAccess
{
    public static void EnsureKnown(HttpContext httpContext, string type)
    {
        if (httpContext.RequestServices.GetRequiredService<SchemaModel>().FindContentType(type) is null)
        {
            throw RequestErrors.TypeNotFound(type);
        }
    }

    // The engine refuses an unreadable type as it refuses an unknown one; asking first makes both the same 404.
    public static async Task<SchemaType> EnsureReadableAsync(HttpContext httpContext, string type)
    {
        var services = httpContext.RequestServices;
        var operation = ApiRequest.Of(httpContext).Operation;
        var access = await services.GetRequiredService<IContentQueryAccess>().ForQueryAsync(operation.Site, operation.Locale, httpContext.RequestAborted);
        return services.GetRequiredService<SchemaModel>().FindContentType(type) is { } found && access.CanReadType(found)
            ? found
            : throw RequestErrors.TypeNotFound(type);
    }
}
