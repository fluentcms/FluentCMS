using FluentCMS.Core.Caching;
using FluentCMS.Core.Schema;
using FluentCMS.Events;
using FluentCMS.Schema;
using Microsoft.AspNetCore.OutputCaching;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Caching;

public sealed class OutputCacheEvictionTests
{
    private static readonly Guid Site = Guid.CreateVersion7();
    private static readonly Guid Entry = Guid.CreateVersion7();
    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    private readonly Store _store = new();
    private readonly Bus _bus = new();
    private readonly OutputCacheEviction _eviction;

    public OutputCacheEvictionTests()
    {
        var schema = new SchemaBuilder()
            .AddCollection("article", type => type.Field("title", "text"))
            .AddCollection("banner", type => type.Scope(ContentScope.Global).Field("title", "text"))
            .Build();
        _eviction = new OutputCacheEviction(_store, _bus, schema);
    }

    [Fact]
    public async Task EachChangeOfPublishedStateEvictsTheEntryAndItsTypeAndRaisesTheTags()
    {
        string[] tags = [$"entry:{Entry:D}", $"type:{Site:D}:article"];
        var delivery = new EventDelivery(Guid.CreateVersion7(), 1);

        await _eviction.HandleAsync(Source(new EntryPublishedEvent("article", Entry, "en", 2)), delivery, Token);
        await _eviction.HandleAsync(Source(new EntryUnpublishedEvent("article", Entry, "en", 2)), delivery, Token);
        await _eviction.HandleAsync(Source(new EntryTrashedEvent("article", Entry, "en", 2)), delivery, Token);
        await _eviction.HandleAsync(Source(new EntryRestoredEvent("article", Entry, "en", 2)), delivery, Token);

        _store.Evicted.ShouldBe([.. tags, .. tags, .. tags, .. tags]);
        _bus.Published.Count.ShouldBe(4);
        _bus.Published.ShouldAllBe(message => message.Tags.SequenceEqual(tags));
    }

    [Fact]
    public async Task AGlobalTypeIsEvictedForEverySite()
    {
        await _eviction.HandleAsync(Source(new EntryPublishedEvent("banner", Entry, "en", 1)), new EventDelivery(Guid.CreateVersion7(), 1), Token);

        _store.Evicted.ShouldBe([$"entry:{Entry:D}", "type:global:banner"]);
    }

    [Fact]
    public async Task ATypeTheSchemaNoLongerHasIsStillEvicted()
    {
        await _eviction.HandleAsync(Source(new EntryUnpublishedEvent("gone", Entry, "en", 1)), new EventDelivery(Guid.CreateVersion7(), 1), Token);

        _store.Evicted.ShouldBe([$"entry:{Entry:D}", $"type:{Site:D}:gone"]);
    }

    private static T Source<T>(T message)
        where T : CmsEvent => message with { Source = EventSource.FromSystem(DateTimeOffset.UtcNow) with { SiteId = Site } };

    private sealed class Store : IOutputCacheStore
    {
        public List<string> Evicted { get; } = [];

        public ValueTask EvictByTagAsync(string tag, CancellationToken cancellationToken)
        {
            Evicted.Add(tag);
            return ValueTask.CompletedTask;
        }

        public ValueTask<byte[]?> GetAsync(string key, CancellationToken cancellationToken) => ValueTask.FromResult<byte[]?>(null);

        public ValueTask SetAsync(string key, byte[] value, string[]? tags, TimeSpan validFor, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class Bus : IEventBus
    {
        public List<CacheInvalidatedEvent> Published { get; } = [];

        public ValueTask PublishAsync<TEvent>(TEvent message, CancellationToken cancellationToken = default)
            where TEvent : CmsEvent
        {
            Published.Add(message.ShouldBeOfType<CacheInvalidatedEvent>());
            return ValueTask.CompletedTask;
        }
    }
}
