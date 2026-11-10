using System.Collections.Concurrent;
using System.Net;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Events;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Events;
using FluentCMS.Schema;
using FluentCMS.Sites;
using FluentCMS.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Delivery;

// FluentCms:Delivery:Cache:CacheTagHeader: the tags of a Delivery read, sent for a CDN to purge by the CacheInvalidatedEvent.
public sealed class CacheTagHeaderTests : IAsyncDisposable
{
    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;
    private static readonly string Site = $"site:{DefaultSite.Id:D}";
    private static readonly string NoteType = $"type:{DefaultSite.Id:D}:note";

    private readonly ConcurrentQueue<IReadOnlyList<string>> _invalidated = new();
    private FluentCmsTestHost? _host;

    public ValueTask DisposeAsync() => _host?.DisposeAsync() ?? ValueTask.CompletedTask;

    [Fact]
    public async Task WithoutTheOptionAReadCarriesNoTags()
    {
        var (note, _) = await StartAsync(header: null, outputCache: true);
        using var client = _host!.CreateClient();

        foreach (var path in new[] { $"note/{note:D}?populate=topic", "note", "note/by/slug/soup" })
        {
            using var response = await GetAsync(client, path);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            response.Headers.Concat(response.Content.Headers).SelectMany(header => header.Value).ShouldAllBe(value => !value.Contains("entry:"));
        }
    }

    [Theory]
    [InlineData("Cache-Tag", ',')]
    [InlineData("Surrogate-Key", ' ')]
    [InlineData("X-Cms-Tags", ',')]
    public async Task AReadCarriesItsSiteItsTypeAndEveryEntryInIt(string header, char separator)
    {
        var (note, topic) = await StartAsync(header, outputCache: true);
        using var client = _host!.CreateClient();
        string[] tags = [Site, NoteType, $"entry:{note:D}", $"entry:{topic:D}"];

        var read = Tags(await GetAsync(client, $"note/{note:D}?populate=topic"), header, separator);
        var cached = await GetAsync(client, $"note/{note:D}?populate=topic");
        var list = Tags(await GetAsync(client, "note?populate=topic"), header, separator);
        var byField = Tags(await GetAsync(client, "note/by/slug/soup"), header, separator);

        read.ShouldBe(tags, ignoreOrder: true);
        cached.Headers.Age.ShouldNotBeNull();
        Tags(cached, header, separator).ShouldBe(tags, ignoreOrder: true);
        list.ShouldBe(tags, ignoreOrder: true);
        byField.ShouldBe(tags, ignoreOrder: true);
    }

    // A draft is sent no-store, so no CDN keeps it to purge.
    [Fact]
    public async Task ADraftReadCarriesNoTags()
    {
        await StartAsync("Cache-Tag", outputCache: true);
        using var client = _host!.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/delivery/v1/note?status=draft", UriKind.Relative));
        request.Headers.TryAddWithoutValidation(TestScheme.KindHeader, nameof(PrincipalKind.ServiceAccount));
        request.Headers.TryAddWithoutValidation(TestScheme.RolesHeader, BuiltInRoles.Editor.ToString());
        request.Headers.TryAddWithoutValidation(TestScheme.ScopeHeader, "draft");

        using var response = await client.SendAsync(request, Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.Contains("Cache-Tag").ShouldBeFalse();
    }

    // With the output cache off nothing is stored, but a CDN still holds what the header tagged, so the event is raised. Each
    // event names only tags the reads were sent, and one of the populating note's, so a purge by them reaches it.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheInvalidationNamesTheTagsTheHeaderCarried(bool outputCache)
    {
        var (note, topic) = await StartAsync("Surrogate-Key", outputCache);
        using var client = _host!.CreateClient();
        var noteTags = Tags(await GetAsync(client, $"note/{note:D}?populate=topic"), "Surrogate-Key", ' ');
        var topicTags = Tags(await GetAsync(client, $"topic/{topic:D}"), "Surrogate-Key", ' ');

        var topics = _host.As(await _host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token)).Content<Topic>();
        var current = await topics.GetAsync(topic, cancellationToken: Token);
        await topics.PatchAsync(topic, new ContentDocument { ["name"] = "Baking" }, current!.Version, Token);
        await topics.PublishAsync(topic, cancellationToken: Token);
        await using (var scope = _host.Services.CreateAsyncScope())
        using (SystemOperation.Begin(scope.ServiceProvider.GetRequiredService<IOperationContextAccessor>(), Origin.Cli, new SiteId(DefaultSite.Id)))
        {
            await scope.ServiceProvider.GetRequiredService<SiteManager>().UpdateAsync(DefaultSite.Id, new SiteChange { Name = "Renamed" }, Token);
        }

        await DrainAsync();

        var invalidated = _invalidated.ToArray();
        invalidated.Length.ShouldBe(2);
        invalidated.ShouldContain(tags => tags.SequenceEqual(new[] { $"entry:{topic:D}", $"type:{DefaultSite.Id:D}:topic" }));
        invalidated.ShouldContain(tags => tags.SequenceEqual(new[] { Site }));
        invalidated.ShouldAllBe(tags => tags.All(tag => noteTags.Contains(tag) || topicTags.Contains(tag)) && tags.Any(noteTags.Contains));
    }

