using FluentCMS.Core.Events;
using FluentCMS.Core.Jobs;
using FluentCMS.Schema;
using FluentCMS.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Plugins.Mcp.Tests;

// A type with a long text, for the lists that are shortened and cut.
[Collection("essay")]
public sealed class Essay : ContentItem
{
    [Text]
    public string? Title { get; set; }

    [LongText]
    public string? Body { get; set; }
}

internal static class McpSafetyTesting
{
    // A host of its own, with the limits a test sets: the limits are counted per instance and the sizes are the host's.
    public static FluentCmsTestHost Host(Action<McpOptions> configure) =>
        new FluentCmsTestHost()
            .WithContentTypes(typeof(Essay), typeof(Post), typeof(Writer))
            .WithServices(services => services.Configure(configure))
            .Configure(cms =>
            {
                cms.Jobs.Options.Queues[JobRunnerOptions.DefaultQueue] = 0;
                cms.Jobs.Options.Queues[OutboxDrainJob.Queue] = 0;
            });
}
