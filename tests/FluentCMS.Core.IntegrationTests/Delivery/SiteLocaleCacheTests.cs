using System.Net;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using FluentCMS.Core.Api.Delivery;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Events;
using FluentCMS.Core.IntegrationTests.Management;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Localization;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Events;
using FluentCMS.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Delivery;

// A site's locales decide what a fallback read gives, so a change to them evicts the site's copies. The site has en, fr and
// fr-CA, whose implicit chain is fr then en, and a page in en and fr. The server keeps a copy for ten minutes here, so only an
// eviction explains a fresh read.
public sealed class SiteLocaleCacheTests : IAsyncLifetime
{
    private static readonly string SiteTag = $"site:{DefaultSite.Id:D}";

    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    private readonly Invalidations _invalidations = new();
    private readonly FluentCmsTestHost _host;
    private HttpClient _client = null!;
    private TestActor _admin = null!;
    private string _path = null!;

    public SiteLocaleCacheTests()
    {
        _host = new FluentCmsTestHost()
            .WithContentTypes<Page, Person>()
            .WithServices(services =>
            {
                services.AddSingleton(_invalidations);
                services.PostConfigure<DeliveryCacheOptions>(cache => cache.OutputCacheDuration = TimeSpan.FromMinutes(10));
            })
            .Configure(cms =>
            {
                cms.Events.AddHandler<CacheInvalidatedEvent, InvalidationRecorder>();
                cms.Jobs.Options.Queues[JobRunnerOptions.DefaultQueue] = 0;
                cms.Jobs.Options.Queues[OutboxDrainJob.Queue] = 0;
            });
    }

    public async ValueTask InitializeAsync()
    {
        await using (var scope = _host.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
            context.Set<RoleGrant>().Add(new RoleGrant { Id = Guid.CreateVersion7(), RoleId = DefaultSite.PublicRole, Permission = "content.page.read" });
            await context.SaveChangesAsync(Token);
        }

        (await InvalidatedAsync()).ShouldBe([SiteTag]);
        await EnsureLocaleAsync("fr");
        (await InvalidatedAsync()).ShouldBe([SiteTag]);
        await EnsureLocaleAsync("fr-CA");
        (await InvalidatedAsync()).ShouldBe([SiteTag]);

        _admin = _host.As(await _host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token));
        var page = await _admin.Content<Page>().CreateAsync(new Page { Title = "Hello" }, Token);
        await _admin.Content<Page>().PublishAsync(page.Id, cancellationToken: Token);
        await InvalidatedAsync();
        await _admin.In("fr").Content<Page>().UpdateAsync(page.Id, new Page { Title = "Bonjour" }, 0, Token);
        await _admin.In("fr").Content<Page>().PublishAsync(page.Id, cancellationToken: Token);
        await InvalidatedAsync();

        _path = $"page/{page.Id}?locale=";
        _client = _host.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task AddingALocaleWithAFallbackEvictsTheSiteAndTheNewLocaleReadsItsFallback()
    {
        await CachedAsync("fr-CA");

        await SitesAsync(sites => sites.AddLocaleAsync(DefaultSite.Id, "de", null, true, ["fr"], Token));

        (await InvalidatedAsync()).ShouldBe([SiteTag]);
        (await GetAsync("fr-CA")).FromCache.ShouldBeFalse();
        (await GetAsync("de")).ShouldRead("Bonjour", from: "fr");
    }

    [Fact]
    public async Task DisablingAndEnablingALocaleChangesWhatTheNextReadFallsBackTo()
    {
        (await CachedAsync("fr-CA")).ShouldRead("Bonjour", from: "fr");

        await SitesAsync(sites => sites.UpdateLocaleAsync(DefaultSite.Id, "fr", new SiteLocaleChange { Enabled = false }, Token));
        (await InvalidatedAsync()).ShouldBe([SiteTag]);
        (await CachedAsync("fr-CA")).ShouldRead("Hello", from: "en");

        await SitesAsync(sites => sites.UpdateLocaleAsync(DefaultSite.Id, "fr", new SiteLocaleChange { Enabled = true }, Token));
        (await InvalidatedAsync()).ShouldBe([SiteTag]);
        (await GetAsync("fr-CA")).ShouldRead("Bonjour", from: "fr");
    }

