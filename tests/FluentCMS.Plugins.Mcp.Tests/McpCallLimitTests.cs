using System.Text.Json;
using FluentCMS.Agents;
using FluentCMS.Plugins.Sample;
using FluentCMS.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Mcp.Tests.McpTesting;
using static FluentCMS.Plugins.Mcp.Tests.McpToolsTesting;

namespace FluentCMS.Plugins.Mcp.Tests;

// The tool calls of a client are counted per client, on the clock of the host, which a test moves.
public sealed class McpCallLimitTests
{
    [Fact]
    public async Task AClientOverItsCallsPerMinuteGetsAToolErrorWithTheTimeToRetryWhileAnotherClientIsServed()
    {
        await using var host = McpSafetyTesting.Host(options => options.Limits = new McpCallLimitOptions { CallsPerMinute = 5, WritesPerMinute = 5 });
        using var http = await KeyAsync(host);
        using var otherHttp = await KeyAsync(host);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);
        await using var other = await ConnectAsync(otherHttp, protocolVersion: InitializeProtocol);

        for (var call = 0; call < 5; call++)
        {
            await OkAsync(client, SampleTools.WhoAmI);
        }

        var refused = await CallAsync(client, SampleTools.WhoAmI);
        var served = await CallAsync(other, SampleTools.WhoAmI);
        await client.ListToolsAsync(cancellationToken: Token);

        // A bucket of five fills by one call each twelve seconds.
        host.Time.Advance(TimeSpan.FromSeconds(11));
        var tooSoon = await CallAsync(client, SampleTools.WhoAmI);
        host.Time.Advance(TimeSpan.FromSeconds(1));
        var again = await CallAsync(client, SampleTools.WhoAmI);
        var once = await CallAsync(client, SampleTools.WhoAmI);

