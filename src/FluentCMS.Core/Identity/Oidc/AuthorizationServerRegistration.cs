using System.Security.Claims;
using FluentCMS.Agents;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Identity.Members;
using FluentCMS.Core.Storage;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenIddict.Validation;
using OpenIddict.Validation.AspNetCore;

namespace FluentCMS.Core.Identity.Oidc;

// The OpenIddict server of spec 4.7 with its stores on the system model, the Data Protection key ring in the database,
// and the bearer scheme the FluentCms scheme takes a request without an API key to.
internal static class AuthorizationServerRegistration
{
    public const string ApplicationName = "FluentCMS";

    // An internal type of Data Protection, so a test fails when a framework version renames it.
    internal const string DataProtectionHostedService = "Microsoft.AspNetCore.DataProtection.Internal.DataProtectionHostedService";

    public static void Add(IServiceCollection services, ClientOptions clients)
    {
        services.AddFluentCmsOptions(ClientOptions.Section, OidcErrorCodes.InvalidClients, clients, options => options.Validate());

        // One application name, so that every instance reads the ring the others write.
        services.AddDataProtection().SetApplicationName(ApplicationName);
        services.AddSingleton<DataProtectionKeyRepository>();

        services.AddOptions<KeyManagementOptions>().Configure<DataProtectionKeyRepository>((options, repository) => options.XmlRepository ??= repository);
        LockedKeyManager.Register(services);

        // The cookie of the hosted pages, which only they and the authorize endpoint read.
        services.AddAuthentication().AddIdentityCookies();
        services.ConfigureApplicationCookie(options =>
        {
            options.LoginPath = HostedPagePaths.Login;
            options.LogoutPath = HostedPagePaths.Logout;
            options.AccessDeniedPath = HostedPagePaths.Error;
        });
        services.AddOptions<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme)
            .Configure<ClientOptions>((cookie, options) => cookie.ExpireTimeSpan = options.SignInCookieLifetime);

        // The stamp check rebuilds the cookie's principal from the user every half hour, which would drop how the user
        // signed in: amr is what a role that requires two-factor reads, and the provider the remote sign-out of SSO does.
        services.Configure<SecurityStampValidatorOptions>(options =>
        {
            var next = options.OnRefreshingPrincipal;
            options.OnRefreshingPrincipal = context =>
            {
                KeepAuthenticationMethods(context);
                return next?.Invoke(context) ?? Task.CompletedTask;
            };
        });

        services.AddOpenIddict()
            .AddCore(core => core.UseEntityFrameworkCore().UseDbContext<SystemDbContext>().ReplaceDefaultEntities<Guid>())
            .AddServer(server =>
            {
                server.SetAuthorizationEndpointUris("connect/authorize")
                    .SetTokenEndpointUris("connect/token")
                    .SetUserInfoEndpointUris("connect/userinfo")
                    .SetIntrospectionEndpointUris("connect/introspect")
                    .SetRevocationEndpointUris("connect/revoke")
                    .SetEndSessionEndpointUris("connect/logout")
                    .SetConfigurationEndpointUris(".well-known/openid-configuration")
                    .SetJsonWebKeySetEndpointUris(".well-known/jwks");

                // PKCE for every client, which OAuth 2.1 asks of public ones. A used refresh token is refused once OpenIddict's
                // reuse leeway is over, which lets two refreshes that cross, as two tabs of a SPA make, both succeed.
                server.AllowAuthorizationCodeFlow().RequireProofKeyForCodeExchange();
                server.Configure(options => options.CodeChallengeMethods.Remove(OpenIddictConstants.CodeChallengeMethods.Plain));
                server.AllowRefreshTokenFlow();
                server.AllowClientCredentialsFlow();

                // The direct login of members (Q4). The token endpoint takes it only while cms.Members.AllowDirectLogin is
                // on, and the discovery document names it only then.
                server.AllowCustomFlow(AuthorizationServer.MemberPasswordGrant);
                server.AddEventHandler(MemberGrantDiscoveryHandler.Descriptor);
                server.RegisterScopes(AuthorizationServer.KnownScopes);

                // Signed JWTs a resource server can read; codes and refresh tokens stay encrypted.
                server.DisableAccessTokenEncryption();

                server.AddEventHandler(PublicOriginDiscoveryHandler.Descriptor);
                server.AddEventHandler(RegistrationDiscoveryHandler.Descriptor);

                // The resource parameter (RFC 8707) is held to the protected resources of the host by a handler of core's,
                // since their URLs may follow the request. It grants nothing, so no client needs a permission for it: the
                // scope of the resource, which a client does need a permission for, is what a token is held to.
                server.DisableResourceValidation().IgnoreResourcePermissions();
                server.AddEventHandler(ResourceIndicatorHandler.AuthorizationDescriptor);
                server.AddEventHandler(ResourceIndicatorHandler.TokenDescriptor);

                // An error of authorize or logout that cannot go back to the client reaches the endpoint, which shows the error page.
                server.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough()
                    .EnableUserInfoEndpointPassthrough()
                    .EnableEndSessionEndpointPassthrough()
                    .EnableErrorPassthrough();
            })
            .AddValidation(validation =>
            {
                validation.UseLocalServer();
                // Both pools: a member's token is kept out of the Management API, of drafts and of /mcp by its scopes.
                validation.AddAudiences(AuthorizationServer.ManagementAudience, AuthorizationServer.MembersAudience);

                validation.AddEventHandler(BearerClaimsHandler.Descriptor);

                // From the Authorization header only: a token in a URL ends up in logs, Referer and history.
                validation.UseAspNetCore()
                    .DisableAccessTokenExtractionFromQueryString()
                    .DisableAccessTokenExtractionFromBodyForm();
            });

