using FluentCMS.Core.Api.Common;
using FluentCMS.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.IntegrationTests.Identity;

// The host the authorization server tests share, for the tests that do not configure one of their own. Every test signs in
// through the same client address, and the buckets refill only when the test clock moves, so the auth limit is out of the
// way.
public sealed class AuthorizationServerFixture : IAsyncDisposable
{
    public FluentCmsTestHost Host { get; } = AuthorizationServerTests.NewHost()
        .WithServices(services => services.PostConfigure<RateLimitOptions>(limits => limits.Auth.PermitLimit = 10_000));

    public ValueTask DisposeAsync() => Host.DisposeAsync();
}
