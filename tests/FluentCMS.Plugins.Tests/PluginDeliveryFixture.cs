using FluentCMS.Core.Events;
using FluentCMS.Testing;

namespace FluentCMS.Plugins.Tests;

// The host the Delivery group tests share, for the tests that do not configure one of their own. No worker drains the
// outbox: a test does, so that the copies its events evict are gone when it asks again.
public sealed class PluginDeliveryFixture : IAsyncDisposable
{
    public FluentCmsTestHost Host { get; } = new FluentCmsTestHost()
        .Configure(cms => cms.Jobs.Options.Queues[OutboxDrainJob.Queue] = 0);

    public ValueTask DisposeAsync() => Host.DisposeAsync();
}
