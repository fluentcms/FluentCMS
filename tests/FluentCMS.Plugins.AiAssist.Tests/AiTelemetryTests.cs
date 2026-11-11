using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using FluentCMS.Core.Telemetry;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.AiAssist.Tests.AiTesting;

namespace FluentCMS.Plugins.AiAssist.Tests;

// A call is a span of the FluentCMS activity source and a count of its meter. The source and the meter are the
// process's, so a test tells its own calls apart by a site and a model of its own.
public sealed class AiTelemetryTests : IClassFixture<AiHost>
{
    private const string Marker = "ships-that-must-not-be-traced";

    private readonly AiHost _ai;

    public AiTelemetryTests(AiHost ai)
    {
        _ai = ai;
        _ai.Chat.Reset();
    }

    [Fact]
    public async Task ACallIsASpanOfTheFluentCmsSourceThatHoldsNothingOfTheText()
    {
        var site = await _ai.SiteAsync();
        using var admin = await _ai.AdminAsync(site);
        ConcurrentQueue<System.Diagnostics.Activity> spans = new();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == FluentCmsTelemetry.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = spans.Enqueue,
        };
        ActivitySource.AddActivityListener(listener);
        _ai.Chat.Answer = $"Ships are large vessels, {Marker}.";

        await SuggestAsync(admin, "rewrite", new { type = "article", field = "summary", text = $"Ships are big boats, {Marker}." });
        _ai.Chat.Failure = new InvalidOperationException($"The provider could not read: {Marker}.");
        (await ActAsync(admin, "shorten", new { type = "article", field = "summary", text = $"Ships are big boats, {Marker}." })).Dispose();

        var mine = spans.Where(span => span.DisplayName.StartsWith("ai ", StringComparison.Ordinal) && (string?)span.GetTagItem("fluentcms.site") == site.ToString("D")).ToList();
        var succeeded = mine.Single(span => span.DisplayName == "ai rewrite");
        succeeded.GetTagItem("fluentcms.operation").ShouldBe("rewrite");
        succeeded.GetTagItem("fluentcms.type").ShouldBe("article");
        succeeded.GetTagItem("fluentcms.provider").ShouldBe(ScriptedFactory.Provider);
        succeeded.GetTagItem("fluentcms.outcome").ShouldBe("succeeded");
        succeeded.Status.ShouldBe(ActivityStatusCode.Unset);
        var failed = mine.Single(span => span.DisplayName == "ai shorten");
        failed.Status.ShouldBe(ActivityStatusCode.Error);
        failed.GetTagItem("fluentcms.outcome").ShouldBe("failed");
        failed.GetTagItem("fluentcms.code").ShouldBe("FCMS1689");

        foreach (var span in spans)
        {
            span.DisplayName.ShouldNotContain(Marker);
            span.TagObjects.ShouldNotContain(tag => (tag.Value == null ? "" : tag.Value.ToString()!).Contains(Marker, StringComparison.Ordinal));
            span.Events.ShouldNotContain(spanEvent => spanEvent.Name.Contains(Marker, StringComparison.Ordinal) || spanEvent.Tags.Any(tag => (tag.Value == null ? "" : tag.Value.ToString()!).Contains(Marker, StringComparison.Ordinal)));
        }
    }

    [Fact]
    public async Task CallsCountByActionProviderAndOutcome()
    {
        // The count has no site, so the test tells its calls apart by a provider that only a host of its own names.
        await using var ai = new AiHost();
        var provider = ai.Chat.Provider = $"scripted-{Guid.NewGuid():N}";
        var site = await ai.SiteAsync(settings => settings.RequestTimeout = 1);
        using var admin = await ai.AdminAsync(site);
        ConcurrentBag<(string Action, string Outcome)> counted = [];
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == FluentCmsTelemetry.Name && instrument.Name == "fluentcms.ai.calls")
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((_, measurement, tags, _) =>
        {
            string? action = null, named = null, outcome = null;
            foreach (var tag in tags)
            {
                action = tag.Key == "fluentcms.operation" ? (string?)tag.Value : action;
                named = tag.Key == "fluentcms.provider" ? (string?)tag.Value : named;
                outcome = tag.Key == "fluentcms.outcome" ? (string?)tag.Value : outcome;
            }

            if (named == provider && measurement == 1)
            {
                counted.Add((action!, outcome!));
            }
        });
        listener.Start();
        var body = new { type = "article", field = "summary", text = "Ships are big boats." };

        // A success, a failure, a timeout, and a refusal, which is no call.
        await SuggestAsync(admin, "rewrite", body);
        ai.Chat.Failure = new InvalidOperationException("The provider broke.");
        (await ActAsync(admin, "title", body)).Dispose();
        ai.Chat.Failure = null;
        ai.Chat.Hang = true;
        (await ActAsync(admin, "summary", body)).Dispose();
        (await ActAsync(admin, "paraphrase", body)).Dispose();

        counted.OrderBy(count => count.Action, StringComparer.Ordinal).ShouldBe([("rewrite", "succeeded"), ("summary", "timeout"), ("title", "failed")]);
    }
}
