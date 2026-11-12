using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace FluentCMS.Cli;

// `fluentcms mcp`: stdio to Streamable HTTP, for an MCP client that speaks stdio only (decision 51). Each line of standard
// input is a JSON-RPC message, posted to /mcp with the key; what the instance answers, as JSON or as a stream of events, is
// written to standard output one message per line. Standard output holds protocol messages only, and every diagnostic goes
// to standard error. The proxy reads no content and adds no tool: it looks only at the session id the instance gives, and at
// the protocol version of the answer to initialize, which later requests name.
internal sealed class McpProxy(ManagementClient client, TextReader input, TextWriter output, TextWriter error) : IDisposable
{
    public const string Endpoint = "mcp";

    private const string SessionHeader = "Mcp-Session-Id";
    private const string ProtocolHeader = "MCP-Protocol-Version";
    private const int InternalError = -32603;

    private readonly SemaphoreSlim _writing = new(1, 1);
    private string? _session;
    private string? _protocol;
    private CliException? _fatal;

    // 0 when standard input ends, and 1 when the instance refuses the key, which ends the proxy. Any other failure answers the
    // one request it concerns with a JSON-RPC error and the proxy goes on.
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var stopped = Task.Delay(Timeout.Infinite, stop.Token);
        List<Task> forwarding = [];

        // Messages are forwarded as they come, so that a cancellation reaches the instance while a call is running.
        while (await ReadLineAsync(stopped, stop.Token) is { } line)
        {
            if (line.Trim().Length > 0)
            {
                forwarding.RemoveAll(task => task.IsCompleted);
                forwarding.Add(ForwardAsync(line, stop));
            }
        }

        await Task.WhenAll(forwarding);
        if (_fatal is { } fatal)
        {
            await error.WriteLineAsync(fatal.Message);
            return fatal.ExitCode;
        }

        await EndSessionAsync();
        return cancellationToken.IsCancellationRequested ? ExitCodes.Failure : ExitCodes.Success;
    }

    // A read of a console does not always stop when it is cancelled, so the proxy stops waiting for it instead.
    private async Task<string?> ReadLineAsync(Task stopped, CancellationToken cancellationToken)
    {
        var read = input.ReadLineAsync(cancellationToken).AsTask();
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
        try
        {
            using var request = client.Request(HttpMethod.Post, Endpoint);
            request.Content = new StringContent(line, Encoding.UTF8, "application/json");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            if (Volatile.Read(ref _session) is { } session)
            {
                request.Headers.TryAddWithoutValidation(SessionHeader, session);
            }

            if (Volatile.Read(ref _protocol) is { } protocol)
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
                var problem = ManagementClient.Problem(response, await response.Content.ReadAsStringAsync(stop.Token));
                Stop(stop, new CliException(CliErrorCodes.McpKeyRefused, $"{client.Instance} refused the key at /{Endpoint} ({(int)response.StatusCode}): {problem.Message}"));
                return;
            }

            if (response.StatusCode is HttpStatusCode.Accepted or HttpStatusCode.NoContent)
            {
                return;
            }

            if (response.IsSuccessStatusCode && response.Content.Headers.ContentType?.MediaType == "text/event-stream")
            {
                await RelayEventsAsync(response, stop.Token);
                return;
            }

            var body = await response.Content.ReadAsStringAsync(stop.Token);
            if (Message(body) is { } message)
            {
                await WriteAsync(message);
                return;
            }

            // A problem of FluentCMS, such as a refused size or a rate limit, or an answer that is no message at all.
            var reason = response.IsSuccessStatusCode
                ? new CliException(CliErrorCodes.UnexpectedAnswer, $"/{Endpoint} answered {(int)response.StatusCode} with no JSON-RPC message.")
                : ManagementClient.Problem(response, body);
            await error.WriteLineAsync(reason.Message);
            await AnswerWithErrorAsync(line, reason.Message);
        }
        // Once the proxy stops, an answer still being read is aborted, which some handlers report as a failed read and not
        // as a cancellation.
        catch (Exception exception) when (stop.IsCancellationRequested && exception is OperationCanceledException or HttpRequestException or IOException)
        {
        }
        catch (CliException exception)
        {
            // The instance could not be reached for this request; the next one may reach it.
            await error.WriteLineAsync(exception.Message);
            await AnswerWithErrorAsync(line, exception.Message);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            // The answer broke off while it was read.
            var reason = new CliException(CliErrorCodes.InstanceUnreachable, $"{client.Instance} could not be reached: {exception.Message}");
            await error.WriteLineAsync(reason.Message);
            await AnswerWithErrorAsync(line, reason.Message);
        }
    }

    // Server-sent events: the data lines of an event, joined, are one message.
    private async Task RelayEventsAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var data = new StringBuilder();
        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null || line.Length == 0)
            {
                if (data.Length > 0 && Message(data.ToString()) is { } message)
                {
                    await WriteAsync(message);
                }

                data.Clear();
                if (line is null)
                {
                    return;
                }

                continue;
            }

            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                var value = line.AsSpan(5);
                data.Append(value.StartsWith(" ") ? value[1..] : value).Append('\n');
            }
        }
    }

    // The message on one line, or null when the text is not one: an object of JSON-RPC 2.0, or a batch of them. The answer to
    // initialize names the protocol version.
    private string? Message(string text)
    {
        try
        {
            using var json = JsonDocument.Parse(text);
            var root = json.RootElement;
            var isMessage = root.ValueKind == JsonValueKind.Array
                ? root.GetArrayLength() > 0 && root.EnumerateArray().All(IsJsonRpc)
                : IsJsonRpc(root);
            if (!isMessage)
            {
                return null;
            }

            if (Volatile.Read(ref _protocol) is null && root.ValueKind == JsonValueKind.Object && root.TryGetProperty("result", out var result)
                && ManagementClient.Text(result, "protocolVersion") is { } version)
            {
                Volatile.Write(ref _protocol, version);
            }

            return JsonSerializer.Serialize(root);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsJsonRpc(JsonElement element) => ManagementClient.Text(element, "jsonrpc") == "2.0";

    // A request whose answer never came gets an error, so that the client does not wait for it.
    private async Task AnswerWithErrorAsync(string line, string reason)
    {
        try
        {
            using var json = JsonDocument.Parse(line);
            if (json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("id", out var id) && json.RootElement.TryGetProperty("method", out _))
            {
                await WriteAsync(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, error = new { code = InternalError, message = reason } }));
            }
        }
        catch (JsonException)
        {
        }
    }

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
}