    [Fact]
    public async Task ChangingTheFallbacksOfALocaleChangesTheNextRead()
    {
        await CachedAsync("fr-CA");

        await SitesAsync(sites => sites.UpdateLocaleAsync(DefaultSite.Id, "fr-CA", new SiteLocaleChange { SetsFallbacks = true, Fallbacks = ["en"] }, Token));

        (await InvalidatedAsync()).ShouldBe([SiteTag]);
        (await GetAsync("fr-CA")).ShouldRead("Hello", from: "en");
    }

    [Fact]
    public async Task RemovingALocaleEvictsTheSite()
    {
        await CachedAsync("fr-CA");

        await SitesAsync(async sites =>
        {
            await sites.RemoveLocaleAsync(DefaultSite.Id, "fr-CA", Token);
            return true;
        });

        (await InvalidatedAsync()).ShouldBe([SiteTag]);
        var removed = await GetAsync("fr-CA");
        removed.Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
        removed.Json["code"]!.GetValue<string>().ShouldBe(LocaleErrorCodes.LocaleNotAllowed);
    }

    [Fact]
    public async Task ALocaleTheHostEnsuresEvictsTheSite()
    {
        await CachedAsync("fr-CA");

        await EnsureLocaleAsync("de");

        (await InvalidatedAsync()).ShouldBe([SiteTag]);
        (await GetAsync("fr-CA")).FromCache.ShouldBeFalse();
    }

    // A delivered eviction would be done by the time the drain returns.
    [Fact]
    public async Task AChangeThatChangesNothingKeepsTheCopies()
    {
        await CachedAsync("fr-CA");

        await SitesAsync(sites => sites.UpdateLocaleAsync(DefaultSite.Id, "fr", new SiteLocaleChange { Enabled = true }, Token));
        await EnsureLocaleAsync("fr");
        await _host.Services.GetRequiredService<OutboxDrainer>().DrainAsync(Token);

        (await GetAsync("fr-CA")).FromCache.ShouldBeTrue();
    }

    // Read twice, so the second answer is the cached copy.
    private async Task<Reply> CachedAsync(string locale)
    {
        await GetAsync(locale);
        var cached = await GetAsync(locale);
        cached.FromCache.ShouldBeTrue();
        return cached;
    }

    private async Task EnsureLocaleAsync(string code)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SiteManager>().EnsureLocaleAsync(DefaultSite.Id, code, Token);
    }

    private Task<T> SitesAsync<T>(Func<SiteManager, Task<T>> change) => _admin.RunAsync(change);

    private async Task<IReadOnlyList<string>> InvalidatedAsync()
    {
        await _host.Services.GetRequiredService<OutboxDrainer>().DrainAsync(Token);
        return await _invalidations.NextAsync(Token);
    }

    private async Task<Reply> GetAsync(string locale)
    {
        using var response = await _client.GetAsync(new Uri("/api/delivery/v1/" + _path + locale, UriKind.Relative), Token);
        return new Reply(response.StatusCode, response.Headers.Age is not null, JsonNode.Parse(await response.Content.ReadAsStringAsync(Token))!);
    }

    private sealed record Reply(HttpStatusCode Status, bool FromCache, JsonNode Json)
    {
        public void ShouldRead(string title, string from)
        {
            Status.ShouldBe(HttpStatusCode.OK);
            Json["title"]!.GetValue<string>().ShouldBe(title);
            Json["fallbacks"]!["title"]!.GetValue<string>().ShouldBe(from);
        }
    }

    private sealed class Invalidations
    {
        private readonly Channel<IReadOnlyList<string>> _tags = Channel.CreateUnbounded<IReadOnlyList<string>>();

        public ValueTask AddAsync(IReadOnlyList<string> tags) => _tags.Writer.WriteAsync(tags);

        public async Task<IReadOnlyList<string>> NextAsync(CancellationToken cancellationToken)
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(TimeSpan.FromSeconds(30));
            return await _tags.Reader.ReadAsync(limit.Token);
        }
    }

    private sealed class InvalidationRecorder(Invalidations invalidations) : IEventHandler<CacheInvalidatedEvent>
    {
        public ValueTask HandleAsync(CacheInvalidatedEvent message, CancellationToken cancellationToken) => invalidations.AddAsync(message.Tags);
    }
}
