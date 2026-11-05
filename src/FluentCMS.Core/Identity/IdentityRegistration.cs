using FluentCMS.Core.Authorization;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Providers;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Identity;

internal static class IdentityRegistration
{
    public static void Add(IServiceCollection services, AdminUserOptions options)
    {
        services.AddFluentCmsOptions(AdminUserOptions.Section, IdentityErrorCodes.InvalidOptions, options, users => users.Validate());

        // The sign-in manager reads the schemes; the authorization server adds them.
        services.AddAuthentication();
        services.AddOptions<IdentityOptions>().Configure<AdminUserOptions>((identity, users) => users.Apply(identity));
        services.AddIdentityCore<CmsUser>()
            .AddUserStore<CmsUserStore>()
            .AddUserManager<CmsUserManager>()
            .AddSignInManager<CmsSignInManager>();

        services.AddScoped<IdentityChanges>();
        services.AddScoped<InvitationService>();
        services.TryAddScoped<IPrincipalRoles, UserPrincipalRoles>();

        services.TryAddSingleton<IEmailSender>(provider =>
            provider.GetService<IHostEnvironment>()?.IsDevelopment() == true
                ? new LogEmailSender(provider.GetRequiredService<ILogger<LogEmailSender>>())
                : new UnconfiguredEmailSender());
    }
}