        refused.IsError.ShouldBe(true);
        Code(refused).ShouldBe("FCMS1634");
        Text(refused).ShouldContain("Retry in 12 seconds");
        refused.Meta![McpRequests.RetryAfterMeta]!.GetValue<int>().ShouldBe(12);
        served.IsError.ShouldNotBe(true);
        Code(tooSoon).ShouldBe("FCMS1634");
        tooSoon.Meta![McpRequests.RetryAfterMeta]!.GetValue<int>().ShouldBe(1);
        again.IsError.ShouldNotBe(true);
        Code(once).ShouldBe("FCMS1634");
    }

    [Fact]
    public async Task TheCallsOfToolsThatChangeDataCountAgainstALowerLimit()
    {
        await using var host = McpSafetyTesting.Host(options => options.Limits = new McpCallLimitOptions { CallsPerMinute = 10, WritesPerMinute = 2 });
        using var http = await KeyAsync(host);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);

        var first = await OkAsync(client, "post_create_draft", new { fields = new { title = "One" } });
        await OkAsync(client, "post_delete", new { id = first.GetProperty("id").GetGuid() });
        var third = await CallAsync(client, "post_create_draft", new { fields = new { title = "Three" } });
        var read = await CallAsync(client, "post_list");
        var listedAsRead = await CallAsync(client, SampleTools.EntryTitles, new { type = "post" });
        host.Time.Advance(TimeSpan.FromSeconds(30));
        var later = await CallAsync(client, "post_create_draft", new { fields = new { title = "Later" } });

        Code(third).ShouldBe("FCMS1634");
        Text(third).ShouldContain("tools that change data");
        Text(third).ShouldContain("Retry in 30 seconds");
        read.IsError.ShouldNotBe(true);
        listedAsRead.IsError.ShouldNotBe(true);
        later.IsError.ShouldNotBe(true);
        (await OkAsync(client, "post_list")).GetProperty("items").EnumerateArray().Select(entry => entry.GetProperty("title").GetString()).ShouldBe(["One", "Later"], ignoreOrder: true);
    }

    // A client that keeps retrying a write the write limit refuses keeps the calls of its reads.
    [Fact]
    public async Task AWriteThatTheWriteLimitRefusesSpendsNoCall()
    {
        await using var host = McpSafetyTesting.Host(options => options.Limits = new McpCallLimitOptions { CallsPerMinute = 3, WritesPerMinute = 1 });
        using var http = await KeyAsync(host);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);

        await OkAsync(client, "post_create_draft", new { fields = new { title = "One" } });
        for (var retry = 0; retry < 5; retry++)
        {
            Code(await CallAsync(client, "post_create_draft", new { fields = new { title = "Again" } })).ShouldBe("FCMS1634");
        }

        (await CallAsync(client, "post_list")).IsError.ShouldNotBe(true);
        (await CallAsync(client, "post_list")).IsError.ShouldNotBe(true);
        var overCalls = await CallAsync(client, "post_list");
        Code(overCalls).ShouldBe("FCMS1634");
        Text(overCalls).ShouldContain("3 tool calls");
    }

    // The first call of a tool lists the tools, and what that list showed of each holds for the next calls of the window.
    [Fact]
    public async Task AListOfTheToolsHoldsForTheCallsOfAWindow()
    {
        var lists = new ListCount();
        await using var host = McpSafetyTesting.Host(_ => { })
            .Configure(cms => cms.AddPlugin<CountedPlugin>())
            .WithServices(services => services.AddSingleton(lists));
        using var http = await KeyAsync(host);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);

        await OkAsync(client, CountedTools.Write);
        await OkAsync(client, CountedTools.Read);
        await OkAsync(client, CountedTools.Write);
        lists.Count.ShouldBe(1);

        host.Time.Advance(McpCallLimits.Window + TimeSpan.FromSeconds(1));
        await OkAsync(client, CountedTools.Read);
        lists.Count.ShouldBe(2);
    }

    // A name no provider lists counts as a tool that changes data, so made-up names cannot be called without end.
    [Fact]
    public async Task ACallOfANameThatIsNoToolCountsAsOneThatChangesData()
    {
        await using var host = McpSafetyTesting.Host(options => options.Limits = new McpCallLimitOptions { CallsPerMinute = 10, WritesPerMinute = 1 });
        using var http = await KeyAsync(host);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);

        var unknown = await Should.ThrowAsync<McpProtocolException>(async () => await CallAsync(client, "made_up_by_a_client"));
        var limited = await CallAsync(client, "made_up_by_a_client");

        unknown.Message.ShouldContain("FCMS1600");
        Code(limited).ShouldBe("FCMS1634");
    }

    [Fact]
    public async Task AClientOverItsLimitIsLoggedOnceAMinute()
    {
        var logs = new Recorded();
        await using var host = McpSafetyTesting.Host(options => options.Limits = new McpCallLimitOptions { CallsPerMinute = 1, WritesPerMinute = 1 })
            .WithServices(services => services.AddLogging(logging => logging.AddProvider(logs)));
        using var http = await KeyAsync(host);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);

        await OkAsync(client, SampleTools.WhoAmI);
        await CallAsync(client, SampleTools.WhoAmI);
        await CallAsync(client, SampleTools.WhoAmI);

        logs.Events.Count(id => id == 9295).ShouldBe(1);
        logs.Events.Count(id => id == 9280).ShouldBe(1);
    }

    [Fact]
    public void ABucketHoldsTheLimitFillsOverAMinuteAndFollowsAClockThatMovesBack()
    {
        var time = new ControllableTimeProvider();
        var limits = new McpCallLimits(Options.Create(new McpOptions { Limits = new McpCallLimitOptions { CallsPerMinute = 60, WritesPerMinute = 6 } }), time);

        Enumerable.Range(0, 60).Select(_ => limits.Call("client:a")).ShouldAllBe(refusal => refusal == null);
        var refused = limits.Call("client:a").ShouldNotBeNull();
        limits.Call("client:b").ShouldBeNull();
        limits.Write("client:a").ShouldBeNull();

        refused.Limit.ShouldBe(60);
        refused.Write.ShouldBeFalse();
        refused.RetryAfter.ShouldBe(TimeSpan.FromSeconds(1));
        refused.Report.ShouldBeTrue();
        limits.Call("client:a").ShouldNotBeNull().Report.ShouldBeFalse();

        // A clock set back takes the bucket with it: it neither fills nor empties.
        time.Advance(TimeSpan.FromHours(-1));
        limits.Call("client:a").ShouldNotBeNull().RetryAfter.ShouldBe(TimeSpan.FromSeconds(1));
        time.Advance(TimeSpan.FromSeconds(1));
        limits.Call("client:a").ShouldBeNull();
        limits.Call("client:a").ShouldNotBeNull();

        time.Advance(TimeSpan.FromMinutes(1));
        Enumerable.Range(0, 60).Select(_ => limits.Call("client:a")).ShouldAllBe(refusal => refusal == null);
    }

    [Fact]
    public void AWriteTheWriteBucketRefusesGivesItsCallBack()
    {
        var limits = new McpCallLimits(Options.Create(new McpOptions { Limits = new McpCallLimitOptions { CallsPerMinute = 3, WritesPerMinute = 1 } }), new ControllableTimeProvider());

        limits.Call("client:a").ShouldBeNull();
        limits.Write("client:a").ShouldBeNull();
        limits.Call("client:a").ShouldBeNull();
        limits.Write("client:a").ShouldNotBeNull().Write.ShouldBeTrue();

        // The refused write took no call: two of the three are left.
        limits.Call("client:a").ShouldBeNull();
        limits.Call("client:a").ShouldBeNull();
        limits.Call("client:a").ShouldNotBeNull().Write.ShouldBeFalse();
    }

    [Fact]
    public async Task LimitsThatMakeNoSenseStopTheHost()
    {
        await using var writesOverCalls = McpSafetyTesting.Host(options => options.Limits = new McpCallLimitOptions { CallsPerMinute = 5, WritesPerMinute = 6 });
        await using var noBulk = McpSafetyTesting.Host(options => options.MaxBulkItems = 0);
        await using var tinyResult = McpSafetyTesting.Host(options => options.MaxResultBytes = 100);

        Should.Throw<OptionsValidationException>(() => writesOverCalls.Services).Message.ShouldContain("WritesPerMinute");
        Should.Throw<OptionsValidationException>(() => noBulk.Services).Message.ShouldContain("MaxBulkItems");
        Should.Throw<OptionsValidationException>(() => tinyResult.Services).Message.ShouldContain("MaxResultBytes");
    }

    private sealed class Recorded : ILoggerProvider
    {
        private readonly List<int> _events = [];

        public IReadOnlyList<int> Events
        {
            get
            {
                lock (_events)
                {
                    return [.. _events];
                }
            }
        }

        public ILogger CreateLogger(string categoryName) => new Sink(this);

        public void Dispose()
        {
        }

        private sealed class Sink(Recorded owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (owner._events)
                {
                    owner._events.Add(eventId.Id);
                }
            }
        }
    }
}

