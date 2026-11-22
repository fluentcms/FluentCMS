using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using FluentCMS.Core.Telemetry;

namespace FluentCMS.Core.IntegrationTests.Api;

// The meter is the process's, so a refusal of a test running beside this one lands here too; a test asks which policies
// counted, never how many.
internal sealed class RefusalCounter : IDisposable
{
    private readonly MeterListener _listener = new();

    public RefusalCounter()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == FluentCmsTelemetry.Name && instrument.Name == FluentCmsTelemetry.Instruments.RateLimitRefused)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            foreach (var tag in tags)
            {
                if (tag.Key == FluentCmsTelemetry.Tags.Policy && tag.Value is string policy)
                {
                    Policies.Add(policy);
                }
            }
        });
        _listener.Start();
    }

    public ConcurrentBag<string> Policies { get; } = [];

    public void Dispose() => _listener.Dispose();
}
