using FluentCMS.Core.Authorization;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Identity.ApiKeys;
using FluentCMS.Core.Identity.Oidc;
using FluentCMS.Providers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Identity;

internal static class IdentityRegistration
{
    public static void Add(IServiceCollection services, AdminUserOptions options, ClientOptions clients)
    {
        services.AddFluentCmsOptions(AdminUserOptions.Section, IdentityErrorCodes.InvalidOptions, options, users => users.Validate());

        // The sign-in manager reads the schemes; the authorization server adds its own beside the API key's.
        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthentication.Scheme, configureOptions: null)
            .AddScheme<FluentCmsAuthenticationOptions, FluentCmsAuthenticationHandler>(FluentCmsAuthentication.Scheme, configureOptions: null);
        services.AddOptions<IdentityOptions>().Configure<AdminUserOptions>((identity, users) => users.Apply(identity));
        // The authenticator provider is what the two-factor page checks a code with; enrolling one is #2780.
        services.AddIdentityCore<CmsUser>()
            .AddUserStore<CmsUserStore>()
            .AddUserManager<CmsUserManager>()
            .AddSignInManager<CmsSignInManager>()
            .AddTokenProvider<PasswordResetTokenProvider>(PasswordResetTokenProvider.ProviderName)
            .AddTokenProvider<AuthenticatorTokenProvider<CmsUser>>(TokenOptions.DefaultAuthenticatorProvider);
        services.AddOptions<PasswordResetTokenProviderOptions>().Configure<AdminUserOptions>((reset, users) =>
        {
            reset.Name = PasswordResetTokenProvider.ProviderName;
            reset.TokenLifespan = users.PasswordResetLifetime;
        });

        services.AddScoped<IdentityChanges>();
        services.AddScoped<SignInEvents>();
        services.AddScoped<InvitationService>();
        services.AddScoped<PasswordResetService>();
        services.AddScoped<UserAdministration>();
        services.AddScoped<CurrentUser>();
        services.AddScoped<ApiKeyService>();
        services.AddScoped<ApiKeyPrincipals>();
        services.TryAddScoped<IPrincipalRoles, UserPrincipalRoles>();

        AuthorizationServerRegistration.Add(services, clients);

        services.TryAddSingleton<IEmailSender>(provider =>
            provider.GetService<IHostEnvironment>()?.IsDevelopment() == true
                ? new LogEmailSender(provider.GetRequiredService<ILogger<LogEmailSender>>())
                : new UnconfiguredEmailSender());
    }
}
