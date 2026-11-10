using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Telemetry;
using FluentCMS.Plugins.Sample;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Mcp.Tests.McpTesting;

namespace FluentCMS.Plugins.Mcp.Tests;

// A tool call is a span of the FluentCMS activity source, a request of the mcp surface and a log line. The source and the
// meter are the process's, so the span test tells its own calls apart by a site of its own.
public sealed class McpTelemetryTests
{
    [Fact]
    public async Task AToolCallIsASpanOfTheFluentCmsSourceNamedForTheTool()
    {
        await using var host = Host();
        var site = await CreateSiteAsync(host);
        ConcurrentQueue<System.Diagnostics.Activity> spans = new();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == FluentCmsTelemetry.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = spans.Enqueue,
        };
        ActivitySource.AddActivityListener(listener);
        using var http = await KeyAsync(host);
        await using var client = await ConnectAsync(http, site, InitializeProtocol);

        await client.CallToolAsync(SampleTools.WhoAmI, cancellationToken: Token);
        await client.CallToolAsync(SampleTools.EntryCreate, new Dictionary<string, object?> { ["type"] = "article", ["title"] = new string('x', 40) }, cancellationToken: Token);
        await Should.ThrowAsync<ModelContextProtocol.McpProtocolException>(async () => await client.CallToolAsync("made_up_by_a_client", cancellationToken: Token));

        var mine = spans.Where(span => span.DisplayName.StartsWith("mcp ", StringComparison.Ordinal) && (string?)span.GetTagItem("fluentcms.site") == site.ToString("D")).ToList();
        var called = mine.Single(span => span.DisplayName == $"mcp {SampleTools.WhoAmI}");
        called.GetTagItem("fluentcms.operation").ShouldBe(SampleTools.WhoAmI);
        called.Status.ShouldBe(ActivityStatusCode.Unset);
        var refused = mine.Single(span => span.DisplayName == $"mcp {SampleTools.EntryCreate}");
        refused.Status.ShouldBe(ActivityStatusCode.Error);
        ((string?)refused.GetTagItem("fluentcms.code")).ShouldNotBeNull().ShouldStartWith("FCMS21");

        // A name a client made up never names a span.
        mine.Single(span => span.DisplayName == "mcp tool").GetTagItem("fluentcms.code").ShouldBe("FCMS1600");

        // The content operation the tool ran is a child of the tool's span.
        spans.ShouldContain(span => span.DisplayName == "content create" && span.ParentId == refused.Id);
    }

    [Fact]
    public async Task RequestsCountUnderTheMcpSurface()
    {
        await using var host = Host();
        using var http = await KeyAsync(host);
        using var anonymous = host.CreateClient();
        ConcurrentBag<string> classes = [];
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == FluentCmsTelemetry.Name && instrument.Name == FluentCmsTelemetry.Instruments.Requests)
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            string? surface = null, status = null;
            foreach (var tag in tags)
            {
                surface = tag.Key == FluentCmsTelemetry.Tags.Surface ? (string?)tag.Value : surface;
                status = tag.Key == FluentCmsTelemetry.Tags.StatusClass ? (string?)tag.Value : status;
            }

            if (surface == "mcp")
            {
                classes.Add(status!);
            }
        });
        listener.Start();

        using var served = await PostAsync(http, DefaultSite.Id);
        using var refused = await PostAsync(anonymous, DefaultSite.Id);

        served.IsSuccessStatusCode.ShouldBeTrue();
        refused.IsSuccessStatusCode.ShouldBeFalse();
        classes.ShouldContain("2xx");
        classes.ShouldContain("4xx");
    }

    [Fact]
    public async Task AToolCallIsLoggedWithItsEventIdAndWithoutItsArguments()
    {
        var logs = new LogCapture();
        await using var host = Host().WithServices(services => services.AddSingleton<ILoggerProvider>(logs));
        using var http = await KeyAsync(host);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);

        await client.CallToolAsync(SampleTools.EntryCreate, new Dictionary<string, object?> { ["type"] = "article", ["title"] = "A secret title" }, cancellationToken: Token);

        var line = logs.Lines.Single(entry => entry.Id == 9280);
        line.Level.ShouldBe(LogLevel.Information);
        line.Message.ShouldContain(SampleTools.EntryCreate);
        line.Message.ShouldContain("succeeded");
        line.Message.ShouldNotContain("A secret title");
    }
}
