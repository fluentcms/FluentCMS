using FluentCMS.Core.Authorization;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Identity.ApiKeys;
using FluentCMS.Core.Identity.Oidc;
using FluentCMS.Core.Identity.Sso;
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
    public static void Add(IServiceCollection services, AdminUserOptions options, ClientOptions clients, SsoOptions sso, ProvidersBuilder providers)
    {
        services.AddFluentCmsOptions(AdminUserOptions.Section, IdentityErrorCodes.InvalidOptions, options, users => users.Validate());

        // The sign-in manager reads the schemes; the authorization server adds its own beside the API key's.
        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthentication.Scheme, configureOptions: null)
            .AddScheme<FluentCmsAuthenticationOptions, FluentCmsAuthenticationHandler>(FluentCmsAuthentication.Scheme, configureOptions: null);
        services.AddOptions<IdentityOptions>().Configure<AdminUserOptions>((identity, users) => users.Apply(identity));
        // The authenticator provider checks the codes of the two-factor page and of /me/two-factor.
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
        services.AddScoped<UserLogins>();
        services.AddScoped<TwoFactorService>();
        services.AddScoped<ApiKeyService>();
        services.AddScoped<ApiKeyPrincipals>();
        services.AddHttpContextAccessor();
        services.TryAddScoped<IPrincipalRoles, UserPrincipalRoles>();

        AuthorizationServerRegistration.Add(services, clients);
        SsoRegistration.Add(services, sso);

        services.AddFluentCmsOptions(EmailOptions.Section, HostErrorCodes.InvalidOption, new EmailOptions(), email => email.Validate());
        providers.Default<IEmailSender>(provider =>
        {
            var email = provider.GetRequiredService<EmailOptions>();
            if (email.UsesSmtp)
            {
                return new SmtpEmailSender(email);
            }

            return provider.GetService<IHostEnvironment>()?.IsDevelopment() == true
                ? new LogEmailSender(provider.GetRequiredService<ILogger<LogEmailSender>>())
                : new UnconfiguredEmailSender();
        });
    }
}
