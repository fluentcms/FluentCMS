using AspNet.Security.OAuth.Apple;
using AspNet.Security.OAuth.GitHub;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Identity.Members;
using FluentCMS.Core.IntegrationTests.Diagnostics;
using FluentCMS.Core.IntegrationTests.Identity;
using FluentCMS.Core.Storage;
using FluentCMS.Providers;
using FluentCMS.Testing;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.MicrosoftAccount;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Members;

// The host the member provider tests share: the member host of MemberFixture with one provider of each type, all of
// them answered in the process, so no test calls a provider. A test tells its rows apart by its own addresses.
public sealed class MemberProviderFixture : IAsyncLifetime
{
    public const string Google = "google";
    public const string MicrosoftAccount = "microsoft";
    public const string GitHub = "github";
    public const string Apple = "apple";

    // A generic OIDC provider, and one that links a member by an address the provider says is verified.
    public const string Corp = "corp";
    public const string Partner = "partner";

    public MemberProviderFixture()
    {
        Host = NewHost(Providers, Oidc, Emails, Logs);
    }

    public FluentCmsTestHost Host { get; }

    internal FakeMemberProviders Providers { get; } = new();

    internal FakeOidcProvider Oidc { get; } = new("https://members-idp.test");

    internal HostedPagesTests.CapturedEmails Emails { get; } = new();

    internal LogCapture Logs { get; } = new();

    internal static FluentCmsTestHost NewHost(
        FakeMemberProviders providers, FakeOidcProvider oidc, IEmailSender emails, ILoggerProvider? logs = null, Action<FluentCmsBuilder>? configure = null) =>
        MemberFixture.NewHost(emails, logs, cms =>
            {
                cms.Members.Providers[Google] = new MemberProvider { Type = MemberProviderTypes.Google, ClientId = FakeMemberProviders.GoogleClientId, ClientSecret = providers.ClientSecret };
                cms.Members.Providers[MicrosoftAccount] = new MemberProvider { Type = MemberProviderTypes.Microsoft, ClientId = FakeMemberProviders.MicrosoftClientId, ClientSecret = providers.ClientSecret };
                cms.Members.Providers[GitHub] = new MemberProvider { Type = MemberProviderTypes.GitHub, ClientId = FakeMemberProviders.GitHubClientId, ClientSecret = providers.ClientSecret };
                cms.Members.Providers[Apple] = new MemberProvider
                {
                    Type = MemberProviderTypes.Apple,
                    ClientId = FakeMemberProviders.AppleClientId,
                    TeamId = FakeMemberProviders.AppleTeamId,
                    KeyId = FakeMemberProviders.AppleKeyId,
                    PrivateKey = providers.ApplePrivateKey,
                };
                cms.Members.Providers[Corp] = new MemberProvider
                {
                    Type = MemberProviderTypes.Oidc,
                    DisplayName = "Corp",
                    Authority = oidc.Issuer,
                    ClientId = FakeOidcProvider.ClientId,
                    ClientSecret = FakeOidcProvider.ClientSecret,
                };
                cms.Members.Providers[Partner] = new MemberProvider
                {
                    Type = MemberProviderTypes.Oidc,
                    DisplayName = "Partner",
                    Authority = oidc.Issuer,
                    ClientId = FakeOidcProvider.ClientId,
                    ClientSecret = FakeOidcProvider.ClientSecret,
                    LinkVerifiedEmail = true,
                };
                configure?.Invoke(cms);
            })
            .WithServices(services =>
            {
                services.ConfigureAll<GoogleOptions>(options => options.BackchannelHttpHandler = providers.Handler);
                services.ConfigureAll<MicrosoftAccountOptions>(options => options.BackchannelHttpHandler = providers.Handler);
                services.ConfigureAll<GitHubAuthenticationOptions>(options => options.BackchannelHttpHandler = providers.Handler);
                services.ConfigureAll<AppleAuthenticationOptions>(options => options.BackchannelHttpHandler = providers.Handler);
                services.ConfigureAll<OpenIdConnectOptions>(options => options.BackchannelHttpHandler = oidc.Handler);
            });

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        await Host.DisposeAsync();
        Providers.Dispose();
        Oidc.Dispose();
    }

    internal static async Task<CmsMember?> MemberAsync(FluentCmsTestHost host, string address)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<MemberManager>().FindByEmailAsync(address);
    }

    internal static async Task<IList<UserLoginInfo>> LoginsAsync(FluentCmsTestHost host, Guid memberId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<CmsMemberLogin>().AsNoTracking()
            .Where(login => login.UserId == memberId)
            .OrderBy(login => login.LoginProvider)
            .Select(login => new UserLoginInfo(login.LoginProvider, login.ProviderKey, login.ProviderDisplayName))
            .ToListAsync(TestContext.Current.CancellationToken);
    }
}
