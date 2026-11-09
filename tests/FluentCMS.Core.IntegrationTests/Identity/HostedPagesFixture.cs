using FluentCMS.Core.Api.Common;
using FluentCMS.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.IntegrationTests.Identity;

// The hosts the hosted pages tests share, each started on its first use: one for the tests that do not configure a host of
// their own, and one with the providers of a fake OpenID Connect provider. A test signs in many times from few addresses,
// and the buckets refill only when the test clock moves, so the auth limit is out of the way; a test of the limit has a
// host of its own. A test tells its rows apart from the others' by its own users and addresses.
public sealed class HostedPagesFixture : IAsyncDisposable
{
    public HostedPagesFixture()
    {
        Host = HostedPagesTests.NewHost(Emails)
            .WithServices(services => services.PostConfigure<RateLimitOptions>(limits => limits.Auth.PermitLimit = 10_000));
        Sso = HostedPagesTests.SsoHost(Emails, Idp);
    }

    public FluentCmsTestHost Host { get; }

    public FluentCmsTestHost Sso { get; }

    internal HostedPagesTests.CapturedEmails Emails { get; } = new();

    internal FakeOidcProvider Idp { get; } = new();

    public async ValueTask DisposeAsync()
    {
        await Host.DisposeAsync();
        await Sso.DisposeAsync();
        Idp.Dispose();
    }
}
