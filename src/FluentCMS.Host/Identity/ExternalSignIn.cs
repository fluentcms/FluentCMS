using FluentCMS.Admin;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace FluentCMS.Host.Identity;

internal static class ExternalSignIn
{
    public static void AddOpenIdConnectProvider(this AuthenticationBuilder authentication, OpenIdConnectProviderOptions provider, IHostEnvironment environment)
    {
        if (!provider.IsConfigured)
        {
            return;
        }

        // Caught here rather than by the handler, which would fail every request once the host is running.
        var isDevelopment = environment.IsDevelopment();
        if (!Uri.TryCreate(provider.Authority, UriKind.Absolute, out var authority)
            || (authority.Scheme != Uri.UriSchemeHttps && !(isDevelopment && authority.Scheme == Uri.UriSchemeHttp)))
        {
            throw new InvalidOperationException(
                $"{OpenIdConnectProviderOptions.SectionName}:Authority must be an absolute HTTPS URL; plain HTTP is accepted only in Development.");
        }

        // The provider's answer lands in Identity's external cookie; the admin page then signs the matching user in.
        authentication.AddOpenIdConnect(OpenIdConnectProviderOptions.Scheme, provider.DisplayName, options =>
        {
            options.SignInScheme = IdentityConstants.ExternalScheme;
            options.RequireHttpsMetadata = !isDevelopment;
            options.Authority = provider.Authority;
            options.ClientId = provider.ClientId;
            options.ClientSecret = provider.ClientSecret;
            options.ResponseType = OpenIdConnectResponseType.Code;
            options.ResponseMode = OpenIdConnectResponseMode.FormPost;
            options.UsePkce = true;
            options.Scope.Add("email");
        });
    }

    // The sign-in page's "Sign in with ..." form posts here; the form binding makes the anti-forgery token required.
    public static void MapExternalSignIn(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(AdminRoutes.ExternalSignInStart, async (
            [FromForm] string provider,
            [FromForm] string? returnUrl,
            SignInManager<FluentCmsUser> signInManager) =>
        {
            var schemes = await signInManager.GetExternalAuthenticationSchemesAsync();
            if (!schemes.Any(scheme => scheme.Name == provider))
            {
                return Results.NotFound();
            }

            var callback = AdminRoutes.IsLocal(returnUrl)
                ? $"{AdminRoutes.ExternalSignIn}?ReturnUrl={Uri.EscapeDataString(returnUrl!)}"
                : AdminRoutes.ExternalSignIn;
            var properties = signInManager.ConfigureExternalAuthenticationProperties(provider, callback);
            return Results.Challenge(properties, [provider]);
        });
    }
}
