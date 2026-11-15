using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentCMS.Core.Diagnostics;

namespace FluentCMS.Cli;

// `fluentcms mcp`: stdio to Streamable HTTP, for an MCP client that speaks stdio only (decision 51). Each line of standard
// input is a JSON-RPC message, posted to /mcp with the key; what the instance answers, as JSON or as a stream of events, is
// written to standard output one message per line. Standard output holds protocol messages only, and every diagnostic goes
// to standard error. The proxy reads no content and adds no tool: it looks only at the ids of requests and answers, so that
// each request gets an answer, at the session id the instance gives, and at the protocol version of the answer to
// initialize, which later requests name.
internal sealed class McpProxy(ManagementClient client, BoundedLineReader input, TextWriter output, TextWriter error, TimeSpan? drainTimeout = null) : IDisposable
{
    public const string Endpoint = "mcp";

    // A line of standard input, an answer and an event of a stream are each held whole, so each is bounded; the MCP plugin
    // takes 1 MiB in a request unless configured otherwise.
    public const int MaxMessageLength = 16 * 1024 * 1024;

    // Standard input ends when the client stops the proxy; the calls in flight have this long to answer, then are cancelled.
    public static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(30);

    private const string SessionHeader = "Mcp-Session-Id";
    private const string ProtocolHeader = "MCP-Protocol-Version";
    private const int InternalError = -32603;
    private const string Limit = "16 MiB";

    private readonly SemaphoreSlim _writing = new(1, 1);
    private readonly TimeSpan _drainTimeout = drainTimeout ?? DrainTimeout;
    private string? _session;
    private string? _protocol;
    private CliException? _fatal;

    // 0 when standard input ends, and 1 when the instance refuses the key, which ends the proxy. Any other failure answers the
    // requests it concerns with a JSON-RPC error and the proxy goes on.
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var stopped = Task.Delay(Timeout.Infinite, stop.Token);
        List<Task> forwarding = [];

        // Messages are forwarded as they come, so that a cancellation reaches the instance while a call is running.
        while (await ReadLineAsync(input, stopped, stop.Token) is { } line)
        {
            if (line.TooLong)
            {
                await WriteErrorAsync($"{CliErrorCodes.McpMessageTooLarge}: a line of standard input of more than {Limit} was dropped, so its requests get no answer.");
            }
            else if (line.Text.Trim().Length > 0)
            {
                forwarding.RemoveAll(task => task.IsCompleted);
                forwarding.Add(ForwardAsync(line.Text, stop));
            }
        }

        await DrainAsync(forwarding, stop);
        if (_fatal is { } fatal)
        {
            await WriteErrorAsync(fatal.Message);
            return fatal.ExitCode;
        }

