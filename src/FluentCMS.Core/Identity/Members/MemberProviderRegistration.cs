using System.Security.Claims;
using AspNet.Security.OAuth.Apple;
using AspNet.Security.OAuth.GitHub;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Events;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.MicrosoftAccount;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace FluentCMS.Core.Identity.Members;

internal static class MemberProviders
{
    // The cookie a provider of the member pool signs into, which the member external login page reads and ends. It is
    // apart from Identity's external cookie, which is the admin pool's, so a sign-in at a provider of one pool is never
    // read by the pages of the other.
    public const string ExternalScheme = "FluentCms.Members.External";

    public const string ExternalCookieName = ".FluentCms.Members.External";

    public const string CallbackPrefix = "/account/members/sso/";

    // Items of the ticket: the provider that issued it, written when the provider answers, and the member that asked
    // to link the login, written when the sign-in starts on the page of a signed-in member.
    public const string ProviderItem = ".fcms.member_provider";
    public const string LinkItem = ".fcms.member_link";

    public const string GitHubNameClaim = "urn:github:name";

    public static AuthenticationProperties Challenge(string redirectUri, Guid? linkFor = null)
    {
        var properties = new AuthenticationProperties { RedirectUri = redirectUri };
        if (linkFor is { } member)
        {
            properties.Items[LinkItem] = member.ToString("D");
        }

        return properties;
    }

    // The callback a provider is given is on the public origin when one is set, as links in emails are.
    public static string Callback(PublicOrigin origin, PathString callbackPath, string fromRequest) =>
        origin.Base is { } publicBase ? publicBase + callbackPath : fromRequest;

    public static OAuthCodeExchangeContext Callback(PublicOrigin origin, PathString callbackPath, OAuthCodeExchangeContext context) =>
        origin.Base is { } publicBase ? new OAuthCodeExchangeContext(context.Properties, context.Code, publicBase + callbackPath) : context;
}

