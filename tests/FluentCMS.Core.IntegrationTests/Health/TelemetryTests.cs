using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.IntegrationTests.Testing;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Telemetry;
using FluentCMS.Hooks;
using FluentCMS.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Health;

public sealed class TelemetryTests : IAsyncDisposable
{
    private const string Secret = "a title nobody may find in telemetry";

    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    private readonly FluentCmsTestHost _host = new FluentCmsTestHost()
        .WithContentTypes<Note>()
        .WithServices(services => services.AddScoped<IBeforeCreateHook<ContentDocument>, NoOpHook>());

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    [Fact]
    public async Task AContentOperationEmitsAFluentCmsSpanWithoutContentValues()
    {
        var spans = new ConcurrentBag<System.Diagnostics.Activity>();
        using var listener = Listen(spans);
        var editor = await _host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        var notes = _host.As(editor).Content<Note>();

        var created = await notes.CreateAsync(new Note { Title = Secret }, Token);
        await notes.UpdateAsync(created.Id, new Note { Title = Secret + "!" }, created.Version, Token);
        await notes.PublishAsync(created.Id, cancellationToken: Token);
        await notes.DeleteAsync(created.Id, Token);

        var site = DefaultSite.Id.ToString("D");
        foreach (var operation in new[] { "create", "update", "publish", "delete" })
        {
            var span = spans.FirstOrDefault(span => Tag(span, FluentCmsTelemetry.Tags.Operation) == operation && Tag(span, FluentCmsTelemetry.Tags.Type) == "note");
            span.ShouldNotBeNull(operation);
            span.Source.Name.ShouldBe("FluentCMS");
            span.DisplayName.ShouldBe($"content {operation}");
            Tag(span, FluentCmsTelemetry.Tags.Site).ShouldBe(site);
        }

        spans.SelectMany(span => span.TagObjects).Select(tag => tag.Value?.ToString()).ShouldNotContain(value => value != null && value.Contains("nobody", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ARunHookEmitsAHookSpan()
    {
        var spans = new ConcurrentBag<System.Diagnostics.Activity>();
        using var listener = Listen(spans);
        var editor = await _host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);

        await _host.As(editor).Content<Note>().CreateAsync(new Note { Title = "hooked" }, Token);

        var span = spans.FirstOrDefault(span => Tag(span, FluentCmsTelemetry.Tags.Operation) == "BeforeCreate");
        span.ShouldNotBeNull();
        span.DisplayName.ShouldBe("hook BeforeCreate");
        Tag(span, FluentCmsTelemetry.Tags.Type).ShouldBe("note");
    }

    [Fact]
    public async Task ARequestIncrementsTheRequestCounterBySurfaceAndAQueryRecordsItsDuration()
    {
        var counts = new ConcurrentBag<(long Value, string? Surface)>();
        var durations = new ConcurrentBag<(double Value, string? Type)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meter) =>
            {
                if (instrument.Meter.Name == FluentCmsTelemetry.Name)
                {
                    meter.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            if (instrument.Name == FluentCmsTelemetry.Instruments.Requests)
            {
                counts.Add((value, Tag(tags, FluentCmsTelemetry.Tags.Surface)));
            }
        });
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
        {
            if (instrument.Name == FluentCmsTelemetry.Instruments.QueryDuration)
            {
                durations.Add((value, Tag(tags, FluentCmsTelemetry.Tags.Type)));
            }
        });
        listener.Start();
        using var client = _host.CreateClient();

        using var live = await client.GetAsync(new Uri("/health/live", UriKind.Relative), Token);
        using var delivery = await client.GetAsync(new Uri("/api/delivery/v1/note", UriKind.Relative), Token);

        counts.ShouldContain(count => count.Surface == "health" && count.Value == 1);
        counts.ShouldContain(count => count.Surface == "delivery" && count.Value == 1);

        var editor = await _host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        await _host.As(editor).Content<Note>().QueryAsync(new ContentQuery { Status = QueryStatus.Draft }, Token);
        durations.ShouldContain(duration => duration.Type == "note" && duration.Value >= 0);
    }

    [Fact]
    public void TheCacheHitRatioIsInstrumentedAlthoughNothingReportsHitsYet()
    {
        const string Cache = "telemetry-test";
        var ratios = new ConcurrentDictionary<string, double>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meter) =>
            {
                if (instrument.Meter.Name == FluentCmsTelemetry.Name && instrument.Name == FluentCmsTelemetry.Instruments.CacheHitRatio)
                {
                    meter.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<double>((_, value, tags, _) => ratios[Tag(tags, "cache") ?? ""] = value);
        listener.Start();

        FluentCmsTelemetry.RecordCache(Cache, hit: true);
        FluentCmsTelemetry.RecordCache(Cache, hit: true);
        FluentCmsTelemetry.RecordCache(Cache, hit: true);
        FluentCmsTelemetry.RecordCache(Cache, hit: false);
        listener.RecordObservableInstruments();

        ratios[Cache].ShouldBe(0.75);
    }

    private static ActivityListener Listen(ConcurrentBag<System.Diagnostics.Activity> spans)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == FluentCmsTelemetry.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = spans.Add,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private static string? Tag(System.Diagnostics.Activity span, string key) => span.GetTagItem(key)?.ToString();

    private static string? Tag(ReadOnlySpan<KeyValuePair<string, object?>> tags, string key)
    {
        foreach (var tag in tags)
        {
            if (tag.Key == key)
            {
                return tag.Value?.ToString();
            }
        }

        return null;
    }

    private sealed class NoOpHook : IBeforeCreateHook<ContentDocument>
    {
        public ValueTask BeforeCreateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
