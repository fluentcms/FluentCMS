using System.Net;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Sites;
using FluentCMS.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Delivery;

// The output cache keys a Delivery read by the site of its host name before the endpoint runs, and the endpoint reads that
// same site, so the host resolvers are asked once for a read, whether it is served from the cache or not.
public sealed class DeliveryHostSiteTests : IAsyncDisposable
{
    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    private readonly CountingHosts _hosts = new();
    private readonly FluentCmsTestHost _host;

    public DeliveryHostSiteTests()
    {
        _host = new FluentCmsTestHost()
            .WithContentTypes<Note, Topic>()
            .WithServices(services => services.AddSingleton<ISiteHostResolver>(_hosts))
            .Seed(async services =>
            {
                var context = services.GetRequiredService<CmsDbContext>();
                context.Set<RoleGrant>().Add(new RoleGrant { Id = Guid.CreateVersion7(), RoleId = DefaultSite.PublicRole, Permission = "content.note.read" });
                await context.SaveChangesAsync();
            });
    }

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    [Fact]
    public async Task ADeliveryReadResolvesItsHostNameOnce()
    {
        using var client = _host.CreateClient();

        using (var stored = await client.GetAsync(new Uri("/api/delivery/v1/note", UriKind.Relative), Token))
        {
            stored.StatusCode.ShouldBe(HttpStatusCode.OK);
            stored.Headers.Age.ShouldBeNull();
        }

        _hosts.Asked.ShouldBe(1);

        using (var served = await client.GetAsync(new Uri("/api/delivery/v1/note", UriKind.Relative), Token))
        {
            served.StatusCode.ShouldBe(HttpStatusCode.OK);
            served.Headers.Age.ShouldNotBeNull();
        }

        _hosts.Asked.ShouldBe(2);
    }

    private sealed class CountingHosts : ISiteHostResolver
    {
        private int _asked;

        public int Asked => Volatile.Read(ref _asked);

        public ValueTask<SiteId?> ResolveAsync(string host, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _asked);
            return ValueTask.FromResult<SiteId?>(null);
        }
    }
}
