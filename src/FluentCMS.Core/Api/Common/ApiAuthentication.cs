using System.Security.Claims;
using FluentCMS.Core.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Api.Common;

internal static class ApiAuthentication
{
    private static readonly object PrincipalItem = new();
    private static readonly object FailedItem = new();

    // The caller of the request: the principal of the FluentCms scheme when there is one, else the default scheme's.
    public static ClaimsPrincipal Of(HttpContext context) =>
        context.Items.TryGetValue(PrincipalItem, out var principal) && principal is ClaimsPrincipal found ? found : context.User;

    // Whether the FluentCms scheme refused the credentials the request carries, which the endpoint answers with a 401.
    public static bool Failed(HttpContext context) => context.Items.ContainsKey(FailedItem);

    public static async Task AuthenticateAsync(HttpContext context)
    {
        var schemes = context.RequestServices.GetRequiredService<IAuthenticationSchemeProvider>();
        if (await schemes.GetSchemeAsync(FluentCmsAuthentication.Scheme) is null)
        {
            return;
        }

        var result = await context.AuthenticateAsync(FluentCmsAuthentication.Scheme);
        if (result.Succeeded)
        {
            context.Items[PrincipalItem] = result.Principal;
        }
        else if (result.Failure is not null)
        {
            context.Items[FailedItem] = true;
        }
    }
}
