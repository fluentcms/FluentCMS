using System.Net;
using System.Text.Json;
using FluentCMS.Core.Authorization;
using Shouldly;
using Xunit;

namespace FluentCMS.Cli.Tests;

// `fluentcms mcp` between a client that speaks stdio and the instance's /mcp: protocol messages only on standard output.
public sealed class McpProxyTests(InstanceFixture instance) : IClassFixture<InstanceFixture>
{
    private const string Initialize = """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"stdio-client","version":"1.0.0"}}}""";
    private const string Initialized = """{"jsonrpc":"2.0","method":"notifications/initialized"}""";
    private const string ListTools = """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""";
    private const string CallTool = """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"note_list","arguments":{}}}""";

    [Fact]
    public async Task AStdioClientListsTheToolsOfAnInstanceAndCallsOne()
    {
        var run = new InstanceRun(instance.Handler);
        var key = await instance.KeyAsync();

        // A client waits for the answer to initialize before it goes on; the input below does the same.
        var input = new ClientInput(run.Output, (Initialize, 0), (Initialized, 1), (ListTools, 1), (CallTool, 2));
        var exitCode = await run.RunAsync(input, "mcp", "--url", InstanceFixture.Url, "--token", key);

        exitCode.ShouldBe(0, run.Error.ToString());
        var messages = run.Output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonDocument.Parse(line).RootElement).ToList();
        messages.Count.ShouldBe(3, run.Output.ToString());
        messages.ShouldAllBe(message => message.GetProperty("jsonrpc").GetString() == "2.0");

        var initialized = messages.Single(message => message.GetProperty("id").GetInt32() == 1);
        initialized.GetProperty("result").GetProperty("protocolVersion").GetString().ShouldNotBeNullOrEmpty();

        var tools = messages.Single(message => message.GetProperty("id").GetInt32() == 2).GetProperty("result").GetProperty("tools").EnumerateArray().Select(tool => tool.GetProperty("name").GetString()).ToList();
        tools.ShouldContain("note_list");

        var called = messages.Single(message => message.GetProperty("id").GetInt32() == 3).GetProperty("result");
        (called.TryGetProperty("isError", out var isError) && isError.GetBoolean()).ShouldBeFalse(called.ToString());
        called.GetProperty("content").GetArrayLength().ShouldBeGreaterThan(0);
        run.Error.ToString().ShouldBeEmpty();
    }

    [Fact]
    public async Task ARefusedKeyEndsTheProxyWithAMessageOnStandardError()
    {
        var run = new InstanceRun(instance.Handler);

        var exitCode = await run.RunAsync(new StringReader(Initialize + "\n" + ListTools + "\n"), "mcp", "--url", InstanceFixture.Url, "--token", "fcms_mk_" + new string('a', 43));

        exitCode.ShouldBe(1);
        run.Output.ToString().ShouldBeEmpty();
        run.Error.ToString().ShouldStartWith("FCMS3362: http://localhost/ refused the key at /mcp (401): ");
        run.Error.ToString().ShouldNotContain(new string('a', 43));
    }

    // The answer to a request still in flight is aborted when the key is refused, as a test host aborts it with an IOException.
    [Fact]
    public async Task AnAnswerAbortedByTheRefusalOfAnotherRequestDoesNotFailTheProxy()
    {
        var run = new InstanceRun(new RefusedWhileAnotherIsRead());

        var exitCode = await run.RunAsync(new StringReader(Initialize + "\n" + ListTools + "\n"), "mcp", "--url", InstanceFixture.Url, "--token", "fcms_mk_" + new string('c', 43));

        exitCode.ShouldBe(1);
        run.Output.ToString().ShouldBeEmpty();
        run.Error.ToString().ShouldBe("FCMS3362: http://localhost/ refused the key at /mcp (401): FCMS1001: Unknown key." + Environment.NewLine);
    }

    // A problem of FluentCMS is not a protocol message: the request gets a JSON-RPC error, the problem goes to standard
    // error, and the proxy goes on with the next message.
    [Fact]
    public async Task AnAnswerThatIsNoProtocolMessageBecomesAJsonRpcErrorAndTheProxyGoesOn()
    {
        var run = new InstanceRun(new LimitedThenAnswering());

        var exitCode = await run.RunAsync(new StringReader(ListTools + "\n" + Initialized + "\n"), "mcp", "--url", InstanceFixture.Url, "--token", "fcms_mk_" + new string('b', 43));

        exitCode.ShouldBe(0, run.Error.ToString());
        var answer = JsonDocument.Parse(run.Output.ToString().Trim()).RootElement;
        answer.GetProperty("id").GetInt32().ShouldBe(2);
        answer.GetProperty("error").GetProperty("message").GetString()!.ShouldStartWith("FCMS3110");
        run.Error.ToString().ShouldStartWith("FCMS3110");
        run.Output.ToString().ShouldNotContain("problem");
    }

    [Fact]
    public async Task McpNeedsAnInstance()
    {
        var run = new InstanceRun(instance.Handler);

        (await run.RunAsync("mcp")).ShouldBe(2);
        run.Error.ToString().ShouldContain("--url");
    }

    // The first request is refused with a problem, as a rate limit refuses it; a notification after it is accepted.
    private sealed class LimitedThenAnswering : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            request.Content is { } content && (await content.ReadAsStringAsync(cancellationToken)).Contains("tools/list", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                {
                    Content = new StringContent("""{"type":"problem","title":"Too many requests","status":429,"detail":"Slow down.","code":"FCMS3110"}""", System.Text.Encoding.UTF8, "application/problem+json"),
                }
                : new HttpResponseMessage(HttpStatusCode.Accepted);
    }

    // tools/list is refused with an answer that is aborted once the proxy stops; initialize is refused once that answer is read.
    private sealed class RefusedWhileAnotherIsRead : HttpMessageHandler
    {
        private readonly TaskCompletionSource _reading = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if ((await request.Content!.ReadAsStringAsync(cancellationToken)).Contains("tools/list", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new AbortedContent(_reading) };
            }

            await _reading.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("""{"type":"problem","title":"Unauthorized","status":401,"detail":"Unknown key.","code":"FCMS1001"}""", System.Text.Encoding.UTF8, "application/problem+json"),
            };
        }
    }

    private sealed class AbortedContent(TaskCompletionSource reading) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            reading.TrySetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw new IOException("The client aborted the request.");
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    // Standard input of a client that sends each line once standard output holds the number of messages given with it.
    private sealed class ClientInput(LineCountingWriter output, params (string Line, int AfterMessages)[] lines) : TextReader
    {
        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

        private int _next;

        public override async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            if (_next == lines.Length)
            {
                // The client stays until every answer is in, and then closes its end.
                await WaitForAsync(3, cancellationToken);
                return null;
            }

            var (line, after) = lines[_next++];
            await WaitForAsync(after, cancellationToken);
            return line;
        }

        private async Task WaitForAsync(int messages, CancellationToken cancellationToken)
        {
            var deadline = DateTime.UtcNow + Patience;
            while (output.Lines < messages)
            {
                if (DateTime.UtcNow > deadline)
                {
                    throw new TimeoutException($"standard output held {output.Lines} messages, not {messages}: {output}");
                }

                await Task.Delay(10, cancellationToken);
            }
        }
    }
}
