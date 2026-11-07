using System.Security.Claims;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Events;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace FluentCMS.Core.Identity.Sso;

// The providers of cms.Sso as OpenID Connect schemes. They come from the options, which configuration binds when the host
// starts, so each is added to the authentication options then rather than with AddOpenIdConnect. A host tunes one further
// with services.Configure<OpenIdConnectOptions>(name, ...).
internal static class SsoRegistration
{
    public const string CallbackPrefix = "/account/sso/";

    public static void Add(IServiceCollection services, SsoOptions sso)
    {
        services.AddFluentCmsOptions(SsoOptions.Section, IdentityErrorCodes.InvalidSsoOptions, sso, options => options.Validate());

        services.AddOptions<AuthenticationOptions>().Configure<SsoOptions>((authentication, options) =>
        {
            foreach (var (name, provider) in options.Providers)
            {
                // Providers are found by name ignoring case, so another scheme may not differ from one only in case.
                SsoOptions.Require(
                    !authentication.SchemeMap.Keys.Any(scheme => string.Equals(scheme, name, StringComparison.OrdinalIgnoreCase)),
                    $"{nameof(SsoOptions.Providers)}:{name}: '{name}' is the name of another authentication scheme.");
                authentication.AddScheme(name, scheme =>
                {
                    scheme.HandlerType = typeof(OpenIdConnectHandler);
                    scheme.DisplayName = provider.Name(name);
                });
            }
        });

        services.AddSingleton<IConfigureOptions<OpenIdConnectOptions>, HandlerOptions>();
        services.AddSingleton<IPostConfigureOptions<OpenIdConnectOptions>, PublicOriginCallback>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<OpenIdConnectOptions>, OpenIdConnectPostConfigureOptions>());
        services.AddScoped<SsoSignIn>();
    }

    // The code flow with PKCE into Identity's external cookie, which the external login page reads. Claims keep the names of
    // the token, so EmailClaim and GroupsClaim name them as the provider does, and amr reaches the page.
    private sealed class HandlerOptions(SsoOptions sso) : IConfigureNamedOptions<OpenIdConnectOptions>
    {
        public void Configure(OpenIdConnectOptions options)
        {
        }

        public void Configure(string? name, OpenIdConnectOptions options)
        {
            if (name is null || !sso.Providers.TryGetValue(name, out var provider))
            {
                return;
            }

            options.SignInScheme = IdentityConstants.ExternalScheme;
            options.SignOutScheme = IdentityConstants.ApplicationScheme;
            options.Authority = provider.ResolveAuthority();
            options.RequireHttpsMetadata = provider.RequireHttpsMetadata;
            options.ClientId = provider.ClientId;
            options.ClientSecret = string.IsNullOrEmpty(provider.ClientSecret) ? null : provider.ClientSecret;
            options.ResponseType = OpenIdConnectResponseType.Code;
            options.UsePkce = true;
            options.CallbackPath = CallbackPrefix + name;
            options.SignedOutCallbackPath = CallbackPrefix + name + "/signed-out";
            options.RemoteSignOutPath = CallbackPrefix + name + "/signout";
            options.MapInboundClaims = false;
            options.TokenValidationParameters.NameClaimType = "name";
            options.GetClaimsFromUserInfoEndpoint = false;
            options.SaveTokens = false;
            options.ClaimActions.Remove("amr");
            foreach (var scope in provider.Scopes.Prepend("email"))
            {
                if (!options.Scope.Contains(scope))
                {
                    options.Scope.Add(scope);
                }
            }

            options.Events.OnTokenValidated = KeepSubject;
            options.Events.OnRemoteFailure = BackToLoginAsync;
            options.Events.OnRemoteSignOut = OwnSignInOnlyAsync;
        }

        // The provider's front-channel logout ends the sign-in on the hosted pages, the admin session. The cookie holds no
        // session id of the provider for the handler to match, so a sign-in made another way, with a password or another
        // provider, is left as it is.
        private static async Task OwnSignInOnlyAsync(RemoteSignOutContext context)
        {
            var cookie = await context.HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
            if (!string.Equals(cookie.Principal?.FindFirstValue(ClaimTypes.AuthenticationMethod), context.Scheme.Name, StringComparison.OrdinalIgnoreCase))
            {
                context.HandleResponse();
            }
        }

        // Identity takes the external login's key from the name identifier, which the token calls sub.
        private static Task KeepSubject(TokenValidatedContext context)
        {
            if (context.Principal?.Identity is ClaimsIdentity identity && !identity.HasClaim(claim => claim.Type == ClaimTypes.NameIdentifier)
                && identity.FindFirst("sub") is { } subject)
            {
                identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, subject.Value, subject.ValueType, subject.Issuer));
            }

            return Task.CompletedTask;
        }

        // A refusal or an error of the provider, or a callback that does not belong to a sign-in started here, goes back to
        // the login page as a failed sign-in rather than an error page; one started on the page that accepts an invitation
        // goes back there, which then finds no external login and says so.
        private static async Task BackToLoginAsync(RemoteFailureContext context)
        {
            var services = context.HttpContext.RequestServices;
            SsoLog.RemoteFailure(services.GetRequiredService<ILogger<SsoSignIn>>(), context.Scheme.Name, context.Failure?.Message ?? "unknown failure");
            await services.GetRequiredService<SignInEvents>().FailedAsync(context.HttpContext, null, LoginMethods.External, LoginFailureReasons.InvalidCredentials);

            var redirect = context.Properties?.RedirectUri;
            if (redirect is not null && redirect.StartsWith(context.Request.PathBase + HostedPagePaths.AcceptInvitation + "?", StringComparison.Ordinal))
            {
                context.Response.Redirect(redirect);
                context.HandleResponse();
                return;
            }

            var returnUrl = redirect?.IndexOf('?', StringComparison.Ordinal) is int start and >= 0
                ? QueryHelpers.ParseQuery(redirect[start..]).GetValueOrDefault("ReturnUrl").ToString()
                : null;
            var query = QueryString.Create("Status", "external-failed");
            if (!string.IsNullOrEmpty(returnUrl))
            {
                query = QueryString.Create("ReturnUrl", returnUrl).Add(query);
            }

            context.Response.Redirect(context.Request.PathBase + HostedPagePaths.Login + query);
            context.HandleResponse();
        }
    }

    // The callback a provider is given is on the public origin when one is set, as links in emails are; the handler keeps
    // the redirect_uri it sends for the code's redemption, so both use it. It runs after every Configure, and before the
    // event a host set, so a host's own OnRedirectToIdentityProvider keeps it.
    private sealed class PublicOriginCallback(SsoOptions sso, PublicOrigin origin) : IPostConfigureOptions<OpenIdConnectOptions>
    {
        public void PostConfigure(string? name, OpenIdConnectOptions options)
        {
            if (name is null || !sso.Providers.ContainsKey(name))
            {
                return;
            }

            var next = options.Events.OnRedirectToIdentityProvider;
            options.Events.OnRedirectToIdentityProvider = context =>
            {
                if (origin.Base is { } publicBase)
                {
                    context.ProtocolMessage.RedirectUri = publicBase + context.Options.CallbackPath;
                }

                return next(context);
            };
        }
    }
}
