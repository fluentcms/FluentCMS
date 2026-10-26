using System.Text.Json;
using System.Text.RegularExpressions;
using FluentCMS.Core;
using FluentCMS.Core.Jobs;
using FluentCMS.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Tests.Jobs;

// The contract every job handler meets, built in or not (EXT-N1, ADR-0010). Derive one class per handler: name its type, and override
// AddHandlers to register one that lives outside Core.
[ConformanceFor(typeof(IJobHandler))]
public abstract partial class JobHandlerConformanceTests : IDisposable
{
    // The length the jobs table keeps for a type.
    private const int MaxTypeLength = 128;

    private ServiceProvider? _provider;

    protected abstract string JobType { get; }

    // False for a handler that reads nothing from its payload, which no payload can then make it fail.
    protected virtual bool ReadsPayload => true;

    private ServiceProvider Provider => _provider ??= BuildProvider();

    private IJobHandler Handler => Provider.GetServices<IJobHandler>().Single(handler => handler.Type == JobType);

    protected virtual void AddHandlers(IServiceCollection services)
    {
    }

    // Every job stores its type, so it is a short lowercase name, the same on every read, and no other handler claims it.
    [Fact]
    public void Its_type_is_one_stable_name_no_other_handler_takes()
    {
        Assert.Single(Provider.GetServices<IJobHandler>(), handler => handler.Type == JobType);
        Assert.Matches(TypeName(), Handler.Type);
        Assert.InRange(Handler.Type.Length, 1, MaxTypeLength);
        Assert.Equal(Handler.Type, Handler.Type);
    }

    // A handler runs as whoever enqueued the job, or as one named engine whatever enqueued it (ADR-0012), the same on every read.
    [Fact]
    public void It_runs_as_the_enqueuer_or_as_one_named_engine()
    {
        var runAs = Handler.RunAs;

        Assert.Equal(runAs, Handler.RunAs);
        Assert.True(runAs is null || !string.IsNullOrWhiteSpace(runAs.Component));
    }

    // Another attempt cannot fix a payload the handler cannot read, so it is a failure with a code and a message, not an exception.
    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    [InlineData("null")]
    public async Task A_payload_it_cannot_read_is_a_failure_that_says_why(string payload)
    {
        if (!ReadsPayload)
        {
            return;
        }

        var job = new Job
        {
            Id = Guid.CreateVersion7(),
            Type = JobType,
            Payload = JsonDocument.Parse(payload).RootElement.Clone(),
            State = JobState.Running,
            Attempts = 1,
            MaxAttempts = NewJob.DefaultMaxAttempts,
            CreatedAt = DateTimeOffset.UtcNow,
            ScheduledAt = DateTimeOffset.UtcNow,
        };
        await using var scope = Provider.CreateAsyncScope();

        var result = await Handler.RunAsync(new JobContext(job, scope.ServiceProvider, (_, _) => Task.FromResult(true)), CancellationToken.None);

        var error = Assert.Single(result.Errors);
        Assert.False(string.IsNullOrWhiteSpace(error.Code));
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    public void Dispose()
    {
        _provider?.Dispose();
        GC.SuppressFinalize(this);
    }

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFluentCmsCore();
        AddHandlers(services);
        return services.BuildServiceProvider();
    }

    [GeneratedRegex(@"^[a-z][a-z0-9]*(?:[._-][a-z0-9]+)*\z")]
    private static partial Regex TypeName();
}
