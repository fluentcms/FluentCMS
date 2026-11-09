using FluentCMS.Core.Events;
using FluentCMS.Testing;

namespace FluentCMS.Core.IntegrationTests.Testing;

// A host with Note that the tests of a class share, for the tests that do not configure one of their own. No worker drains
// the outbox: a test does, so that the rows it reads are there when it reads them.
public sealed class NoteHostFixture : IAsyncDisposable
{
    public FluentCmsTestHost Host { get; } = new FluentCmsTestHost()
        .WithContentTypes<Note>()
        .Configure(cms => cms.Jobs.Options.Queues[OutboxDrainJob.Queue] = 0);

    public ValueTask DisposeAsync() => Host.DisposeAsync();
}
