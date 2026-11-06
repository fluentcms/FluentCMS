using FluentCMS.Core.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace FluentCMS.Core.Api.Common;

internal sealed class ScopeDeniedException(string message) : Exception(message);

// The Management API is for a bearer token or a key with the management scope. Without it the engine would still let a
// principal read what its roles allow, so the routes say who they are for: a request that is not signed in is 401, and
// a Delivery key or a token without the scope is 403. What the principal may then do stays the engine's to decide.
internal static class ManagementScope
{
    // After WithFluentCmsOperation, whose filter resolves the principal this one reads.
    public static TBuilder RequireManagementScope<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var principal = ApiRequest.Of(context.HttpContext).Operation.Principal;
            var scopes = (principal.Claim(TokenScopes.Claim) ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (!scopes.Contains(TokenScopes.Management, StringComparer.Ordinal))
            {
                throw new ScopeDeniedException($"The Management API needs a bearer token or an API key with the {TokenScopes.Management} scope.");
            }

            return await next(context);
        });
}
