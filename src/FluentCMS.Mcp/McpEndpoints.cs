using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FluentCMS.Mcp;

public static class McpEndpoints
{
    public const string Route = "/mcp";

    private static readonly AuthorizationPolicy AgentsOnly = new AuthorizationPolicyBuilder(AgentKeyAuthentication.Scheme)
        .RequireAuthenticatedUser()
        .Build();

    public static IEndpointConventionBuilder MapFluentCmsMcp(this IEndpointRouteBuilder endpoints)
    {
        // Stateless mode maps POST alone. A client that opens a stream with GET must get 405 to learn there is none,
        // which a GET fallback elsewhere in the host would otherwise turn into a 404.
        endpoints.MapGet(Route, (HttpContext context) =>
        {
            context.Response.Headers.Allow = HttpMethods.Post;
            return Results.StatusCode(StatusCodes.Status405MethodNotAllowed);
        });

        // The agent scheme alone: a person's admin cookie is never read here, so it cannot become the initiator of a call (SEC-01).
        return endpoints.MapMcp(Route).RequireAuthorization(AgentsOnly);
    }
}
