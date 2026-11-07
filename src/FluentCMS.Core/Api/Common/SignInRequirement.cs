using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace FluentCMS.Core.Api.Common;

internal sealed class SignInRequiredException(string message) : Exception(message);

// A plugin's own group is for a caller that the FluentCms scheme signed in, so that an endpoint whose permission check was
// forgotten is not open to anyone. An endpoint that is public on purpose says so with AllowAnonymous(). What the caller may
// then do stays the plugin's to check. It is a filter, as the Management scope is, so that the refusal is a problem like
// core's and no host needs UseAuthorization for it.
internal static class SignInRequirement
{
    // After WithFluentCmsOperation, whose filter resolves the principal this one reads.
    public static TBuilder RequireSignedIn<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var httpContext = context.HttpContext;
            if (ApiRequest.Of(httpContext).Operation.Principal.IsAnonymous
                && httpContext.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is null)
            {
                throw new SignInRequiredException("This endpoint needs a bearer token or an API key.");
            }

            return await next(context);
        });
}
