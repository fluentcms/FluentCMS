using FluentCMS.Core.Events;
using FluentCMS.Core.Jobs;
using FluentCMS.Plugins.Sample;
using FluentCMS.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Plugins.Tests;

// The host the contribution tests share, for the tests that do not configure one of their own. A test runs the plugin's job
// and drains the outbox itself, so neither queue has workers of the host's own. A test tells its notes, rows and calls apart
// from the others' by their texts.
public sealed class PluginContributionFixture : IAsyncDisposable
{
    public PluginContributionFixture() => Host = new FluentCmsTestHost()
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
