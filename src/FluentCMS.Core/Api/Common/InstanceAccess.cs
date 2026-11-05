using FluentCMS.Core.Authorization;
using Microsoft.AspNetCore.Http;

namespace FluentCMS.Core.Api.Common;

internal sealed class InstanceAccessDeniedException(string message) : Exception(message);

// An endpoint that cannot answer yet, a 503.
internal sealed class ServiceUnavailableException(string message) : Exception(message);

// A permission the instance holds as a whole, such as system.read, and not for a site: a role of one site, a grant limited
// to sites and a key bound to sites do not reach it.
internal static class InstanceAccess
{
    public static async Task RequireAsync(HttpContext httpContext, IAuthorizationEvaluator evaluator, string permission)
    {
        var operation = ApiRequest.Of(httpContext).Operation;
        var permissions = await evaluator.ResolveAsync(operation, httpContext.RequestAborted);

        if (!permissions.Allows(permission, new AccessTarget(Site: null, Locale: null)))
        {
            throw new InstanceAccessDeniedException($"The request needs the instance-wide permission {permission}.");
        }
    }
}
