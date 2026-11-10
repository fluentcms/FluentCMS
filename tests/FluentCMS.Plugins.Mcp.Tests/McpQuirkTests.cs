using System.Collections.Concurrent;
using System.Text.Json;
using FluentCMS.Agents;
using FluentCMS.Content;
using FluentCMS.Plugins.Sample;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Mcp.Tests.McpTesting;

namespace FluentCMS.Plugins.Mcp.Tests;

// What the server does with a provider that misbehaves: it serves the others, and says so in the log.
public sealed class McpQuirkTests
{
    [Fact]
    public async Task ABrokenProviderADuplicateNameAndABadSchemaAreLeftOutAndLogged()
    {
        var logs = new LogCapture();
        await using var host = Host().Configure(cms => cms.AddPlugin<QuirkyPlugin>()).WithServices(services => services.AddSingleton<ILoggerProvider>(logs));
        using var http = await KeyAsync(host);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);

        var tools = (await client.ListToolsAsync(cancellationToken: Token)).Select(tool => tool.Name).ToList();
        var whoAmI = await client.CallToolAsync(SampleTools.WhoAmI, cancellationToken: Token);

        tools.ShouldBe([SampleTools.WhoAmI, SampleTools.EntryCreate, SampleTools.EntryTitles, SampleTools.NoteAdd, QuirkyTools.Fine]);

        // The earlier plugin answers for the name both list.
        whoAmI.StructuredContent.ShouldNotBeNull().GetProperty("origin").GetString().ShouldBe(nameof(Origin.Mcp));
        logs.Lines.ShouldContain(line => line.Id == 9282 && line.Level == LogLevel.Error && line.Message.Contains(nameof(FailingTools), StringComparison.Ordinal) && line.Message.Contains("quirky", StringComparison.Ordinal));
        logs.Lines.ShouldContain(line => line.Id == 9283 && line.Message.Contains(SampleTools.WhoAmI, StringComparison.Ordinal));
        logs.Lines.ShouldContain(line => line.Id == 9283 && line.Message.Contains(QuirkyTools.BadSchema, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnExceptionThatSaysNothingOfItsCauseIsAnErrorResultWithoutItsMessageAndIsLogged()
    {
        var logs = new LogCapture();
        await using var host = Host().Configure(cms => cms.AddPlugin<QuirkyPlugin>()).WithServices(services => services.AddSingleton<ILoggerProvider>(logs));
        using var http = await KeyAsync(host);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);

        var crashed = await client.CallToolAsync(QuirkyTools.Crash, cancellationToken: Token);
        var fine = await client.CallToolAsync(QuirkyTools.Fine, cancellationToken: Token);

        crashed.IsError.ShouldBe(true);
        Code(crashed).ShouldBe("FCMS3199");
        Text(crashed).ShouldNotContain(QuirkyTools.Secret);
        logs.Lines.ShouldContain(line => line.Id == 9281 && line.Level == LogLevel.Error && line.Message.Contains(QuirkyTools.Crash, StringComparison.Ordinal));
        logs.Lines.ShouldNotContain(line => line.Id >= 9280 && line.Id <= 9299 && line.Message.Contains(QuirkyTools.Secret, StringComparison.Ordinal));
        Text(fine).ShouldBe("fine");
    }
}

// After the sample plugin, so that the sample's tools are the earlier ones.
[FluentCmsPlugin("quirky", SdkRange = "[1.0.0,2.0.0)", DependsOn = ["sample"])]
public sealed class QuirkyPlugin : IPlugin
{
    public void ConfigureServices(IPluginServiceBuilder plugin) => plugin.AddMcpTools<FailingTools>().AddMcpTools<QuirkyTools>();

    public void Configure(IPluginAppBuilder app)
    {
    }
}

public sealed class FailingTools : IMcpToolProvider
{
    public ValueTask<IReadOnlyList<McpToolDescriptor>> ListToolsAsync(McpListRequest request, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("The provider cannot list.");

    public ValueTask<McpToolResult?> CallToolAsync(McpToolCall request, CancellationToken cancellationToken = default) => ValueTask.FromResult<McpToolResult?>(null);
}

public sealed class QuirkyTools : IMcpToolProvider
{
    public const string Fine = "quirky_fine";
    public const string BadSchema = "quirky_bad_schema";
    public const string Crash = "quirky_crash";
    public const string Secret = "Server=db;Password=hunter2";

    private static readonly JsonElement ObjectSchema = Schema("""{ "type": "object" }""");

    public ValueTask<IReadOnlyList<McpToolDescriptor>> ListToolsAsync(McpListRequest request, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<McpToolDescriptor>>(
        [
            new(SampleTools.WhoAmI, "Takes the name of a tool of the sample plugin.", ObjectSchema),
            new(BadSchema, "Has a schema that describes no object.", Schema("""{ "type": "string" }""")),
            new(Fine, "Answers fine.", ObjectSchema),
        ]);

    public ValueTask<McpToolResult?> CallToolAsync(McpToolCall request, CancellationToken cancellationToken = default) => request.Name switch
    {
        Fine => ValueTask.FromResult<McpToolResult?>(McpToolResult.FromText("fine")),
        Crash => throw new InvalidOperationException(Secret),
        _ => ValueTask.FromResult<McpToolResult?>(null),
    };

    private static JsonElement Schema(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}

internal sealed class LogCapture : ILoggerProvider
{
    public ConcurrentQueue<(int Id, LogLevel Level, string Message)> Lines { get; } = new();

    public ILogger CreateLogger(string categoryName) => new Logger(this);

    public void Dispose()
    {
    }

    private sealed class Logger(LogCapture capture) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            capture.Lines.Enqueue((eventId.Id, logLevel, formatter(state, exception)));
    }
}
