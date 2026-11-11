using System.Net;
using FluentCMS.Providers;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.MultiSite.Tests.MultiSiteTesting;

namespace FluentCMS.Plugins.MultiSite.Tests;

// The writes of host names run one at a time under a lock. On this host the lock is never free, as when another instance
// is in the middle of a write and does not finish.
public sealed class HostNamesBusyTests(HostNamesBusyTests.BusyHost fixture) : IClassFixture<HostNamesBusyTests.BusyHost>
{
    [Fact]
    public async Task AWriteThatCannotTakeTheLockIsRefusedAsBusyAndChangesNothing()
    {
        using var admin = await AdminAsync(fixture.Host);
        var site = await CreateSiteAsync(admin);
        var name = HostName();

        var busy = await AddHostAsync(admin, site, name, expected: HttpStatusCode.ServiceUnavailable);

        Code(busy).ShouldBe("FCMS1666");
        busy!["status"]!.GetValue<int>().ShouldBe(503);
        Code(await SendAsync(admin, HttpMethod.Patch, $"{SitesRoute}/{site}/hosts/{name}", new { isPrimary = true }, HttpStatusCode.ServiceUnavailable)).ShouldBe("FCMS1666");
        Code(await SendAsync(admin, HttpMethod.Delete, $"{SitesRoute}/{site}/hosts/{name}", expected: HttpStatusCode.ServiceUnavailable)).ShouldBe("FCMS1666");
        (await SendAsync(admin, HttpMethod.Get, $"{SitesRoute}/{site}/hosts"))!["items"]!.AsArray().ShouldBeEmpty();
    }

    public sealed class BusyHost() : MultiSiteHost(null, cms => cms.Providers.Use<IDistributedLock, HeldHostNames>());

    // Every other lock of the host is free.
    private sealed class HeldHostNames : IDistributedLock
    {
        public Task<IDistributedLockHandle?> TryAcquireAsync(string name, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            Task.FromResult<IDistributedLockHandle?>(name == SiteHostService.LockName ? null : new Held(name));

        private sealed class Held(string name) : IDistributedLockHandle
        {
            public string Name => name;

            public CancellationToken Lost => CancellationToken.None;

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