        await EndSessionAsync();
        return cancellationToken.IsCancellationRequested ? ExitCodes.Failure : ExitCodes.Success;
    }

    private async Task DrainAsync(List<Task> forwarding, CancellationTokenSource stop)
    {
        var all = Task.WhenAll(forwarding);
        if (!stop.IsCancellationRequested)
        {
            using var timer = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
            var finished = await Task.WhenAny(all, Task.Delay(_drainTimeout, timer.Token)) == all;
            await timer.CancelAsync();
            if (!finished && !stop.IsCancellationRequested)
            {
                var running = forwarding.Count(task => !task.IsCompleted);
                await WriteErrorAsync(string.Create(
                    CultureInfo.InvariantCulture,
                    $"note: standard input ended, and {(running == 1 ? "1 call" : $"{running} calls")} still running after {_drainTimeout.TotalSeconds:0.#} seconds {(running == 1 ? "was" : "were")} cancelled."));
                await stop.CancelAsync();
            }
        }

        await all;
    }

    // A read of a console does not always stop when it is cancelled, so the proxy stops waiting for it instead.
    private static async Task<BoundedLine?> ReadLineAsync(BoundedLineReader lines, Task stopped, CancellationToken cancellationToken)
    {
        var read = lines.ReadLineAsync(cancellationToken).AsTask();
        try
        {
            return await Task.WhenAny(read, stopped) == read ? await read : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private async Task ForwardAsync(string line, CancellationTokenSource stop)
    {
        var sent = Sent.Of(line);

        // The requests answered so far, so that a failure after an answer was written errs only for the others.
        var answered = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            using var request = client.Request(HttpMethod.Post, Endpoint);
            request.Content = new StringContent(line, Encoding.UTF8, "application/json");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

            // initialize starts a session, so it names none, and a client that initializes again is not held to the old one.
            var session = sent.IsInitialize ? null : Volatile.Read(ref _session);
            var protocol = sent.IsInitialize ? null : Volatile.Read(ref _protocol);
            if (session is not null)
            {
                request.Headers.TryAddWithoutValidation(SessionHeader, session);
            }

            if (protocol is not null)
            {
                request.Headers.TryAddWithoutValidation(ProtocolHeader, protocol);
            }

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stop.Token);
            if (response.Headers.TryGetValues(SessionHeader, out var sessions) && sessions.FirstOrDefault() is { Length: > 0 } given)
            {
                Volatile.Write(ref _session, given);
            }

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                string body;
                try
                {
                    body = await ReadAsync(response, stop.Token);
                }
                catch (CliException)
                {
                    body = "";
                }

                var problem = ManagementClient.Problem(response, body);
                Stop(stop, new CliException(CliErrorCodes.McpKeyRefused, $"{client.Instance} refused the key at /{Endpoint} ({(int)response.StatusCode}): {problem.Message}"));
                return;
            }

            // A session the instance no longer knows is answered with 404, and so is every later request that names it, so
            // the proxy forgets it; the client's next initialize starts a new one.
            if (response.StatusCode == HttpStatusCode.NotFound && session is not null)
            {
                Interlocked.CompareExchange(ref _session, null, session);
                Interlocked.CompareExchange(ref _protocol, null, protocol);
            }

            // The answer of a request accepted this way would come on a stream the proxy does not open.
            if (response.StatusCode is HttpStatusCode.Accepted or HttpStatusCode.NoContent)
            {
                await AnswerWithErrorAsync(sent, $"{CliErrorCodes.UnexpectedAnswer}: /{Endpoint} accepted the request without answering it, and the proxy opens no stream for a later answer.");
                return;
            }

            if (response.IsSuccessStatusCode && response.Content.Headers.ContentType?.MediaType == "text/event-stream")
            {
                await RelayEventsAsync(response, sent, answered, stop.Token);
                await AnswerWithErrorAsync(sent, $"{CliErrorCodes.UnexpectedAnswer}: the event stream of /{Endpoint} ended without the answer.", answered);
                return;
            }

            var text = await ReadAsync(response, stop.Token);
            if (Message(text, sent) is { } message)
            {
                await WriteAsync(message.Text);
                answered.UnionWith(message.Answered);
                await AnswerWithErrorAsync(sent, $"{CliErrorCodes.UnexpectedAnswer}: /{Endpoint} answered without the answer to this request.", answered);
                return;
            }

            // A problem of FluentCMS, such as a refused size or a rate limit, or an answer that is no message at all.
            var reason = response.IsSuccessStatusCode
                ? new CliException(CliErrorCodes.UnexpectedAnswer, $"/{Endpoint} answered {(int)response.StatusCode} with no JSON-RPC message.")
                : ManagementClient.Problem(response, text);
            await WriteErrorAsync(reason.Message);
            await AnswerWithErrorAsync(sent, reason.Message);
        }
        // Once the proxy stops, an answer still being read is aborted, which some handlers report as a failed read and not
        // as a cancellation.
        catch (Exception exception) when (stop.IsCancellationRequested && exception is OperationCanceledException or HttpRequestException or IOException)
        {
        }
        catch (CliException exception)
        {
            // The instance could not be reached for this request, or its answer was too large; the next one may do.
            await WriteErrorAsync(exception.Message);
            await AnswerWithErrorAsync(sent, exception.Message, answered);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            // The answer broke off while it was read.
            var reason = new CliException(CliErrorCodes.InstanceUnreachable, $"{client.Instance} could not be reached: {exception.Message}");
            await WriteErrorAsync(reason.Message);
            await AnswerWithErrorAsync(sent, reason.Message, answered);
        }
    }

    // Server-sent events: the data lines of an event, joined, are one message. The ids of the requests it answers are added
    // as each is written.
    private async Task RelayEventsAsync(HttpResponseMessage response, Sent sent, HashSet<string> answered, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var lines = BoundedLineReader.Of(stream, MaxMessageLength);
        var data = new StringBuilder();
        var tooLarge = false;
        while (true)
        {
            var line = await lines.ReadLineAsync(cancellationToken);
            if (line is not { } read || (read.Text.Length == 0 && !read.TooLong))
            {
                if (tooLarge)
                {
                    await WriteErrorAsync($"{CliErrorCodes.McpMessageTooLarge}: /{Endpoint} sent an event of more than {Limit}, which was dropped.");
                }
                else if (data.Length > 0 && Message(data.ToString(), sent) is { } message)
                {
                    await WriteAsync(message.Text);
                    answered.UnionWith(message.Answered);
                }

                data.Clear();
                tooLarge = false;
                if (line is null)
                {
                    return;
                }

                continue;
            }

            if (read.TooLong || (!tooLarge && data.Length + read.Text.Length > MaxMessageLength))
            {
                tooLarge = true;
                data.Clear();
            }
            else if (!tooLarge && read.Text.StartsWith("data:", StringComparison.Ordinal))
            {
                var value = read.Text.AsSpan(5);
                data.Append(value.StartsWith(" ") ? value[1..] : value).Append('\n');
            }
        }
    }

    // The message on one line and the ids of the requests it answers, or null when the text is not one: an object of
    // JSON-RPC 2.0, or a batch of them. The answer to initialize names the protocol version.
    private (string Text, IReadOnlyList<string> Answered)? Message(string text, Sent sent)
    {
        try
        {
            using var json = JsonDocument.Parse(text);
            var root = json.RootElement;
            List<JsonElement> items = root.ValueKind == JsonValueKind.Array ? [.. root.EnumerateArray()] : [root];
            if (items.Count == 0 || !items.All(IsJsonRpc))
            {
                return null;
            }

            if (sent.IsInitialize && root.ValueKind == JsonValueKind.Object && root.TryGetProperty("result", out var result)
                && ManagementClient.Text(result, "protocolVersion") is { } version)
            {
                Volatile.Write(ref _protocol, version);
            }

            // An answer has an id and no method; a request or a notification of the instance has a method.
            var answered = items.Where(item => item.TryGetProperty("id", out _) && !item.TryGetProperty("method", out _)).Select(item => IdKey(item.GetProperty("id"))).ToList();
            return (JsonSerializer.Serialize(root), answered);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsJsonRpc(JsonElement element) => ManagementClient.Text(element, "jsonrpc") == "2.0";

    // An id is a string, a number or null, compared by value, so that a server that writes 1 for 1.0 still answers it.
    private static string IdKey(JsonElement id) => id.ValueKind switch
    {
        JsonValueKind.String => "s" + id.GetString(),
        JsonValueKind.Number when id.TryGetDecimal(out var number) => "n" + number.ToString("G29", CultureInfo.InvariantCulture),
        _ => "r" + id.GetRawText(),
    };

    // A request whose answer never came gets an error, so that the client does not wait for it; the requests of a batch get
    // a batch of errors.
    private async Task AnswerWithErrorAsync(Sent sent, string reason, HashSet<string>? answered = null)
    {
        var errors = sent.Ids
            .Where(id => answered is null || !answered.Contains(IdKey(id)))
            .Select(id => new { jsonrpc = "2.0", id, error = new { code = InternalError, message = reason } })
            .ToList();
        if (errors.Count > 0)
        {
            await WriteAsync(sent.IsBatch ? JsonSerializer.Serialize(errors) : JsonSerializer.Serialize(errors[0]));
        }
    }

    // An answer longer than the bound is refused as it is read rather than held.
    private static async Task<string> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength > MaxMessageLength)
        {
            throw TooLarge();
        }

        // A content that buffers itself before it gives its stream may not watch the token, so the wait for it does.
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).WaitAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxMessageLength)
            {
                throw TooLarge();
            }

            buffer.Write(chunk, 0, read);
        }

        buffer.Position = 0;
        using var reader = new StreamReader(buffer, Encoding.UTF8);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    private static CliException TooLarge() =>
        new(CliErrorCodes.McpMessageTooLarge, $"/{Endpoint} answered more than {Limit}, which the proxy does not hold.");

    private async Task WriteAsync(string message)
    {
        await _writing.WaitAsync();
        try
        {
            await output.WriteAsync(message);
            await output.WriteAsync('\n');
            await output.FlushAsync();
        }
        finally
        {
            _writing.Release();
        }
    }

    // A message may repeat what the instance answered.
    private Task WriteErrorAsync(string message) => error.WriteLineAsync(PrintableText.Escape(message, keepLineBreaks: true));

    private void Stop(CancellationTokenSource stop, CliException reason)
    {
        Interlocked.CompareExchange(ref _fatal, reason, null);
        stop.Cancel();
    }

    public void Dispose() => _writing.Dispose();

    // A session the instance gave is ended when the client goes; a failure here changes nothing for the client.
    private async Task EndSessionAsync()
    {
        if (_session is not { } session)
        {
            return;
        }

        try
        {
            using var request = client.Request(HttpMethod.Delete, Endpoint);
            request.Headers.TryAddWithoutValidation(SessionHeader, session);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        }
        catch (Exception exception) when (exception is CliException or OperationCanceledException)
        {
        }
    }

    // What the proxy reads of a message it forwards: the ids of its requests, whose answers it waits for, whether it is a
    // batch, and whether it is initialize.
    private sealed record Sent(IReadOnlyList<JsonElement> Ids, bool IsBatch, bool IsInitialize)
    {
        public static Sent Of(string line)
        {
            try
            {
                using var json = JsonDocument.Parse(line);
                var root = json.RootElement;
                List<JsonElement> items = root.ValueKind == JsonValueKind.Array ? [.. root.EnumerateArray()] : [root];
                var requests = items.Where(item => item.ValueKind == JsonValueKind.Object && item.TryGetProperty("id", out _) && item.TryGetProperty("method", out _)).ToList();
                return new Sent(
                    [.. requests.Select(item => item.GetProperty("id").Clone())],
                    root.ValueKind == JsonValueKind.Array,
                    requests.Any(item => ManagementClient.Text(item, "method") == "initialize"));
            }
            catch (JsonException)
            {
                return new Sent([], IsBatch: false, IsInitialize: false);
            }
        }
    }
}