    private async Task<(Guid Note, Guid Topic)> StartAsync(string? header, bool outputCache)
    {
        _host = new FluentCmsTestHost()
            .WithContentTypes<Note, Topic>()
            .WithServices(services =>
            {
                TestScheme.Add(services);
                services.AddSingleton(_invalidated);
            })
            .Configure(cms =>
            {
                cms.DeliveryCache.CacheTagHeader = header;
                cms.DeliveryCache.OutputCache = outputCache;
                cms.Jobs.Options.Queues[OutboxDrainJob.Queue] = 0;
                cms.Events.AddHandler<CacheInvalidatedEvent, Recorder>();
            });

        Guid note = default, topic = default;
        _host.Seed(async services =>
        {
            var context = services.GetRequiredService<CmsDbContext>();
            context.Set<RoleGrant>().AddRange(
                new RoleGrant { Id = Guid.CreateVersion7(), RoleId = DefaultSite.PublicRole, Permission = "content.note.read" },
                new RoleGrant { Id = Guid.CreateVersion7(), RoleId = DefaultSite.PublicRole, Permission = "content.topic.read" });
            await context.SaveChangesAsync();

            var topics = services.GetRequiredService<IContentService<Topic>>();
            topic = (await topics.CreateAsync(new Topic { Name = "Cooking" })).Id;
            await topics.PublishAsync(topic);
            var notes = services.GetRequiredService<IContentService<Note>>();
            note = (await notes.CreateAsync(new Note { Title = "Soup", Slug = "soup", Topic = new Ref<Topic>(topic) })).Id;
            await notes.PublishAsync(note);
        });

        _host.CreateClient().Dispose();
        await DrainAsync();
        _invalidated.Clear();
        return (note, topic);
    }

    private async Task DrainAsync()
    {
        while (await _host!.Services.GetRequiredService<OutboxDrainer>().DrainAsync(Token) > 0)
        {
        }
    }

    private static Task<HttpResponseMessage> GetAsync(HttpClient client, string path) =>
        client.GetAsync(new Uri("/api/delivery/v1/" + path, UriKind.Relative), Token);

    private static string[] Tags(HttpResponseMessage response, string header, char separator)
    {
        using (response)
        {
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            return response.Headers.GetValues(header).ShouldHaveSingleItem().Split(separator);
        }
    }

    private sealed class Recorder(ConcurrentQueue<IReadOnlyList<string>> invalidated) : IEventHandler<CacheInvalidatedEvent>
    {
        public ValueTask HandleAsync(CacheInvalidatedEvent message, CancellationToken cancellationToken)
        {
            invalidated.Enqueue(message.Tags);
            return ValueTask.CompletedTask;
        }
    }
}
