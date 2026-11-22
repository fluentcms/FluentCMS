using System.Security.Claims;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Identity.Oidc;
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

        // Again once every scheme is added, since a host may add one after these, and logins are found ignoring case too.
        services.AddOptions<AuthenticationOptions>().PostConfigure<SsoOptions>((authentication, options) =>
        {
            foreach (var name in options.Providers.Keys)
            {
                SsoOptions.Require(
                    authentication.SchemeMap.Keys.Count(scheme => string.Equals(scheme, name, StringComparison.OrdinalIgnoreCase)) == 1,
                    $"{nameof(SsoOptions.Providers)}:{name}: '{name}' is the name of another authentication scheme.");
            }
        });

        services.AddSingleton<IConfigureOptions<OpenIdConnectOptions>, HandlerOptions>();
        services.AddSingleton<IPostConfigureOptions<OpenIdConnectOptions>, ChainedEvents>();
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
        }
    }

    // The events of a provider run FluentCMS's own first and then those the host set, as Events or as EventsType, so that a
    // host's events drop neither the check of the remote sign-out nor the token's subject and session (#3225). It runs
    // after every Configure, and before PublicOriginCallback, which wraps the events it leaves.
    private sealed class ChainedEvents(SsoOptions sso) : IPostConfigureOptions<OpenIdConnectOptions>
    {
        public void PostConfigure(string? name, OpenIdConnectOptions options)
        {
            if (name is null || !sso.Providers.ContainsKey(name))
            {
                return;
            }

            options.Events = new SsoEvents(options.Events, options.EventsType);
            options.EventsType = null;
        }
    }

    private sealed class SsoEvents : OpenIdConnectEvents
    {
        private static readonly OpenIdConnectEvents NoEvents = new();

        private readonly object _hostItem = new();
        private readonly OpenIdConnectEvents? _host;
        private readonly Type? _hostType;

        public SsoEvents(OpenIdConnectEvents? host, Type? hostType)
        {
            (_host, _hostType) = (host, hostType);
            OnAuthenticationFailed = context => Host(context.HttpContext).AuthenticationFailed(context);
            OnAuthorizationCodeReceived = context => Host(context.HttpContext).AuthorizationCodeReceived(context);
            OnMessageReceived = context => Host(context.HttpContext).MessageReceived(context);
            OnPushAuthorization = context => Host(context.HttpContext).PushAuthorization(context);
            OnRedirectToIdentityProvider = context => Host(context.HttpContext).RedirectToIdentityProvider(context);
            OnRedirectToIdentityProviderForSignOut = context => Host(context.HttpContext).RedirectToIdentityProviderForSignOut(context);
            OnSignedOutCallbackRedirect = context => Host(context.HttpContext).SignedOutCallbackRedirect(context);
            OnTokenResponseReceived = context => Host(context.HttpContext).TokenResponseReceived(context);
            OnUserInformationReceived = context => Host(context.HttpContext).UserInformationReceived(context);
            OnAccessDenied = context => Host(context.HttpContext).AccessDenied(context);
            OnTicketReceived = context => Host(context.HttpContext).TicketReceived(context);
            OnTokenValidated = async context =>
            {
                await KeepSubjectAndSession(context);
                await Host(context.HttpContext).TokenValidated(context);
            };
            OnRemoteFailure = async context =>
            {
                await BackToLoginAsync(context);
                await Host(context.HttpContext).RemoteFailure(context);
            };
            OnRemoteSignOut = async context =>
            {
                await NamedSignInOnlyAsync(context);
                await Host(context.HttpContext).RemoteSignOut(context);
            };
        }

        // A type is resolved once a request, as the handler resolves EventsType.
        private OpenIdConnectEvents Host(HttpContext http)
        {
            if (_hostType is null)
            {
                return _host ?? NoEvents;
            }

            if (http.Items.TryGetValue(_hostItem, out var resolved) && resolved is OpenIdConnectEvents events)
            {
                return events;
            }

            events = (OpenIdConnectEvents)http.RequestServices.GetRequiredService(_hostType);
            http.Items[_hostItem] = events;
            return events;
        }

        // The provider's front-channel logout ends the sign-in on the hosted pages, the admin session, only when it carries
        // the sid and iss of the id token that sign-in was made with. The handler skips its own check when the cookie has no
        // such claims, so it is never left to the handler. A match ends the server session's authorizations with the
        // cookie, as a confirmed logout does; anything else, a sign-in made with a password, through another provider or
        // with a token without sid included, ends nothing, and the answer is the same.
        private static async Task NamedSignInOnlyAsync(RemoteSignOutContext context)
        {
            context.HandleResponse();
            context.Response.Headers.CacheControl = "no-cache, no-store";
            context.Response.Headers.Pragma = "no-cache";

            var services = context.HttpContext.RequestServices;
            var logger = services.GetRequiredService<ILogger<SsoSignIn>>();
            var cookie = await context.HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
            if (!cookie.Succeeded || SsoSession.Of(cookie.Properties!) is not { } session)
            {
                SsoLog.RemoteSignOutIgnored(logger, context.Scheme.Name, "the request carries no sign-in made through a provider with a session id.");
                return;
            }

            if (await RepeatsAsync(context.Request, OpenIdConnectParameterNames.Sid) || await RepeatsAsync(context.Request, OpenIdConnectParameterNames.Iss))
            {
                SsoLog.RemoteSignOutIgnored(logger, context.Scheme.Name, "the sid or iss is sent more than once.");
                return;
            }

            if (!session.IsNamedBy(context.Scheme.Name, context.ProtocolMessage?.Sid, context.ProtocolMessage?.Iss))
            {
                SsoLog.RemoteSignOutIgnored(logger, context.Scheme.Name, "the sign-in is another provider's, or the sid and iss are missing or are not those of the sign-in.");
                return;
            }

            var userId = services.GetRequiredService<UserManager<CmsUser>>().GetUserId(cookie.Principal!);
            await services.GetRequiredService<UserSessions>().EndAsync(userId, CmsSignInManager.ServerSession(cookie.Properties!), CancellationToken.None);
            await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
            SsoLog.RemoteSignOut(logger, context.Scheme.Name, userId);
            await services.GetRequiredService<SessionEvents>().SignInEndedAsync(context.HttpContext, members: false, userId, cookie.Principal!, SessionEndReasons.RemoteSignOut, bySystem: true);
        }

        // The handler's message keeps the first of several values of a parameter, so a repeated one could hide another.
        private static async Task<bool> RepeatsAsync(HttpRequest request, string name) =>
            request.Query[name].Count > 1 || (request.HasFormContentType && (await request.ReadFormAsync()).TryGetValue(name, out var values) && values.Count > 1);

        // Identity takes the external login's key from the name identifier, which the token calls sub. The token's sid and
        // iss go with the external cookie, for the sign-in to keep.
        private static Task KeepSubjectAndSession(TokenValidatedContext context)
        {
            if (context.Principal?.Identity is ClaimsIdentity identity && !identity.HasClaim(claim => claim.Type == ClaimTypes.NameIdentifier)
                && identity.FindFirst("sub") is { } subject)
            {
                identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, subject.Value, subject.ValueType, subject.Issuer));
            }

            if (context.SecurityToken is { } token && context.Properties is { } properties
                && token.Payload.TryGetValue("sid", out var value) && value is string { Length: > 0 } sid && !string.IsNullOrEmpty(token.Issuer))
            {
                new SsoSession(context.Scheme.Name, sid, token.Issuer).WriteTo(properties);
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
