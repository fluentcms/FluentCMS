using System.Data.Common;
using System.Net;
using System.Runtime.CompilerServices;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Events;
using FluentCMS.Sites;
using FluentCMS.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.MultiSite.Tests.MultiSiteTesting;

namespace FluentCMS.Plugins.MultiSite.Tests;

// The map of host names is kept on each instance for 30 seconds. Two hosts on one database file are two instances: the
// one that writes drops its copy at once, and the other reads the change when its copy expires.
public sealed class HostMapTests : IAsyncLifetime
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("fluentcms-multi-site-db-");
    private readonly Instance _writer;
    private readonly Instance _other;

    public HostMapTests()
    {
        var file = Path.Combine(_directory.FullName, "cms.db");
        _writer = new Instance(file);
        _other = new Instance(file);
    }

    // One after the other, so that the first has applied the migrations when the second starts.
    public ValueTask InitializeAsync()
    {
        _ = _writer.Host.Services;
        _ = _other.Host.Services;
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _writer.DisposeAsync();
        await _other.DisposeAsync();
        try
        {
            _directory.Delete(recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task AHostChangeIsSeenAtOnceByTheInstanceThatWroteItAndWithin30SecondsByAnother()
    {
        using var admin = await AdminAsync(_writer.Host);
        var site = await CreateSiteAsync(admin);
        var name = HostName();
        (await SiteOfAsync(_writer.Host, name)).ShouldBe(DefaultSite.Id);
        (await SiteOfAsync(_other.Host, name)).ShouldBe(DefaultSite.Id);

        await AddHostAsync(admin, site, name);

        (await SiteOfAsync(_writer.Host, name)).ShouldBe(site);
        (await SiteOfAsync(_other.Host, name)).ShouldBe(DefaultSite.Id);
        _other.Host.Time.Advance(SiteHostMap.CacheDuration - TimeSpan.FromSeconds(1));
        (await SiteOfAsync(_other.Host, name)).ShouldBe(DefaultSite.Id);
        _other.Host.Time.Advance(TimeSpan.FromSeconds(1));
        (await SiteOfAsync(_other.Host, name)).ShouldBe(site);

        await SendAsync(admin, HttpMethod.Delete, $"{SitesRoute}/{site}/hosts/{name}", expected: HttpStatusCode.NoContent);

        (await SiteOfAsync(_writer.Host, name)).ShouldBe(DefaultSite.Id);
        (await SiteOfAsync(_other.Host, name)).ShouldBe(site);
        _other.Host.Time.Advance(SiteHostMap.CacheDuration);
        (await SiteOfAsync(_other.Host, name)).ShouldBe(DefaultSite.Id);
    }

    [Fact]
    public async Task AHostChangeRaisesSiteUpdatedEventForItsSite()
    {
        using var admin = await AdminAsync(_writer.Host);
        var site = await CreateSiteAsync(admin);
        var slug = (await SendAsync(admin, HttpMethod.Get, $"{SitesRoute}/{site}"))!["slug"]!.GetValue<string>();
        var name = HostName();
        _writer.Updates.Clear();

        await AddHostAsync(admin, site, name);
        await SendAsync(admin, HttpMethod.Delete, $"{SitesRoute}/{site}/hosts/{name}", expected: HttpStatusCode.NoContent);

        _writer.Updates.Select(update => (update.SiteId, update.Slug, update.Source.SiteId)).ShouldBe([(site, slug, site), (site, slug, site)]);
    }

    // A site that was deleted without its hosts being removed, as when the instance stops in between: its host name reads
    // the default site, and another site may take the name, which is seen as any host change is, whatever the instances
    // keep of the site that is gone.
    [Fact]
    public async Task AHostNameLeftBehindByADeletedSiteReadsTheDefaultSiteAndIsFreeForAnother()
    {
        using var admin = await AdminAsync(_writer.Host);
        var site = await CreateSiteAsync(admin);
        var name = HostName();
        await AddHostAsync(admin, site, name);
        await DeleteLeavingTheHostsAsync(site);

        (await SiteOfAsync(_writer.Host, name)).ShouldBe(DefaultSite.Id);
        (await SiteOfAsync(_other.Host, name)).ShouldBe(DefaultSite.Id);

        var other = await CreateSiteAsync(admin);
        await AddHostAsync(admin, other, name);
        (await SiteOfAsync(_writer.Host, name)).ShouldBe(other);
        (await SiteOfAsync(_other.Host, name)).ShouldBe(DefaultSite.Id);
        _other.Host.Time.Advance(SiteHostMap.CacheDuration);
        (await SiteOfAsync(_other.Host, name)).ShouldBe(other);
    }

    // That the site of a host name is gone is kept as long as that a site exists would be, on every instance, so a request
    // on such a host name reads no site until then.
    [Fact]
    public async Task AHostNameLeftBehindByADeletedSiteReadsNoSiteWhileItsAbsenceIsKept()
    {
        using var admin = await AdminAsync(_writer.Host);
        var site = await CreateSiteAsync(admin);
        var name = HostName();
        await AddHostAsync(admin, site, name);
        await DeleteLeavingTheHostsAsync(site);

        foreach (var instance in new[] { _writer, _other })
        {
            (await instance.SiteReads.CountAsync(() => SiteOfAsync(instance.Host, name))).ShouldBeGreaterThan(0);
            (await instance.SiteReads.CountAsync(() => SiteOfAsync(instance.Host, name))).ShouldBe(0);
            instance.Host.Time.Advance(SiteCache.CacheDuration - TimeSpan.FromSeconds(1));
            (await instance.SiteReads.CountAsync(() => SiteOfAsync(instance.Host, name))).ShouldBe(0);
            instance.Host.Time.Advance(TimeSpan.FromSeconds(1));
            (await instance.SiteReads.CountAsync(() => SiteOfAsync(instance.Host, name))).ShouldBeGreaterThan(0);
        }
    }

    // A host in the stored form is found as it is, and one that no site has is found in that form to be no one's, so
    // neither puts anything on the heap once the map is read.
    [Fact]
    public async Task AHostNameInTheStoredFormIsLookedUpWithoutAllocating()
    {
        using var admin = await AdminAsync(_writer.Host);
        var site = new SiteId(await CreateSiteAsync(admin));
        var name = HostName();
        var unknown = HostName();
        await AddHostAsync(admin, site.Value, name);
        var map = _writer.Host.Services.GetRequiredService<SiteHostMap>();
        var token = Token;
        (await map.ResolveAsync(name, token)).ShouldBe(site);
        Lookups(map, site, name, unknown, token).ShouldBeTrue();

        var before = GC.GetAllocatedBytesForCurrentThread();
        var found = Lookups(map, site, name, unknown, token);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        found.ShouldBeTrue();
        allocated.ShouldBe(0);

        static bool Lookups(SiteHostMap map, SiteId site, string name, string unknown, CancellationToken token)
        {
            var found = true;
            for (var i = 0; i < 100; i++)
            {
                var known = map.ResolveAsync(name, token);
                var none = map.ResolveAsync(unknown, token);
                found &= known.IsCompletedSuccessfully && known.Result == site && none.IsCompletedSuccessfully && none.Result is null;
            }

            return found;
        }
    }

    private async Task DeleteLeavingTheHostsAsync(Guid site)
    {
        await using var scope = _writer.Host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SiteManager>().DeleteAsync(site, Token);
    }

    private static async Task<Guid> SiteOfAsync(FluentCmsTestHost host, string hostname)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<ISiteResolver>().ResolveAsync(new SiteRequest { Host = hostname }, Token)).Value;
    }

    private sealed class Instance : MultiSiteHost
    {
        public Instance(string file)
            : base(file)
        {
            Host.WithServices(services => services.AddSingleton(Updates)).Configure(cms =>
            {
                cms.Events.AddHandler<SiteUpdatedEvent, Recorder>();
                cms.Services.ConfigureDbContext<CmsDbContext>(options => options.AddInterceptors(SiteReads));
            });
        }

        public List<SiteUpdatedEvent> Updates { get; } = [];

        public SiteReads SiteReads { get; } = new();
    }

    // The commands of core's context that read cms_site, counted for one call and none that runs beside it.
    private sealed class SiteReads : DbCommandInterceptor
    {
        private readonly AsyncLocal<StrongBox<int>?> _reads = new();

        public async Task<int> CountAsync(Func<Task> call)
        {
            var reads = new StrongBox<int>();
            _reads.Value = reads;
            await call();
            return Volatile.Read(ref reads.Value);
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Count(command);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Count(command);
            return ValueTask.FromResult(result);
        }

        private void Count(DbCommand command)
        {
            if (_reads.Value is { } reads && command.CommandText.Contains("\"cms_site\"", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref reads.Value);
            }
        }
    }

    private sealed class Recorder(List<SiteUpdatedEvent> updates) : IEventHandler<SiteUpdatedEvent>
    {
        public ValueTask HandleAsync(SiteUpdatedEvent message, CancellationToken cancellationToken)
        {
            updates.Add(message);
            return ValueTask.CompletedTask;
        }
    }
}