// A tool provider that counts how often it is asked to list, with a tool that reads and one that changes data.
[FluentCmsPlugin("counted", SdkRange = "[1.0.0,2.0.0)")]
public sealed class CountedPlugin : IPlugin
{
    public void ConfigureServices(IPluginServiceBuilder plugin) => plugin.AddMcpTools<CountedTools>();

    public void Configure(IPluginAppBuilder app)
    {
    }
}

public sealed class ListCount
{
    private int _count;

    public int Count => Volatile.Read(ref _count);

    public void Add() => Interlocked.Increment(ref _count);
}

public sealed class CountedTools(ListCount lists) : IMcpToolProvider
{
    public const string Read = "counted_read";
    public const string Write = "counted_write";

    private static readonly JsonElement ObjectSchema = JsonSerializer.SerializeToElement(new { type = "object" });

    public ValueTask<IReadOnlyList<McpToolDescriptor>> ListToolsAsync(McpListRequest request, CancellationToken cancellationToken = default)
    {
        lists.Add();
        return ValueTask.FromResult<IReadOnlyList<McpToolDescriptor>>(
        [
            new(Read, "Reads.", ObjectSchema) { Annotations = new McpToolAnnotations { ReadOnly = true, Idempotent = true } },
            new(Write, "Writes.", ObjectSchema),
        ]);
    }

    public ValueTask<McpToolResult?> CallToolAsync(McpToolCall request, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<McpToolResult?>(request.Name is Read or Write ? McpToolResult.FromJson(JsonSerializer.SerializeToElement(new { tool = request.Name })) : null);
}
