using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using FluentCMS.Content;
using FluentCMS.Core.Api.Delivery;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Events;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Core.Telemetry;
using FluentCMS.Events;
using FluentCMS.Schema;
using FluentCMS.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Delivery;

// The cache headers and the output cache of the Delivery API, on a host per test so that what one test caches cannot
// answer another. The server keeps a copy for ten minutes here, so only an eviction explains a fresh read.
public sealed class DeliveryCacheTests : IAsyncLifetime
{
    private const string PrivateCacheControl = "max-age=60, private, stale-while-revalidate=300";

    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    private readonly Invalidations _invalidations = new();
    private readonly FluentCmsTestHost _host;
    private HttpClient _client = null!;
    private TestActor _editor = null!;
    private Note _first = null!;

    public DeliveryCacheTests()
    {
        _host = new FluentCmsTestHost()
            .WithContentTypes<Note, Memo, Topic>()
            .WithServices(services =>
            {
                TestScheme.Add(services);
                services.AddSingleton(_invalidations);
                services.PostConfigure<DeliveryCacheOptions>(cache => cache.OutputCacheDuration = TimeSpan.FromMinutes(10));
            })
            .Configure(cms => cms.Events.AddHandler<CacheInvalidatedEvent, InvalidationRecorder>());
    }

    public async ValueTask InitializeAsync()
    {
        await using (var scope = _host.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
            context.Set<RoleGrant>().AddRange(
                new RoleGrant { Id = Guid.CreateVersion7(), RoleId = DefaultSite.PublicRole, Permission = "content.note.read" },
                new RoleGrant { Id = Guid.CreateVersion7(), RoleId = DefaultSite.PublicRole, Permission = "content.topic.read" });
            await context.SaveChangesAsync(Token);
        }

        _editor = _host.As(await _host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token));
        _first = await PublishedAsync(new Note { Title = "First", Slug = "first" });
        _client = _host.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task AReadCarriesAnETagLastModifiedAndTheConfiguredCacheControl()
    {
        foreach (var path in new[] { $"note/{_first.Id}", "note/by/slug/first", "note" })
        {
            var reply = await GetAsync(path);

            reply.Status.ShouldBe(HttpStatusCode.OK);
            reply.ETag.ShouldNotBeNull().ShouldStartWith("\"");
            reply.CacheControl.ShouldBe(DeliveryCacheOptions.DefaultCacheControl);
            reply.Vary.ShouldBe(DeliveryResponse.Vary);
            reply.LastModified.ShouldBe(Seconds(PublishedAt(reply.Json["items"]?[0] ?? reply.Json)));
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
        using var listener = ListenToCacheLookups(lookups);

        var first = await GetAsync($"note/{_first.Id}");
        var second = await GetAsync($"note/{_first.Id}");

        first.FromCache.ShouldBeFalse();
        second.FromCache.ShouldBeTrue();
        second.Body.ShouldBe(first.Body);
        second.ETag.ShouldBe(first.ETag);
        second.CacheControl.ShouldBe(first.CacheControl);

        // A lookup is counted when its response completes, which can be after the client has read it.
        SpinWait.SpinUntil(() => lookups.Contains("miss") && lookups.Contains("hit"), TimeSpan.FromSeconds(10)).ShouldBeTrue();
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

    [Fact]
    public async Task IfModifiedSinceAloneIsAnsweredInFull()
    {
        var path = $"note/{_first.Id}";
        var full = await GetAsync(path);
        await GetAsync(path);

        var since = await GetAsync(path, ("If-Modified-Since", full.LastModified!.Value.ToString("R", CultureInfo.InvariantCulture)));

        since.Status.ShouldBe(HttpStatusCode.OK);
        since.Body.ShouldBe(full.Body);
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
        string[] tags = [$"entry:{_first.Id:D}", $"type:{DefaultSite.Id:D}:note"];

        await notes.UnpublishAsync(_first.Id, cancellationToken: Token);
        (await InvalidatedAsync()).ShouldBe(tags);
        await notes.PublishAsync(_first.Id, cancellationToken: Token);
        (await InvalidatedAsync()).ShouldBe(tags);
        await notes.DeleteAsync(_first.Id, Token);
        (await InvalidatedAsync()).ShouldBe(tags);
        await notes.RestoreAsync(_first.Id, Token);
        (await InvalidatedAsync()).ShouldBe(tags);
    }

    [Fact]
    public async Task AKeyReadsACopyOfItsOwnWhichItsCacheControlKeepsPrivate()
    {
        var memos = _editor.Content<Memo>();
        await memos.PublishAsync((await memos.CreateAsync(new Memo { Title = "Memo" }, Token)).Id, cancellationToken: Token);
        await InvalidatedAsync();
        using var editor = _host.AsKey(await _host.CreateApiKeyAsync(ApiKeyClass.Delivery, BuiltInRoles.EditorName, cancellationToken: Token));
        using var reader = _host.AsKey(await _host.CreateApiKeyAsync(ApiKeyClass.Delivery, BuiltInRoles.PublicName, cancellationToken: Token));

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

    private async Task<Note> PublishedAsync(Note note)
    {
        var notes = _editor.Content<Note>();
        var created = await notes.CreateAsync(note, Token);
        await notes.PublishAsync(created.Id, cancellationToken: Token);
        await InvalidatedAsync();
        return created;
    }

    // The tags of the next eviction, after asking for the outbox to be delivered now rather than when the runner gets to it.
    private async Task<IReadOnlyList<string>> InvalidatedAsync()
    {
        await _host.Services.GetRequiredService<OutboxDrainer>().DrainAsync(Token);
        return await _invalidations.NextAsync(Token);
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

    private static DateTimeOffset PublishedAt(JsonNode entry) => DateTimeOffset.Parse(entry["publishedAt"]!.GetValue<string>(), CultureInfo.InvariantCulture);

    private static DateTimeOffset Seconds(DateTimeOffset moment) => new(moment.Ticks - (moment.Ticks % TimeSpan.TicksPerSecond), moment.Offset);

    private static MeterListener ListenToCacheLookups(ConcurrentBag<string?> lookups)
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
                lookups.Add(result);
            }
        });
        listener.Start();
        return listener;
    }

    private sealed record Reply(HttpStatusCode Status, string? ETag, DateTimeOffset? LastModified, string? CacheControl, string? Vary, bool FromCache, long? ContentLength, string Body)
    {
        public JsonNode Json => JsonNode.Parse(Body)!;
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
