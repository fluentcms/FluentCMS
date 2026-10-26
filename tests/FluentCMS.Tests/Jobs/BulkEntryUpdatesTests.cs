using System.Text.Json;
using FluentCMS.Core.Builtin.Jobs;
using FluentCMS.Core.Jobs;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Tests.Jobs;

public sealed class BulkEntryUpdatesTests
{
    // Another attempt reads the same payload, so the job fails at once instead of throwing to be retried.
    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"changeSetId\": \"not-an-id\", \"items\": []}")]
    [InlineData("{\"changeSetId\": \"0192f3a1-7c2e-7a4b-9d3e-1f2a3b4c5d6e\"}")]
    public async Task A_payload_it_cannot_read_is_a_failure(string payload)
    {
        var job = new Job
        {
            Id = Guid.CreateVersion7(),
            Type = BulkEntryUpdates.Name,
            Payload = JsonDocument.Parse(payload).RootElement.Clone(),
            State = JobState.Running,
            Attempts = 1,
            MaxAttempts = NewJob.DefaultMaxAttempts,
            CreatedAt = DateTimeOffset.UtcNow,
            ScheduledAt = DateTimeOffset.UtcNow,
        };
        await using var services = new ServiceCollection().BuildServiceProvider();

        var result = await new BulkEntryUpdates().RunAsync(new JobContext(job, services, (_, _) => Task.FromResult(true)), CancellationToken.None);

        Assert.Equal("payload_invalid", Assert.Single(result.Errors).Code);
    }
}
