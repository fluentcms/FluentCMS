using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Api.Common;
using FluentCMS.Core.Api.Delivery;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Events;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Core.Telemetry;
using FluentCMS.Events;
using FluentCMS.Schema;
using FluentCMS.Sites;
using FluentCMS.Testing;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Delivery;

// The cache headers and the output cache of the Delivery API. The tests share a host, and each reads a site of its own, so
// that what one test caches cannot answer another.
public sealed class DeliveryCacheTests(DeliveryCacheFixture fixture) : IClassFixture<DeliveryCacheFixture>, IAsyncLifetime
{
    private const string PrivateCacheControl = "max-age=60, private, stale-while-revalidate=300";

    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    private readonly FluentCmsTestHost _host = fixture.Host;
    private readonly DeliveryCacheFixture.Evictions _invalidations = fixture.Invalidations;
    private Guid _site;
    private Guid _publicRole;
    private HttpClient _client = null!;
    private TestActor _editor = null!;
    private Note _first = null!;

    private string SiteTag => $"site:{_site:D}";

    public async ValueTask InitializeAsync()
    {
        await using (var scope = _host.Services.CreateAsyncScope())
        {
            _site = (await scope.ServiceProvider.GetRequiredService<SiteManager>().CreateAsync("Cache", $"cache-{Guid.NewGuid():N}", DefaultSite.Locale, Token)).Id;
            var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
            _publicRole = (await context.Set<Role>().SingleAsync(role => role.SiteId == _site && role.Name == BuiltInRoles.PublicName, Token)).Id;
            context.Set<RoleGrant>().AddRange(
                new RoleGrant { Id = Guid.CreateVersion7(), RoleId = _publicRole, Permission = "content.note.read" },
                new RoleGrant { Id = Guid.CreateVersion7(), RoleId = _publicRole, Permission = "content.topic.read" });
            await context.SaveChangesAsync(Token);
        }

        // A change to a role's grants evicts its site; that and whatever an earlier test left are not this test's to wait for.
        await DrainAsync();
        _invalidations.Forget();
        _editor = _host.As(await _host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token)).In(new SiteId(_site));
        _first = await PublishedAsync(new Note { Title = "First", Slug = "first" });
        _client = OfTheSite(_host.CreateClient());
    }

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }

    // No date in a body moves when an entry leaves a list or a role changes what the caller reads, so none is sent.
    [Fact]
    public async Task AReadCarriesAnETagAndTheConfiguredCacheControlAndNoLastModified()
    {
        foreach (var path in new[] { $"note/{_first.Id}", "note/by/slug/first", "note" })
        {
            var reply = await GetAsync(path);

            reply.Status.ShouldBe(HttpStatusCode.OK);
            reply.ETag.ShouldNotBeNull().ShouldStartWith("\"");
            reply.CacheControl.ShouldBe(DeliveryCacheOptions.DefaultCacheControl);
            reply.Vary.ShouldBe(DeliveryResponse.Vary);
            reply.LastModified.ShouldBeNull();
        }
    }

    // CDNs and uptime checks send HEAD, which minimal APIs would answer 405 on a GET route.
    [Fact]
    public async Task AHeadHasTheHeadersOfTheGetAndNoBody()
    {
        foreach (var path in new[] { $"note/{_first.Id}", "note/by/slug/first", "note" })
        {
            var get = await GetAsync(path);
            var head = await HeadAsync(path);

            head.Status.ShouldBe(HttpStatusCode.OK, path);
            head.Body.ShouldBeEmpty();
            head.ContentLength.ShouldBe(Encoding.UTF8.GetByteCount(get.Body));
            head.ETag.ShouldBe(get.ETag);
            head.LastModified.ShouldBe(get.LastModified);
            head.CacheControl.ShouldBe(get.CacheControl);
            head.Vary.ShouldBe(get.Vary);
        }
    }

    [Fact]
    public async Task ARepeatedHeadIsServedFromTheOutputCache()
    {
        var first = await HeadAsync($"note/{_first.Id}");
        var second = await HeadAsync($"note/{_first.Id}");
        var get = await GetAsync($"note/{_first.Id}");

        first.FromCache.ShouldBeFalse();
        second.FromCache.ShouldBeTrue();
        second.ContentLength.ShouldBe(first.ContentLength);
        second.Body.ShouldBeEmpty();
        get.FromCache.ShouldBeFalse();
        get.Body.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task TheETagChangesWhenANewVersionIsPublished()
    {
        var before = await GetAsync($"note/{_first.Id}");

        await _editor.Content<Note>().PatchAsync(_first.Id, new ContentDocument { ["title"] = "Changed" }, _first.Version, Token);
        await _editor.Content<Note>().PublishAsync(_first.Id, cancellationToken: Token);
        await InvalidatedAsync();
        var after = await GetAsync($"note/{_first.Id}");

        after.ETag.ShouldNotBeNull().ShouldNotBe(before.ETag);
    }

    [Fact]
    public async Task ARepeatedRequestIsServedFromTheOutputCache()
    {
        ConcurrentBag<string?> lookups = [];
        var both = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var listener = ListenToCacheLookups(result =>
        {
            lookups.Add(result);
            if (lookups.Contains("miss") && lookups.Contains("hit"))
            {
                both.TrySetResult();
            }
        });

        var first = await GetAsync($"note/{_first.Id}");
        var second = await GetAsync($"note/{_first.Id}");

        first.FromCache.ShouldBeFalse();
        second.FromCache.ShouldBeTrue();
        second.Body.ShouldBe(first.Body);
        second.ETag.ShouldBe(first.ETag);
        second.CacheControl.ShouldBe(first.CacheControl);

        // A lookup is counted when its response completes, which can be after the client has read it.
        await both.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
    }

    [Fact]
    public async Task AMatchingIfNoneMatchIsNotModifiedAndAnotherIsAnsweredInFull()
    {
        var path = $"note/{_first.Id}";
        var full = await GetAsync(path);
        var bearer = await GetAsync(path, Bearer(TokenScopes.Delivery));

        var cached = await GetAsync(path, ("If-None-Match", full.ETag!));
        var any = await GetAsync(path, ("If-None-Match", "*"));
        var other = await GetAsync(path, ("If-None-Match", "\"other\""));
        var uncached = await GetAsync(path, [.. Bearer(TokenScopes.Delivery), ("If-None-Match", bearer.ETag!)]);

        cached.Status.ShouldBe(HttpStatusCode.NotModified);
        cached.Body.ShouldBeEmpty();
        cached.ETag.ShouldBe(full.ETag);
        cached.CacheControl.ShouldBe(full.CacheControl);
        any.Status.ShouldBe(HttpStatusCode.NotModified);
        other.Status.ShouldBe(HttpStatusCode.OK);
        other.Body.ShouldBe(full.Body);
        uncached.Status.ShouldBe(HttpStatusCode.NotModified);
        uncached.ETag.ShouldBe(bearer.ETag);
        uncached.CacheControl.ShouldBe(PrivateCacheControl);
    }

    // A date later than any copy would have the output cache answer 304 by the date it stored the copy, if it were asked.
    [Fact]
    public async Task IfModifiedSinceAloneIsServedFromTheCacheInFull()
    {
        var path = $"note/{_first.Id}";
        var full = await GetAsync(path);
        var date = DateTimeOffset.UtcNow.AddDays(1).ToString("R", CultureInfo.InvariantCulture);

        var since = await GetAsync(path, ("If-Modified-Since", date));
        var head = await SendAsync(_client, path, HttpMethod.Head, [("If-Modified-Since", date)]);
        var both = await GetAsync(path, ("If-Modified-Since", date), ("If-None-Match", full.ETag!));

        since.Status.ShouldBe(HttpStatusCode.OK);
        since.FromCache.ShouldBeTrue();
        since.Body.ShouldBe(full.Body);
        head.Status.ShouldBe(HttpStatusCode.OK);
        both.Status.ShouldBe(HttpStatusCode.NotModified);
    }

    [Fact]
    public async Task PublishingOrUnpublishingEvictsTheEntryAndItsTypeAndTheNextReadShowsTheChange()
    {
        var path = $"note/{_first.Id}";
        await GetAsync(path);
        await GetAsync("note");
        (await GetAsync(path)).FromCache.ShouldBeTrue();
        (await GetAsync("note")).FromCache.ShouldBeTrue();

        var notes = _editor.Content<Note>();
        await notes.PatchAsync(_first.Id, new ContentDocument { ["title"] = "First, again" }, _first.Version, Token);
        await notes.PublishAsync(_first.Id, cancellationToken: Token);
        await InvalidatedAsync();

        var changed = await GetAsync(path);
        changed.FromCache.ShouldBeFalse();
        changed.Json["title"]!.GetValue<string>().ShouldBe("First, again");
        Titles(await GetAsync("note")).ShouldBe(["First, again"]);

        // Every read of the type carries its tag, so another entry's publish evicts this one's copy too.
        await PublishedAsync(new Note { Title = "Second", Slug = "second" });
        Titles(await GetAsync("note")).ShouldBe(["First, again", "Second"], ignoreOrder: true);
        (await GetAsync(path)).FromCache.ShouldBeFalse();

        await GetAsync("note/by/slug/first");
        (await GetAsync(path)).FromCache.ShouldBeTrue();
        await notes.UnpublishAsync(_first.Id, cancellationToken: Token);
        await InvalidatedAsync();

        (await GetAsync(path)).Status.ShouldBe(HttpStatusCode.NotFound);
        (await GetAsync("note/by/slug/first")).Status.ShouldBe(HttpStatusCode.NotFound);
        Titles(await GetAsync("note")).ShouldBe(["Second"]);
    }

    [Fact]
    public async Task APopulatedEntryEvictsTheReadsThatShowIt()
    {
        var topics = _editor.Content<Topic>();
        var topic = await topics.CreateAsync(new Topic { Name = "Cooking" }, Token);
        await topics.PublishAsync(topic.Id, cancellationToken: Token);
        await InvalidatedAsync();
        var note = await PublishedAsync(new Note { Title = "Soup", Slug = "soup", Topic = new Ref<Topic>(topic.Id) });

        var path = $"note/{note.Id}?populate=topic";
        await GetAsync(path);
        (await GetAsync(path)).FromCache.ShouldBeTrue();
        await topics.PatchAsync(topic.Id, new ContentDocument { ["name"] = "Baking" }, topic.Version, Token);
        await topics.PublishAsync(topic.Id, cancellationToken: Token);
        await InvalidatedAsync();

        var changed = await GetAsync(path);
        changed.FromCache.ShouldBeFalse();
        changed.Json["topic"]!["name"]!.GetValue<string>().ShouldBe("Baking");
    }

    [Fact]
    public async Task EachEvictionRaisesCacheInvalidatedEventWithTheTags()
    {
        var notes = _editor.Content<Note>();
        string[] tags = [$"entry:{_first.Id:D}", $"type:{_site:D}:note"];

        await notes.UnpublishAsync(_first.Id, cancellationToken: Token);
        (await InvalidatedAsync()).ShouldBe(tags);
        await notes.PublishAsync(_first.Id, cancellationToken: Token);
        (await InvalidatedAsync()).ShouldBe(tags);
        await notes.DeleteAsync(_first.Id, Token);
        (await InvalidatedAsync()).ShouldBe(tags);
        await notes.RestoreAsync(_first.Id, Token);
        (await InvalidatedAsync()).ShouldBe(tags);
    }

    // The Public role decides what the shared copy holds, so a change to its grants evicts the site.
    [Fact]
    public async Task AChangeToARoleOfTheSiteEvictsTheSiteAndTheNextReadFollowsTheNewGrants()
    {
        var path = $"note/{_first.Id}";
        await GetAsync(path);
        (await GetAsync(path)).FromCache.ShouldBeTrue();

        await using (var scope = _host.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
            context.Remove(await context.Set<RoleGrant>().SingleAsync(grant => grant.RoleId == _publicRole && grant.Permission == "content.note.read", Token));
            await context.SaveChangesAsync(Token);
        }

        (await InvalidatedAsync()).ShouldBe([SiteTag]);
        (await GetAsync(path)).Status.ShouldBe(HttpStatusCode.NotFound);
    }

    // A key's copy follows its role; a role of the instance evicts every site, since a key of any site may hold it.
    [Fact]
    public async Task AChangeToARoleOfTheInstanceEvictsEverySite()
    {
        var memos = _editor.Content<Memo>();
        await memos.PublishAsync((await memos.CreateAsync(new Memo { Title = "Memo" }, Token)).Id, cancellationToken: Token);
        await InvalidatedAsync();
        using var editor = OfTheSite(_host.AsKey(await _host.CreateApiKeyAsync(ApiKeyClass.Delivery, BuiltInRoles.EditorName, cancellationToken: Token)));
        await SendAsync(editor, "memo");
        (await SendAsync(editor, "memo")).FromCache.ShouldBeTrue();

        await using (var scope = _host.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
            context.Set<RoleGrant>().Add(new RoleGrant { Id = Guid.CreateVersion7(), RoleId = BuiltInRoles.Editor, Permission = "settings.branding.manage" });
            await context.SaveChangesAsync(Token);
        }

        (await InvalidatedAsync()).ShouldBe([.. Sites().Select(site => $"site:{site:D}")], ignoreOrder: true);
        (await SendAsync(editor, "memo")).FromCache.ShouldBeFalse();
    }

    // A store that outlives the process, or that instances on another schema share, would otherwise hand them this copy.
    [Fact]
    public async Task AStoreSharedWithAnotherSchemaServesItNoCopyOfThisOne()
    {
        await using var services = new ServiceCollection().AddOptions().AddOutputCache().BuildServiceProvider();
        var store = services.GetRequiredService<IOutputCacheStore>();
        await using var first = await SharingAsync(store, other: false, grant: true);
        await using var same = await SharingAsync(store, other: false, grant: false);
        await using var other = await SharingAsync(store, other: true, grant: false);
        using var firstClient = first.CreateClient();
        using var sameClient = same.CreateClient();
        using var otherClient = other.CreateClient();

        (await SendAsync(firstClient, "note")).Status.ShouldBe(HttpStatusCode.OK);
        var shared = await SendAsync(sameClient, "note");
        var refused = await SendAsync(otherClient, "note");

        shared.Status.ShouldBe(HttpStatusCode.OK);
        shared.FromCache.ShouldBeTrue();
        refused.Status.ShouldBe(HttpStatusCode.NotFound);
        refused.FromCache.ShouldBeFalse();
    }

    [Fact]
    public async Task AKeyReadsACopyOfItsOwnWhichItsCacheControlKeepsPrivate()
    {
        var memos = _editor.Content<Memo>();
        await memos.PublishAsync((await memos.CreateAsync(new Memo { Title = "Memo" }, Token)).Id, cancellationToken: Token);
        await InvalidatedAsync();
        using var editor = OfTheSite(_host.AsKey(await _host.CreateApiKeyAsync(ApiKeyClass.Delivery, BuiltInRoles.EditorName, cancellationToken: Token)));
        using var reader = OfTheSite(_host.AsKey(await _host.CreateApiKeyAsync(ApiKeyClass.Delivery, BuiltInRoles.PublicName, cancellationToken: Token)));

        var first = await SendAsync(editor, "memo");
        var again = await SendAsync(editor, "memo");
        var anonymous = await GetAsync("memo");
        var other = await SendAsync(reader, "memo");

        first.Status.ShouldBe(HttpStatusCode.OK);
        Titles(first).ShouldBe(["Memo"]);
        first.CacheControl.ShouldBe(PrivateCacheControl);
        again.FromCache.ShouldBeTrue();
        again.Body.ShouldBe(first.Body);
        anonymous.Status.ShouldBe(HttpStatusCode.NotFound);
        other.Status.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task BearerTokensDraftsAndRefusedCredentialsAreNeverServedFromTheCache()
    {
        var path = $"note/{_first.Id}";
        await GetAsync(path);
        (await GetAsync(path)).FromCache.ShouldBeTrue();

        var bearer = await GetAsync(path, Bearer(TokenScopes.Delivery));
        var bearerAgain = await GetAsync(path, Bearer(TokenScopes.Delivery));
        var draft = await GetAsync(path + "?status=draft", Bearer(TokenScopes.Preview));
        var draftAgain = await GetAsync(path + "?status=draft", Bearer(TokenScopes.Preview));
        var refused = await GetAsync(path, (TestScheme.FailHeader, "1"));
        var wrongKey = await GetAsync(path, ("X-Api-Key", "fcms_not_a_key"));

        bearer.Status.ShouldBe(HttpStatusCode.OK);
        bearer.CacheControl.ShouldBe(PrivateCacheControl);
        bearerAgain.FromCache.ShouldBeFalse();
        draft.Status.ShouldBe(HttpStatusCode.OK);
        draft.CacheControl.ShouldBe("no-store");
        draftAgain.FromCache.ShouldBeFalse();
        refused.Status.ShouldBe(HttpStatusCode.Unauthorized);
        wrongKey.Status.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AnErrorIsNotCachedAndSaysNoStore()
    {
        var path = $"note/{Guid.CreateVersion7()}";

        var missing = await GetAsync(path);
        var again = await GetAsync(path);

        missing.Status.ShouldBe(HttpStatusCode.NotFound);
        missing.ETag.ShouldBeNull();
        missing.CacheControl.ShouldNotBeNull().ShouldContain("no-store");
        again.FromCache.ShouldBeFalse();
    }

    private static async Task<FluentCmsTestHost> SharingAsync(IOutputCacheStore store, bool other, bool grant)
    {
        var host = new FluentCmsTestHost().WithServices(services => services.AddSingleton(store));
        host = other ? host.WithContentTypes<Note, Topic>() : host.WithContentTypes<Note, Memo, Topic>();
        if (grant)
        {
            await using var scope = host.Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
            context.Set<RoleGrant>().Add(new RoleGrant { Id = Guid.CreateVersion7(), RoleId = DefaultSite.PublicRole, Permission = "content.note.read" });
            await context.SaveChangesAsync(Token);
        }

        return host;
    }

    private async Task<Note> PublishedAsync(Note note)
    {
        var notes = _editor.Content<Note>();
        var created = await notes.CreateAsync(note, Token);
        await notes.PublishAsync(created.Id, cancellationToken: Token);
        await InvalidatedAsync();
        return created;
    }

    // The tags of the next eviction, once the outbox is delivered.
    private async Task<IReadOnlyList<string>> InvalidatedAsync()
    {
        await DrainAsync();
        return await _invalidations.NextAsync(Token);
    }

    private async Task DrainAsync()
    {
        while (await _host.Services.GetRequiredService<OutboxDrainer>().DrainAsync(Token) > 0)
        {
        }
    }

    private HttpClient OfTheSite(HttpClient client)
    {
        client.DefaultRequestHeaders.Add(RequestBridge.SiteHeader, _site.ToString("D"));
        return client;
    }

    private List<Guid> Sites()
    {
        using var scope = _host.Services.CreateScope();
        return [.. scope.ServiceProvider.GetRequiredService<CmsDbContext>().Set<Site>().Select(site => site.Id)];
    }

    private Task<Reply> GetAsync(string path, params (string Name, string Value)[] headers) => SendAsync(_client, path, headers);

    private Task<Reply> HeadAsync(string path) => SendAsync(_client, path, HttpMethod.Head, []);

    private static Task<Reply> SendAsync(HttpClient client, string path, params (string Name, string Value)[] headers) =>
        SendAsync(client, path, HttpMethod.Get, headers);

    private static async Task<Reply> SendAsync(HttpClient client, string path, HttpMethod method, (string Name, string Value)[] headers)
    {
        using var request = new HttpRequestMessage(method, new Uri("/api/delivery/v1/" + path, UriKind.Relative));
        foreach (var (name, value) in headers)
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }

        using var response = await client.SendAsync(request, Token);
        return new Reply(
            response.StatusCode,
            response.Headers.ETag?.ToString(),
            response.Content.Headers.LastModified,
            response.Headers.NonValidated.TryGetValues("Cache-Control", out var cacheControl) ? cacheControl.ToString() : null,
            response.Headers.NonValidated.TryGetValues("Vary", out var vary) ? vary.ToString() : null,
            response.Headers.Age is not null,
            response.Content.Headers.ContentLength,
            await response.Content.ReadAsStringAsync(Token));
    }

    private static (string, string)[] Bearer(string scope) =>
    [
        (TestScheme.KindHeader, nameof(PrincipalKind.ServiceAccount)),
        (TestScheme.RolesHeader, BuiltInRoles.Editor.ToString()),
        (TestScheme.ScopeHeader, scope),
    ];

    private static string[] Titles(Reply reply) => [.. reply.Json["items"]!.AsArray().Select(item => item!["title"]!.GetValue<string>())];

    private static MeterListener ListenToCacheLookups(Action<string?> lookedUp)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meter) =>
            {
                if (instrument.Meter.Name == FluentCmsTelemetry.Name && instrument.Name == FluentCmsTelemetry.Instruments.CacheRequests)
                {
                    meter.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            string? cache = null, result = null;
            foreach (var tag in tags)
            {
                cache = tag.Key == FluentCmsTelemetry.Tags.Cache ? tag.Value as string : cache;
                result = tag.Key == FluentCmsTelemetry.Tags.Result ? tag.Value as string : result;
            }

            if (cache == DeliveryOutputCachePolicy.CacheName)
            {
                lookedUp(result);
            }
        });
        listener.Start();
        return listener;
    }

    private sealed record Reply(HttpStatusCode Status, string? ETag, DateTimeOffset? LastModified, string? CacheControl, string? Vary, bool FromCache, long? ContentLength, string Body)
    {
        public JsonNode Json => JsonNode.Parse(Body)!;
    }
}
