using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Storage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using OpenIddict.Server;
using OpenIddict.Validation;
using OpenIddict.Validation.AspNetCore;

namespace FluentCMS.Core.Identity.Oidc;

// The OpenIddict server of spec 4.7 with its stores on the system model, the Data Protection key ring in the database,
// and the bearer scheme the FluentCms scheme takes a request without an API key to.
internal static class AuthorizationServerRegistration
{
    public const string ApplicationName = "FluentCMS";

    public static void Add(IServiceCollection services, ClientOptions clients)
    {
        services.AddFluentCmsOptions(ClientOptions.Section, OidcErrorCodes.InvalidClients, clients, options => options.Validate());

        // One application name, so that every instance reads the ring the others write.
        services.AddDataProtection().SetApplicationName(ApplicationName);
        services.AddSingleton<DataProtectionKeyRepository>();
        services.AddOptions<KeyManagementOptions>().Configure<DataProtectionKeyRepository>((options, repository) => options.XmlRepository ??= repository);

        // The cookie of the hosted pages (#2779), which only the authorize endpoint reads.
        services.AddAuthentication().AddIdentityCookies();
        services.ConfigureApplicationCookie(options =>
        {
            options.LoginPath = "/account/login";
            options.LogoutPath = "/account/logout";
            options.AccessDeniedPath = "/account/access-denied";
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

                // PKCE for every client, which OAuth 2.1 asks of public ones; a used refresh token is refused at once.
                server.AllowAuthorizationCodeFlow().RequireProofKeyForCodeExchange();
                server.Configure(options => options.CodeChallengeMethods.Remove(OpenIddict.Abstractions.OpenIddictConstants.CodeChallengeMethods.Plain));
                server.AllowRefreshTokenFlow().SetRefreshTokenReuseLeeway(TimeSpan.Zero);
                server.AllowClientCredentialsFlow();
                server.RegisterScopes(AuthorizationServer.KnownScopes);

                // Signed JWTs a resource server can read; codes and refresh tokens stay encrypted.
                server.DisableAccessTokenEncryption();

                server.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough()
                    .EnableUserInfoEndpointPassthrough()
                    .EnableEndSessionEndpointPassthrough();
            })
            .AddValidation(validation =>
            {
                validation.UseLocalServer();
                validation.AddAudiences(AuthorizationServer.ManagementAudience);

                validation.AddEventHandler(BearerClaimsHandler.Descriptor);

                // From the Authorization header only: a token in a URL ends up in logs, Referer and history.
                validation.UseAspNetCore()
                    .DisableAccessTokenExtractionFromQueryString()
                    .DisableAccessTokenExtractionFromBodyForm();
            });

        // A revoked session stops its access tokens at once, not when they expire. After the local server's own setup,
        // which sets these from the server's options.
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
        services.AddHostedService<ClientRegistrationService>();
        services.AddFluentCmsEndpoints(FluentCmsEndpointGroup.Connect, ConnectEndpoints.Map);
        services.AddFluentCmsEndpoints(FluentCmsEndpointGroup.WellKnown, ConnectEndpoints.MapWellKnown);
    }
}
