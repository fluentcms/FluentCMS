using FluentCMS.Core.DependencyInjection;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FluentCMS.Core.Identity.Members;

// The member pool of spec 4.7: its options, its own Identity managers on its own options, its sign-in cookie and the
// services of its APIs and pages. Nothing here is the admin pool's: a member's manager, sign-in manager, token providers
// and cookie are types and schemes of their own.
internal static class MemberRegistration
{
    public static void Add(IServiceCollection services, MemberOptions options)
    {
        services.AddFluentCmsOptions(MemberOptions.Section, MemberErrorCodes.InvalidOptions, options, members => members.Validate());
        services.AddSingleton<MemberIdentityOptions>();
        services.AddOptions<MemberEmailTokenProviderOptions>().Configure<MemberOptions>((token, members) =>
        {
            token.Name = MemberEmailTokenProvider.ProviderName;
            token.TokenLifespan = members.EmailVerificationLifetime;
        });
        services.AddOptions<MemberPasswordResetTokenProviderOptions>().Configure<MemberOptions>((token, members) =>
        {
            token.Name = MemberPasswordResetTokenProvider.ProviderName;
            token.TokenLifespan = members.PasswordResetLifetime;
        });

        // The cookie of the member pages, which only they and the authorize endpoint read.
        services.AddAuthentication().AddCookie(MemberAuthentication.CookieScheme, cookie =>
        {
            cookie.Cookie.Name = MemberAuthentication.CookieName;
            cookie.LoginPath = HostedPagePaths.MemberLogin;
            cookie.LogoutPath = HostedPagePaths.MemberLogout;
            cookie.AccessDeniedPath = HostedPagePaths.MemberLogin;
            cookie.SlidingExpiration = true;
        });
        services.AddOptions<CookieAuthenticationOptions>(MemberAuthentication.CookieScheme)
            .Configure<ClientOptions>((cookie, clients) => cookie.ExpireTimeSpan = clients.SignInCookieLifetime);

        // The hasher of the admin pool, PBKDF2 behind the replaceable interface (Q3).
        services.TryAddScoped<IPasswordHasher<CmsMember>, PasswordHasher<CmsMember>>();
        services.AddScoped<MemberEmailTokenProvider>();
        services.AddScoped<MemberPasswordResetTokenProvider>();
        services.AddScoped<MemberStore>();
        services.AddScoped<MemberManager>();
        services.TryAddScoped<UserManager<CmsMember>>(provider => provider.GetRequiredService<MemberManager>());
        services.AddSingleton<MemberClaimsPrincipalFactory>();
        services.AddScoped<MemberSignInEvents>();
        services.AddScoped<MemberSignInManager>();
        services.TryAddScoped<SignInManager<CmsMember>>(provider => provider.GetRequiredService<MemberSignInManager>());

        services.AddSingleton<MemberMailLimiter>();
        services.AddScoped<MemberMail>();
        services.AddScoped<MemberSessions>();
        services.AddScoped<MemberAccountService>();
        services.AddScoped<MemberGroupService>();
        services.AddScoped<MemberAdministration>();
        services.AddHostedService<UnverifiedMembersScheduleService>();
    }
}
