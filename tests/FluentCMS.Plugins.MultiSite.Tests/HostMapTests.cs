using System.Net;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Events;
using FluentCMS.Sites;
using FluentCMS.Testing;
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
    // the default site, and another site may take the name.
    [Fact]
    public async Task AHostNameLeftBehindByADeletedSiteReadsTheDefaultSiteAndIsFreeForAnother()
    {
        using var admin = await AdminAsync(_writer.Host);
        var site = await CreateSiteAsync(admin);
        var name = HostName();
        await AddHostAsync(admin, site, name);
        await using (var scope = _writer.Host.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<SiteManager>().DeleteAsync(site, Token);
        }

        (await SiteOfAsync(_writer.Host, name)).ShouldBe(DefaultSite.Id);

        var other = await CreateSiteAsync(admin);
        await AddHostAsync(admin, other, name);
        (await SiteOfAsync(_writer.Host, name)).ShouldBe(other);
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
            Host.WithServices(services => services.AddSingleton(Updates)).Configure(cms => cms.Events.AddHandler<SiteUpdatedEvent, Recorder>());
        }

        public List<SiteUpdatedEvent> Updates { get; } = [];
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
