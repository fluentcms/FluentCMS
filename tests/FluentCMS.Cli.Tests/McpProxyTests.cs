using System.IO.Pipes;
using System.Net;
using System.Text;
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
        var input = new ClientInput(run.Output, 3, (Initialize, 0), (Initialized, 1), (ListTools, 1), (CallTool, 2));
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

    // A server with sessions answers 404 once a session expired, and then to every request that names it; the proxy forgets
    // it, so that the client's next initialize, which names no session, starts a new one.
    [Fact]
    public async Task ASessionThatTheServerNoLongerKnowsIsForgottenAndTheNextInitializeStartsAnother()
    {
        var server = new SessionServer();
        var run = new InstanceRun(server);
        var again = Initialize.Replace("\"id\":1", "\"id\":3", StringComparison.Ordinal);
        var listAgain = ListTools.Replace("\"id\":2", "\"id\":4", StringComparison.Ordinal);

        var input = new ClientInput(run.Output, 4, (Initialize, 0), (ListTools, 1), (again, 2), (listAgain, 3));
        (await run.RunAsync(input, "mcp", "--url", InstanceFixture.Url, "--token", "fcms_mk_session")).ShouldBe(0, run.Error.ToString());

        var answers = Messages(run.Output.ToString());
        answers.Single(message => message.GetProperty("id").GetInt32() == 2).TryGetProperty("error", out _).ShouldBeTrue();
        answers.Single(message => message.GetProperty("id").GetInt32() == 4).GetProperty("result").GetProperty("tools").GetArrayLength().ShouldBe(0);
        server.Sessions.ShouldBe([null, "s1", null, "s2"]);
        server.Ended.ShouldBe("s2");
    }

    [Fact]
    public async Task ARequestWhoseEventStreamEndsWithoutItsAnswerGetsAJsonRpcError()
    {
        var run = new InstanceRun(new Answering(_ => Events("""{"jsonrpc":"2.0","method":"notifications/progress","params":{"progress":1}}""")));

        (await run.RunAsync(new StringReader(ListTools + "\n"), "mcp", "--url", InstanceFixture.Url, "--token", "fcms_mk_stream")).ShouldBe(0, run.Error.ToString());

        var messages = Messages(run.Output.ToString());
        messages.Count.ShouldBe(2);
        messages[0].GetProperty("method").GetString().ShouldBe("notifications/progress");
        messages[1].GetProperty("id").GetInt32().ShouldBe(2);
        messages[1].GetProperty("error").GetProperty("message").GetString()!.ShouldStartWith("FCMS3361: the event stream of /mcp ended without the answer");
    }

    // An answer that would come later, on a stream the proxy does not open, never comes.
    [Fact]
    public async Task ARequestAcceptedWithoutAnAnswerGetsAJsonRpcError()
    {
        var run = new InstanceRun(new Answering(_ => new HttpResponseMessage(HttpStatusCode.Accepted)));

        (await run.RunAsync(new StringReader(ListTools + "\n" + Initialized + "\n"), "mcp", "--url", InstanceFixture.Url, "--token", "fcms_mk_accepted")).ShouldBe(0, run.Error.ToString());

        var answer = Messages(run.Output.ToString()).Single();
        answer.GetProperty("id").GetInt32().ShouldBe(2);
        answer.GetProperty("error").GetProperty("message").GetString()!.ShouldStartWith("FCMS3361: /mcp accepted the request without answering it");
    }

    [Fact]
    public async Task ABatchThatIsRefusedGetsABatchOfErrorsForItsRequests()
    {
        var run = new InstanceRun(new LimitedThenAnswering());
        const string Batch = """[{"jsonrpc":"2.0","id":1,"method":"tools/list"},{"jsonrpc":"2.0","method":"notifications/initialized"},{"jsonrpc":"2.0","id":"two","method":"prompts/list"}]""";

        (await run.RunAsync(new StringReader(Batch + "\n"), "mcp", "--url", InstanceFixture.Url, "--token", "fcms_mk_batch")).ShouldBe(0, run.Error.ToString());

        using var answer = JsonDocument.Parse(run.Output.ToString().Trim());
        var errors = answer.RootElement.EnumerateArray().ToList();
        errors.Select(error => error.GetProperty("id").ToString()).ShouldBe(["1", "two"]);
        errors.ShouldAllBe(error => error.GetProperty("error").GetProperty("message").GetString()!.StartsWith("FCMS3110", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnAnswerOverTheBoundIsNotReadAndItsRequestGetsAJsonRpcError()
    {
        var run = new InstanceRun(new Answering(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new Oversized() }));

        (await run.RunAsync(new StringReader(ListTools + "\n"), "mcp", "--url", InstanceFixture.Url, "--token", "fcms_mk_large")).ShouldBe(0, run.Error.ToString());

        var answer = Messages(run.Output.ToString()).Single();
        answer.GetProperty("id").GetInt32().ShouldBe(2);
        answer.GetProperty("error").GetProperty("message").GetString()!.ShouldStartWith("FCMS3317: /mcp answered more than 16 MiB");
        run.Error.ToString().ShouldStartWith("FCMS3317: ");
    }

    [Fact]
    public async Task TheCallsInFlightWhenStandardInputEndsAreCancelledAfterTheDrainTimeout()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var client = new ManagementClient(new Instance(new Uri(InstanceFixture.Url), "fcms_mk_drain"), new HttpClient(new Answering(_ => null)));
        using var proxy = new McpProxy(client, BoundedLineReader.Of(new StringReader(ListTools + "\n"), McpProxy.MaxMessageLength), output, error, TimeSpan.FromMilliseconds(500));

        (await proxy.RunAsync(TestContext.Current.CancellationToken)).ShouldBe(0);

        output.ToString().ShouldBeEmpty();
        error.ToString().ShouldBe("note: standard input ended, and 1 call still running after 0.5 seconds was cancelled." + Environment.NewLine);
        client.Dispose();
    }

    // Ctrl+C cancels the command, and the proxy ends the session the instance gave before it exits.
    [Fact]
    public async Task ACancelledProxyEndsItsSession()
    {
        var server = new SessionServer();
        var run = new InstanceRun(server);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var input = new HeldOpen(Initialize);

        var running = CommandLine.RunAsync(["mcp", "--url", InstanceFixture.Url, "--token", "fcms_mk_cancel"], run.Output, run.Error, launcher: null, _ => new HttpClient(server, disposeHandler: false), input, cancel.Token);

        // The proxy keeps the session before it writes the answer that gave it.
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (run.Output.Lines < 1 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        await cancel.CancelAsync();

        (await running).ShouldBe(1);
        server.Ended.ShouldBe("s1");
    }

    [Fact]
    public async Task ALineOverTheBoundIsGivenAsTooLongWithoutItsText()
    {
        var lines = BoundedLineReader.Of(new StringReader("short\n" + new string('x', 20) + "\nfits\r\n0123456789\nend"), 10);
        List<BoundedLine> read = [];
        while (await lines.ReadLineAsync(TestContext.Current.CancellationToken) is { } line)
        {
            read.Add(line);
        }

        read.ShouldBe([new("short", false), new("", true), new("fits", false), new("0123456789", false), new("end", false)]);
    }

    // A line ends at a line feed, a carriage return or both, as an event stream may end its lines.
    [Fact]
    public async Task ALoneCarriageReturnEndsALineAndALineFeedAfterOneDoesNot()
    {
        var lines = BoundedLineReader.Of(new MemoryStream(Encoding.UTF8.GetBytes("one\rtwo\r\n\rthree\nfour")), 100);
        List<string> read = [];
        while (await lines.ReadLineAsync(TestContext.Current.CancellationToken) is { } line)
        {
            read.Add(line.Text);
        }

        read.ShouldBe(["one", "two", "", "three", "four"]);
    }

    // A client keeps its end of standard input open while it waits for the answer, so a line is read as soon as it is
    // there, whatever its length; StreamReader went on reading while each read filled its buffer of 1024 bytes.
    [Theory]
    [InlineData(1023, 1)]
    [InlineData(1024, 1)]
    [InlineData(2048, 1)]
    [InlineData(4096, 1)]
    [InlineData(8192, 1)]
    [InlineData(65536, 1)]
    [InlineData(1024, 3)]
    [InlineData(3072, 3)]
    public async Task ALineOnAPipeIsReadWhileTheWriterIsStillOpen(int bytes, int bytesPerCharacter)
    {
        using var writer = new AnonymousPipeServerStream(PipeDirection.Out);
        using var reader = new AnonymousPipeClientStream(PipeDirection.In, writer.ClientSafePipeHandle);
        var character = bytesPerCharacter == 3 ? ((char)0x20AC).ToString() : "x";
        var line = string.Concat(Enumerable.Repeat(character, (bytes - 1) / bytesPerCharacter)) + new string('y', (bytes - 1) % bytesPerCharacter);
        var lines = BoundedLineReader.Of(reader, McpProxy.MaxMessageLength);

        var reading = lines.ReadLineAsync(TestContext.Current.CancellationToken).AsTask();
        await writer.WriteAsync(Encoding.UTF8.GetBytes(line + "\n"), TestContext.Current.CancellationToken);
        await writer.FlushAsync(TestContext.Current.CancellationToken);

        var read = await reading.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        read.ShouldBe(new BoundedLine(line, TooLong: false));
    }

    // The event stream of an answer is read as it comes, while the server keeps it open.
    [Fact]
    public async Task AnEventOfAKibibyteIsRelayedWhileTheStreamIsStillOpen()
    {
        using var writer = new AnonymousPipeServerStream(PipeDirection.Out);
        var reader = new AnonymousPipeClientStream(PipeDirection.In, writer.ClientSafePipeHandle);
        var run = new InstanceRun(new Answering(_ =>
        {
            var content = new StreamContent(reader);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }));
        var frame = "data: {\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{\"pad\":\"PAD\"}}\n\n";
        var padded = frame.Replace("PAD", new string('p', 1024 - Encoding.UTF8.GetByteCount(frame) + 3), StringComparison.Ordinal);
        Encoding.UTF8.GetByteCount(padded).ShouldBe(1024);

        var running = run.RunAsync(new StringReader(ListTools + "\n"), "mcp", "--url", InstanceFixture.Url, "--token", "fcms_mk_pipe");
        await writer.WriteAsync(Encoding.UTF8.GetBytes(padded), TestContext.Current.CancellationToken);
        await writer.FlushAsync(TestContext.Current.CancellationToken);

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (run.Output.Lines < 1 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        var relayed = run.Output.Lines;
        writer.Dispose();
        (await running).ShouldBe(0, run.Error.ToString());
        relayed.ShouldBe(1);
        Messages(run.Output.ToString()).Single().GetProperty("result").GetProperty("pad").GetString()!.ShouldStartWith("ppp");
    }

    // The answer was written before the stream broke, so the request gets no error as well.
    [Fact]
    public async Task ARequestAnsweredBeforeItsStreamBrokeGetsNoErrorToo()
    {
        var run = new InstanceRun(new Answering(_ =>
        {
            var content = new StreamContent(new BreakingStream(Encoding.UTF8.GetBytes("data: {\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{}}\n\n")));
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }));

        (await run.RunAsync(new StringReader(ListTools + "\n"), "mcp", "--url", InstanceFixture.Url, "--token", "fcms_mk_broken")).ShouldBe(0, run.Error.ToString());

        Messages(run.Output.ToString()).Single().TryGetProperty("result", out _).ShouldBeTrue();
        run.Error.ToString().ShouldStartWith("FCMS3329: ");
    }

    // A server may write an id as 1 where the request wrote 1.0; the ids are compared by value.
    [Fact]
    public async Task AnAnswerWhoseIdIsWrittenOtherwiseStillAnswersItsRequest()
    {
        var run = new InstanceRun(new Answering(_ => Events("""{"jsonrpc":"2.0","id":1,"result":{}}""")));
        const string Request = """{"jsonrpc":"2.0","id":1.0,"method":"tools/list"}""";

        (await run.RunAsync(new StringReader(Request + "\n"), "mcp", "--url", InstanceFixture.Url, "--token", "fcms_mk_ids")).ShouldBe(0, run.Error.ToString());

        Messages(run.Output.ToString()).Single().TryGetProperty("result", out _).ShouldBeTrue();
    }

    // A JSON answer that leaves out a request of the batch: that request gets an error.
    [Fact]
    public async Task ARequestThatAJsonAnswerLeavesOutGetsAJsonRpcError()
    {
        var run = new InstanceRun(new Answering(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""[{"jsonrpc":"2.0","id":1,"result":{}}]""", Encoding.UTF8, "application/json"),
        }));
        const string Batch = """[{"jsonrpc":"2.0","id":1,"method":"tools/list"},{"jsonrpc":"2.0","id":2,"method":"prompts/list"}]""";

        (await run.RunAsync(new StringReader(Batch + "\n"), "mcp", "--url", InstanceFixture.Url, "--token", "fcms_mk_partial")).ShouldBe(0, run.Error.ToString());

        var messages = Messages(run.Output.ToString());
        messages.Count.ShouldBe(2);
        messages[0][0].GetProperty("id").GetInt32().ShouldBe(1);
        messages[1][0].GetProperty("id").GetInt32().ShouldBe(2);
        messages[1][0].GetProperty("error").GetProperty("message").GetString()!.ShouldStartWith("FCMS3361: /mcp answered without the answer to this request");
    }

    private static List<JsonElement> Messages(string output) =>
        [.. output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonDocument.Parse(line).RootElement.Clone())];

    private static HttpResponseMessage Events(params string[] messages) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(string.Concat(messages.Select(message => $"event: message\ndata: {message}\n\n")), System.Text.Encoding.UTF8, "text/event-stream"),
    };

    // Answers each POST with what the function gives, or never when it gives null.
    private sealed class Answering(Func<string, HttpResponseMessage?> answer) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is { } content ? await content.ReadAsStringAsync(cancellationToken) : "";
            if (answer(body) is { } response)
            {
                return response;
            }

            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("Not reached.");
        }
    }

    // A server with sessions: initialize starts the next one, s1 then s2; s1 has expired by the next request that names it.
    private sealed class SessionServer : HttpMessageHandler
    {
        private int _started;

        public List<string?> Sessions { get; } = [];

        public string? Ended { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var session = request.Headers.TryGetValues("Mcp-Session-Id", out var values) ? values.Single() : null;
            if (request.Method == HttpMethod.Delete)
            {
                Ended = session;
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            using var message = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var id = message.RootElement.GetProperty("id").GetRawText();
            lock (Sessions)
            {
                Sessions.Add(session);
            }

            if (message.RootElement.GetProperty("method").GetString() == "initialize")
            {
                var started = $"s{Interlocked.Increment(ref _started)}";
                var answer = Json("""{"jsonrpc":"2.0","id":ID,"result":{"protocolVersion":"2025-06-18","capabilities":{},"serverInfo":{"name":"s","version":"1"}}}""".Replace("ID", id, StringComparison.Ordinal));
                answer.Headers.Add("Mcp-Session-Id", started);
                return answer;
            }

            return session == "s1" ? new HttpResponseMessage(HttpStatusCode.NotFound) : Json("""{"jsonrpc":"2.0","id":ID,"result":{"tools":[]}}""".Replace("ID", id, StringComparison.Ordinal));
        }

        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
    }

    // An answer whose length is over the proxy's bound, which it refuses before reading.
    private sealed class Oversized : HttpContent
    {
        public Oversized() => Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => throw new InvalidOperationException("The proxy read an answer over its bound.");

        protected override bool TryComputeLength(out long length)
        {
            length = McpProxy.MaxMessageLength + 1L;
            return true;
        }
    }

    // A stream that gives its bytes and then breaks, as a connection that drops does.
    private sealed class BreakingStream(byte[] bytes) : Stream
    {
        private bool _given;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_given)
            {
                throw new IOException("The connection broke.");
            }

            _given = true;
            bytes.CopyTo(buffer);
            return ValueTask.FromResult(bytes.Length);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    // Standard input that gives its lines and then stays open, as a client's does until it stops the proxy.
    private sealed class HeldOpen(params string[] lines) : TextReader
    {
        private string _pending = string.Concat(lines.Select(line => line + "\n"));

        public override async ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default)
        {
            if (_pending.Length == 0)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            var count = Math.Min(buffer.Length, _pending.Length);
            _pending.AsSpan(0, count).CopyTo(buffer.Span);
            _pending = _pending[count..];
            return count;
        }
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

    // Standard input of a client that sends each line once standard output holds the number of messages given with it, and
    // closes its end once it holds all the answers.
    private sealed class ClientInput(LineCountingWriter output, int answers, params (string Line, int AfterMessages)[] lines) : TextReader
    {
        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

        private int _next;
        private string _pending = "";

        public override async ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default)
        {
            if (_pending.Length == 0)
            {
                if (await ReadLineAsync(cancellationToken) is not { } line)
                {
                    return 0;
                }

                _pending = line + "\n";
            }

            var count = Math.Min(buffer.Length, _pending.Length);
            _pending.AsSpan(0, count).CopyTo(buffer.Span);
            _pending = _pending[count..];
            return count;
        }

        public override async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            if (_next == lines.Length)
            {
                await WaitForAsync(answers, cancellationToken);
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
