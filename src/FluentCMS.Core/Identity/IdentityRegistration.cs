using FluentCMS.Core.Authorization;
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
        options.Validate();
        services.AddSingleton(options);

        // The sign-in manager reads the schemes; the authorization server adds them.
        services.AddAuthentication();
        services.AddIdentityCore<CmsUser>(options.Apply)
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
