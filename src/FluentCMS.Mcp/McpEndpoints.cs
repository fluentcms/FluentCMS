using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FluentCMS.Mcp;

public static class McpEndpoints
{
    public const string Route = "/mcp";

    public static IEndpointConventionBuilder MapFluentCmsMcp(this IEndpointRouteBuilder endpoints)
    {
        // Stateless mode maps POST alone. A client that opens a stream with GET must get 405 to learn there is none,
        // which a GET fallback elsewhere in the host would otherwise turn into a 404.
        endpoints.MapGet(Route, (HttpContext context) =>
        {
            context.Response.Headers.Allow = HttpMethods.Post;
            return Results.StatusCode(StatusCodes.Status405MethodNotAllowed);
        });

        return endpoints.MapMcp(Route);
    }
}
