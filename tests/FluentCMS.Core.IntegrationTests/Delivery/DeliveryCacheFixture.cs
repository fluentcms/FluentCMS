using System.Threading.Channels;
using FluentCMS.Core.Api.Delivery;
using FluentCMS.Core.Events;
using FluentCMS.Events;
using FluentCMS.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.IntegrationTests.Delivery;

// The host the output cache tests share. The server keeps a copy for ten minutes, so only an eviction explains a fresh read,
// and every eviction is recorded. No worker drains the outbox: a test does, so that the eviction it caused has happened when
// the drain returns.
public sealed class DeliveryCacheFixture : IAsyncDisposable
{
    public DeliveryCacheFixture()
    {
        Host = new FluentCmsTestHost()
            .WithContentTypes<Note, Memo, Topic>()
            .WithServices(services =>
            {
                TestScheme.Add(services);
                services.AddSingleton(Invalidations);
                services.PostConfigure<DeliveryCacheOptions>(cache => cache.OutputCacheDuration = TimeSpan.FromMinutes(10));
            })
            .Configure(cms =>
            {
                cms.Jobs.Options.Queues[OutboxDrainJob.Queue] = 0;
                cms.Events.AddHandler<CacheInvalidatedEvent, InvalidationRecorder>();
            });
    }

    public FluentCmsTestHost Host { get; }

    internal Evictions Invalidations { get; } = new();

    public ValueTask DisposeAsync() => Host.DisposeAsync();

    internal sealed class Evictions
    {
        private readonly Channel<IReadOnlyList<string>> _tags = Channel.CreateUnbounded<IReadOnlyList<string>>();

        public ValueTask AddAsync(IReadOnlyList<string> tags) => _tags.Writer.WriteAsync(tags);

        // What an earlier test evicted is not the next one's to wait for.
        public void Forget()
        {
            while (_tags.Reader.TryRead(out _))
            {
            }
        }

        public async Task<IReadOnlyList<string>> NextAsync(CancellationToken cancellationToken)
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(TimeSpan.FromSeconds(30));
            return await _tags.Reader.ReadAsync(limit.Token);
        }
    }

    internal sealed class InvalidationRecorder(Evictions invalidations) : IEventHandler<CacheInvalidatedEvent>
    {
        public ValueTask HandleAsync(CacheInvalidatedEvent message, CancellationToken cancellationToken) => invalidations.AddAsync(message.Tags);
    }
}
