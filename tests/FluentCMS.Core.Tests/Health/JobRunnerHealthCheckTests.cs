using System.Diagnostics.Metrics;
using FluentCMS.Core.Health;
using FluentCMS.Core.Telemetry;
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

    private async Task<HealthStatus> StatusAsync() => (await CheckAsync()).Status;

    private Task<HealthCheckResult> CheckAsync() =>
        new JobRunnerHealthCheck(_pulse, _options, _clock, NullLogger<JobRunnerHealthCheck>.Instance).CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

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

    [Fact]
    public async Task AQueueWhoseOldestPendingJobWaitedPastTheLimitIsDegradedAndEveryAgeIsInTheData()
    {
        _pulse.Beat(Start);
        _pulse.Measure(new Dictionary<string, TimeSpan> { ["default"] = TimeSpan.Zero, ["webhooks"] = TimeSpan.FromMinutes(15) });

        var within = await CheckAsync();
        _pulse.Measure(new Dictionary<string, TimeSpan> { ["default"] = TimeSpan.FromSeconds(3), ["webhooks"] = TimeSpan.FromMinutes(16) });
        var behind = await CheckAsync();

        within.Status.ShouldBe(HealthStatus.Healthy);
        within.Data["webhooks"].ShouldBe(900.0);
        behind.Status.ShouldBe(HealthStatus.Degraded);
        behind.Description.ShouldBe("The oldest pending job of the queue 'webhooks' has waited 960 seconds; the limit is 900.");
        behind.Data["default"].ShouldBe(3.0);
        behind.Data["webhooks"].ShouldBe(960.0);
    }

    [Fact]
    public async Task AnInfiniteAgeLimitReportsTheAgesWithoutDegrading()
    {
        _options.PendingJobAgeLimit = Timeout.InfiniteTimeSpan;
        _pulse.Beat(Start);
        _pulse.Measure(new Dictionary<string, TimeSpan> { ["default"] = TimeSpan.FromDays(3) });

        var result = await CheckAsync();

        result.Status.ShouldBe(HealthStatus.Healthy);
        result.Data["default"].ShouldBe(TimeSpan.FromDays(3).TotalSeconds);
    }

    [Fact]
    public void TheGaugeReportsTheAgesTheRunnerLastRecordedByQueue()
    {
        var reported = new Dictionary<string, double>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meters) =>
        {
            if (instrument.Meter.Name == FluentCmsTelemetry.Name && instrument.Name == FluentCmsTelemetry.Instruments.JobOldestPendingAge)
            {
                meters.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((_, value, tags, _) => reported[(string)tags[0].Value!] = value);
        listener.Start();

        FluentCmsTelemetry.RecordJobQueueAges(new Dictionary<string, TimeSpan> { ["outbox"] = TimeSpan.Zero, ["webhooks"] = TimeSpan.FromSeconds(42) });
        listener.RecordObservableInstruments();

        reported.ShouldBe(new Dictionary<string, double> { ["outbox"] = 0, ["webhooks"] = 42 });
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
