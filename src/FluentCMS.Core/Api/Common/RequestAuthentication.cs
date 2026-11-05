using System.Security.Claims;
using FluentCMS.Core.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Api.Common;

// A request whose credentials were sent but cannot be accepted, which is a 401 and never an anonymous read.
internal sealed class RequestAuthenticationException(string message) : Exception(message);

// The FluentCms scheme decides who a request is: the API key handler when a key is sent, the bearer scheme otherwise.
// Without it, as in a host that removed it, the request's user from the default scheme is used.
internal static class RequestAuthentication
{
    public static async Task<ClaimsPrincipal> UserAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var schemes = httpContext.RequestServices.GetService<IAuthenticationSchemeProvider>();
        if (schemes is null || await schemes.GetSchemeAsync(FluentCmsAuthentication.Scheme) is null)
        {
            return httpContext.User;
        }

        var result = await httpContext.AuthenticateAsync(FluentCmsAuthentication.Scheme);
        if (result.Failure is not null)
        {
            throw new RequestAuthenticationException("The credentials of the request are not valid.");
        }

        // So that whatever runs after the endpoint, such as the problem writer, sees the same principal.
        if (result.Succeeded)
        {
            httpContext.User = result.Principal;
        }

        return result.Succeeded ? result.Principal : new ClaimsPrincipal(new ClaimsIdentity());
    }
}
