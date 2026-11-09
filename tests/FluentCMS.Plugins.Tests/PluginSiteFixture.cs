using FluentCMS.Core.Events;
using FluentCMS.Core.Jobs;
using FluentCMS.Plugins.Sample;
using FluentCMS.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Plugins.Tests;

// The host the per-site tests share, for the tests that do not configure one of their own. Each test turns the plugin off in
// a site of its own, and runs the jobs and drains the outbox itself, so neither queue has workers of the host's own.
public sealed class PluginSiteFixture : IAsyncDisposable
{
    public PluginSiteFixture() => Host = new FluentCmsTestHost()
        .WithContentTypes<Note>()
        .WithServices(services => services.AddSingleton(Calls))
        .Configure(cms =>
        {
            cms.Jobs.Options.Queues[JobRunnerOptions.DefaultQueue] = 0;
            cms.Jobs.Options.Queues[OutboxDrainJob.Queue] = 0;
        });

    public PluginCalls Calls { get; } = new();

    public FluentCmsTestHost Host { get; }

    public ValueTask DisposeAsync() => Host.DisposeAsync();
}
