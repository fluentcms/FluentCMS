using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Api.Common;

internal static class ApiAuthentication
{
    // The policy scheme that the API key registration adds. It forwards to the API key scheme when a key is presented and
    // otherwise to the host's default scheme, so it sees the caller of an API request whatever the default scheme is.
    public const string Scheme = "FluentCms";

    private static readonly object PrincipalItem = new();

    // The caller of the request: the principal of the FluentCms scheme when there is one, else the default scheme's.
    public static ClaimsPrincipal Of(HttpContext context) =>
        context.Items.TryGetValue(PrincipalItem, out var principal) && principal is ClaimsPrincipal found ? found : context.User;

    public static async Task AuthenticateAsync(HttpContext context)
    {
        var schemes = context.RequestServices.GetRequiredService<IAuthenticationSchemeProvider>();
        if (await schemes.GetSchemeAsync(Scheme) is null)
        {
            return;
        }

        var result = await context.AuthenticateAsync(Scheme);
        if (result.Succeeded)
        {
            context.Items[PrincipalItem] = result.Principal;
        }
    }
}
