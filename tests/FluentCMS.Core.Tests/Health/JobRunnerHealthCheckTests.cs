using FluentCMS.Core.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Health;

public class JobRunnerHealthCheckTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    private readonly JobRunnerPulse _pulse = new();
    private readonly Clock _clock = new(Start);
    private readonly HealthOptions _options = new() { JobRunnerHeartbeatTimeout = TimeSpan.FromMinutes(2) };

    private async Task<HealthStatus> StatusAsync() =>
        (await new JobRunnerHealthCheck(_pulse, _options, _clock, NullLogger<JobRunnerHealthCheck>.Instance).CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken)).Status;

    [Fact]
    public async Task ARunnerThatHasNotStartedIsUnhealthy() =>
        (await StatusAsync()).ShouldBe(HealthStatus.Unhealthy);

    [Fact]
    public async Task ARunnerThatBeatsWithinTheLimitIsHealthyAndOneThatWentSilentIsNot()
    {
        _pulse.Beat(Start);
        (await StatusAsync()).ShouldBe(HealthStatus.Healthy);

        _clock.Now = Start + TimeSpan.FromMinutes(2);
        (await StatusAsync()).ShouldBe(HealthStatus.Healthy);

        _clock.Now = Start + TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(1);
        (await StatusAsync()).ShouldBe(HealthStatus.Unhealthy);

        _pulse.Beat(_clock.Now);
        (await StatusAsync()).ShouldBe(HealthStatus.Healthy);
    }

    [Fact]
    public async Task AnInfiniteLimitLeavesTheRunnerOutEvenWhenItNeverStarted()
    {
        _options.JobRunnerHeartbeatTimeout = Timeout.InfiniteTimeSpan;

        (await StatusAsync()).ShouldBe(HealthStatus.Healthy);
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