        // Without a public origin OpenIddict takes the issuer and the URLs of the discovery document from the request, as the
        // links in emails do.
        services.AddOptions<OpenIddictServerOptions>().Configure<PublicOrigin>((server, origin) => server.Issuer = origin.Issuer ?? server.Issuer);
        services.AddOptions<OpenIddictServerOptions>().Configure<ClientOptions>((server, options) =>
        {
            server.AccessTokenLifetime = options.AccessTokenLifetime;
            server.RefreshTokenLifetime = options.RefreshTokenLifetime;
        });

        // A revoked session stops its access tokens at once, not when they expire, at the cost of reading the token and its
        // authorization on every bearer request; a cache of them would let a revoked token through until it expired. After the
        // local server's own setup, which sets these from the server's options.
        services.PostConfigure<OpenIddictValidationOptions>(options =>
        {
            options.EnableAuthorizationEntryValidation = true;
            options.EnableTokenEntryValidation = true;
        });

        services.AddSingleton<SigningKeyStore>();
        services.AddSingleton<IConfigureOptions<OpenIddictServerOptions>, ServerCredentials>();
        services.TryAddSingleton<ISigningKeyStatus, SigningKeyStatus>();
        services.TryAddSingleton<IDataProtectionStatus, DataProtectionStatus>();

        // A host or test that set a bearer scheme of its own keeps it.
        services.Configure<FluentCmsAuthenticationOptions>(
            FluentCmsAuthentication.Scheme,
            options => options.BearerScheme ??= OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);

        services.AddScoped<UserSessions>();
        services.AddScoped<ClientConsents>();
        services.AddScoped<McpClients>();
        services.TryAddScoped<IMcpClients>(provider => provider.GetRequiredService<McpClients>());
        services.AddSingleton<ClientRegistrationLimiter>();
        services.AddHostedService<ClientRegistrationService>();
        services.AddFluentCmsEndpoints(FluentCmsEndpointGroup.Connect, ConnectEndpoints.Map);
        services.AddFluentCmsEndpoints(FluentCmsEndpointGroup.WellKnown, ConnectEndpoints.MapWellKnown);

        // Data Protection's hosted service loads the ring when the host starts, which with the ring in the database is a
        // query on every start and a new key on a new database; the ring loads on its first use instead, a sign-in or a
        // token. Last, since every registration that needs Data Protection adds the service again.
        foreach (var hosted in services.Where(service => service.ServiceType == typeof(IHostedService) && service.ImplementationType?.FullName == DataProtectionHostedService).ToList())
        {
            services.Remove(hosted);
        }
    }

    private static void KeepAuthenticationMethods(SecurityStampRefreshingPrincipalContext context)
    {
        if (context.NewPrincipal?.Identity is ClaimsIdentity identity && context.CurrentPrincipal is { } current)
        {
            identity.AddClaims(current.FindAll(claim => claim.Type is OpenIddictConstants.Claims.AuthenticationMethodReference or ClaimTypes.AuthenticationMethod)
                .Where(claim => !identity.HasClaim(claim.Type, claim.Value))
                .Select(claim => new Claim(claim.Type, claim.Value)));
        }
    }
}