// The providers of cms.Members.Providers as authentication schemes named member-{name}. They come from the options,
// which configuration binds when the host starts, so each is added to the authentication options then, as the providers
// of cms.Sso are. A host tunes one further with services.Configure<GoogleOptions>("member-{name}", ...) and the like.
internal static class MemberProviderRegistration
{
    public static void Add(IServiceCollection services)
    {
        services.AddOptions<AuthenticationOptions>()
            .Configure<MemberOptions>((authentication, options) =>
            {
                foreach (var (name, provider) in options.Providers)
                {
                    var scheme = MemberProvider.SchemeOf(name);
                    MemberProvider.Require(
                        !authentication.SchemeMap.Keys.Any(known => string.Equals(known, scheme, StringComparison.OrdinalIgnoreCase)),
                        $"{FluentCmsOptions.Section}:{MemberOptions.Section}:{nameof(MemberOptions.Providers)}:{name}: '{scheme}', the authentication scheme of the provider, is the name of another authentication scheme.");

                    // No display name: the admin login page offers the schemes that have one, and this is none of its.
                    authentication.AddScheme(scheme, built => built.HandlerType = HandlerOf(provider.Kind));
                }
            })
            .ValidateOnStart();

        Configure<GoogleOptions>(services, MemberProviderKind.Google, Google);
        Configure<MicrosoftAccountOptions>(services, MemberProviderKind.Microsoft, (options, _, provider) => OAuth(options, provider));
        Configure<GitHubAuthenticationOptions>(services, MemberProviderKind.GitHub, GitHub);
        Configure<AppleAuthenticationOptions>(services, MemberProviderKind.Apple, Apple);
        Configure<OpenIdConnectOptions>(services, MemberProviderKind.Oidc, Oidc);

        // What AddGoogle, AddMicrosoftAccount, AddGitHub, AddApple and AddOpenIdConnect register beside their scheme.
        services.AddMemoryCache();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<GoogleOptions>, OAuthPostConfigureOptions<GoogleOptions, GoogleHandler>>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<MicrosoftAccountOptions>, OAuthPostConfigureOptions<MicrosoftAccountOptions, MicrosoftAccountHandler>>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<GitHubAuthenticationOptions>, GitHubPostConfigureOptions>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<GitHubAuthenticationOptions>, OAuthPostConfigureOptions<GitHubAuthenticationOptions, GitHubAuthenticationHandler>>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<AppleAuthenticationOptions>, ApplePostConfigureOptions>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<AppleAuthenticationOptions>, OAuthPostConfigureOptions<AppleAuthenticationOptions, AppleAuthenticationHandler>>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<OpenIdConnectOptions>, OpenIdConnectPostConfigureOptions>());
        services.AddSingleton<IPostConfigureOptions<OpenIdConnectOptions>, PublicOriginCallback>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<LoggerFilterOptions>, AppleLogLevel>());

        services.AddScoped<MemberExternalSignIn>();
        services.AddScoped<MemberLogins>();
    }

    private static Type HandlerOf(MemberProviderKind kind) => kind switch
    {
        MemberProviderKind.Google => typeof(MemberGoogleHandler),
        MemberProviderKind.Microsoft => typeof(MemberMicrosoftHandler),
        MemberProviderKind.GitHub => typeof(MemberGitHubHandler),
        MemberProviderKind.Apple => typeof(MemberAppleHandler),
        _ => typeof(OpenIdConnectHandler),
    };

    private static void Configure<TOptions>(IServiceCollection services, MemberProviderKind kind, Action<TOptions, string, MemberProvider> configure)
        where TOptions : RemoteAuthenticationOptions =>
        services.AddSingleton<IConfigureOptions<TOptions>>(provider => new ProviderOptions<TOptions>(provider.GetRequiredService<MemberOptions>(), kind, configure));

    // Google's user information says whether the address is verified; the handler does not keep it unless asked.
    private static void Google(GoogleOptions options, string name, MemberProvider provider)
    {
        OAuth(options, provider);
        options.ClaimActions.MapJsonKey(MemberExternalSignIn.EmailVerifiedClaim, "email_verified");
    }

    // The address of the public profile is never taken: it need be neither the primary one nor verified. With
    // user:email the handler asks for the account's addresses, of which MemberGitHubHandler takes the verified primary.
    private static void GitHub(GitHubAuthenticationOptions options, string name, MemberProvider provider)
    {
        OAuth(options, provider);
        options.ClaimActions.Remove(ClaimTypes.Email);
        if (!options.Scope.Contains(MemberGitHubHandler.EmailScope))
        {
            options.Scope.Add(MemberGitHubHandler.EmailScope);
        }
    }

    // The handler signs a client secret with the private key each time one runs out; the id token it validates against
    // Apple's keys carries the address and email_verified.
    private static void Apple(AppleAuthenticationOptions options, string name, MemberProvider provider)
    {
        OAuth(options, provider);
        options.TeamId = provider.TeamId!.Trim();
        options.KeyId = provider.KeyId!.Trim();
        options.GenerateClientSecret = true;
        options.PrivateKey = (_, _) => Task.FromResult(provider.PrivateKey.AsMemory());
    }

    // The code flow with PKCE, as for a provider of cms.Sso. Claims keep the names of the token.
    private static void Oidc(OpenIdConnectOptions options, string name, MemberProvider provider)
    {
        options.Authority = provider.Authority!.Trim();
        options.RequireHttpsMetadata = provider.RequireHttpsMetadata;
        options.ClientId = provider.ClientId!.Trim();
        options.ClientSecret = string.IsNullOrEmpty(provider.ClientSecret) ? null : provider.ClientSecret;
        options.ResponseType = OpenIdConnectResponseType.Code;
        options.UsePkce = true;
        options.SignedOutCallbackPath = MemberProviders.CallbackPrefix + name + "/signed-out";
        options.RemoteSignOutPath = MemberProviders.CallbackPrefix + name + "/signout";
        options.MapInboundClaims = false;
        options.TokenValidationParameters.NameClaimType = "name";
        options.GetClaimsFromUserInfoEndpoint = false;
        AddScopes(options.Scope, provider.Scopes.Prepend("email"));

        options.Events.OnTokenValidated = KeepSubject;

        // A provider's logout ends no member session, and none is started here: the two paths the handler would answer
        // are left to the rest of the pipeline, which has nothing there.
        options.Events.OnRemoteSignOut = NotServed;
        options.Events.OnSignedOutCallbackRedirect = NotServed;
    }

    private static Task NotServed(RemoteSignOutContext context)
    {
        context.SkipHandler();
        return Task.CompletedTask;
    }

    private static void OAuth(OAuthOptions options, MemberProvider provider)
    {
        options.ClientId = provider.ClientId!.Trim();
        options.ClientSecret = provider.ClientSecret ?? "";
        AddScopes(options.Scope, provider.Scopes);
    }

    private static void AddScopes(ICollection<string> scopes, IEnumerable<string> added)
    {
        foreach (var scope in added)
        {
            if (!scopes.Contains(scope))
            {
                scopes.Add(scope);
            }
        }
    }

    // The login's key is read from the name identifier, which the token calls sub.
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
    // the member login page as a failed sign-in rather than an error page. One that a signed-in member started to link
    // a login goes back to the page it started on, and is no sign-in.
    private static async Task BackToLoginAsync(RemoteFailureContext context)
    {
        var services = context.HttpContext.RequestServices;
        var provider = context.Scheme.Name[MemberProvider.SchemePrefix.Length..];
        MemberLog.ProviderFailure(services.GetRequiredService<ILogger<MemberExternalSignIn>>(), provider, context.Failure?.Message ?? "unknown failure");

        var failed = QueryString.Create("Status", "external-failed");
        if (context.Properties?.Items.ContainsKey(MemberProviders.LinkItem) == true)
        {
            context.Response.Redirect(context.Request.PathBase + HostedPagePaths.MemberLogins + failed);
            context.HandleResponse();
            return;
        }

        await services.GetRequiredService<MemberSignInEvents>().FailedAsync(context.HttpContext, null, provider, LoginFailureReasons.InvalidCredentials);

        var redirect = context.Properties?.RedirectUri;
        var returnUrl = redirect?.IndexOf('?', StringComparison.Ordinal) is int start and >= 0
            ? QueryHelpers.ParseQuery(redirect[start..]).GetValueOrDefault("ReturnUrl").ToString()
            : null;
        if (!string.IsNullOrEmpty(returnUrl))
        {
            failed = QueryString.Create("ReturnUrl", returnUrl).Add(failed);
        }

        context.Response.Redirect(context.Request.PathBase + HostedPagePaths.MemberLogin + failed);
        context.HandleResponse();
    }

    // What every provider of the member pool has in common, whatever its handler: where it signs in, where it comes
    // back to, and that the ticket names the provider, which the external login page trusts over anything a request says.
    private sealed class ProviderOptions<TOptions>(MemberOptions members, MemberProviderKind kind, Action<TOptions, string, MemberProvider> configure) : IConfigureNamedOptions<TOptions>
        where TOptions : RemoteAuthenticationOptions
    {
        public void Configure(TOptions options)
        {
        }

        public void Configure(string? name, TOptions options)
        {
            if (name is null || !name.StartsWith(MemberProvider.SchemePrefix, StringComparison.Ordinal)
                || !members.Providers.TryGetValue(name[MemberProvider.SchemePrefix.Length..], out var provider) || provider.Kind != kind)
            {
                return;
            }

            var providerName = name[MemberProvider.SchemePrefix.Length..];
            options.SignInScheme = MemberProviders.ExternalScheme;
            options.CallbackPath = MemberProviders.CallbackPrefix + providerName;
            options.SaveTokens = false;
            options.Events.OnRemoteFailure = BackToLoginAsync;
            options.Events.OnTicketReceived = context =>
            {
                context.Properties!.Items[MemberProviders.ProviderItem] = providerName;
                return Task.CompletedTask;
            };

            configure(options, providerName, provider);
        }
    }

    // The OpenID Connect handler keeps the redirect_uri it sends for the code's redemption, so both use the public
    // origin. It runs after every Configure, and before the event a host set, so a host's own event keeps it.
    private sealed class PublicOriginCallback(MemberOptions members, PublicOrigin origin) : IPostConfigureOptions<OpenIdConnectOptions>
    {
        public void PostConfigure(string? name, OpenIdConnectOptions options)
        {
            if (name is null || !name.StartsWith(MemberProvider.SchemePrefix, StringComparison.Ordinal)
                || !members.Providers.TryGetValue(name[MemberProvider.SchemePrefix.Length..], out var provider) || provider.Kind != MemberProviderKind.Oidc)
            {
                return;
            }

            var next = options.Events.OnRedirectToIdentityProvider;
            options.Events.OnRedirectToIdentityProvider = context =>
            {
                context.ProtocolMessage.RedirectUri = MemberProviders.Callback(origin, context.Options.CallbackPath, context.ProtocolMessage.RedirectUri);
                return next(context);
            };
        }
    }
}
