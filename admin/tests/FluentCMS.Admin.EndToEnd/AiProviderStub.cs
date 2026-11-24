using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;

namespace FluentCMS.Admin.EndToEnd;

// A stand-in of an AI provider that speaks the OpenAI chat completions API, on a loopback port over plain HTTP, which the host
// allows as an endpoint (FluentCms:Ai:AllowedEndpoints) and a case names in the site's AI settings, so that no case calls a
// real provider. A streamed answer sends each of Pieces as a chunk; while Gate is set it waits for it after the first, so that
// a case sees the suggestion arrive in parts. Each connection is answered once and closed.
internal sealed class AiProviderStub : IDisposable
{
    private static readonly TimeSpan ConversationLimit = TimeSpan.FromMinutes(2);

    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly string _host;

    // It listens on the address and is named by the host it is reached by, which differ when the host runs in a container.
    public AiProviderStub(IPAddress address, string host)
    {
        _listener = new TcpListener(address, 0);
        _host = host;
        _listener.Start();
        _ = AcceptAsync();
    }

    // The endpoint a site names, under which the client asks /chat/completions.
    public string Endpoint => $"http://{_host}:{((IPEndPoint)_listener.LocalEndpoint).Port}/v1";

    public IReadOnlyList<string> Pieces { get; set; } = ["A suggestion."];

    public TaskCompletionSource? Gate { get; set; }

    // The requests the provider was sent, as their JSON bodies.
    public List<JsonObject> Requests { get; } = [];

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
        _stop.Dispose();
    }

    private async Task AcceptAsync()
    {
        while (true)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch (Exception exception) when (exception is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                return;
            }

            _ = AnswerAsync(client);
        }
    }

    private async Task AnswerAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                using var limit = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                limit.CancelAfter(ConversationLimit);
                await AnswerAsync(client.GetStream(), limit.Token);
            }
            catch (Exception exception) when (exception is OperationCanceledException or SocketException or ObjectDisposedException or IOException)
            {
                // A client that broke off or stalled ends its own answer only.
            }
        }
    }

    private async Task AnswerAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var body = await ReadRequestAsync(stream, cancellationToken);
        var request = JsonNode.Parse(body) as JsonObject ?? [];
        lock (Requests)
        {
            Requests.Add(request);
        }

        var pieces = Pieces;
        var gate = Gate;
        if (request["stream"]?.GetValue<bool>() != true)
        {
            var answer = new JsonObject
            {
                ["id"] = "stub",
                ["object"] = "chat.completion",
                ["created"] = 1,
                ["model"] = "stub",
                ["choices"] = new JsonArray(new JsonObject { ["index"] = 0, ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = string.Concat(pieces) }, ["finish_reason"] = "stop" }),
                ["usage"] = Usage(),
            }.ToJsonString();
            await WriteAsync(stream, $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {Encoding.UTF8.GetByteCount(answer)}\r\nConnection: close\r\n\r\n{answer}", cancellationToken);
            return;
        }

        await WriteAsync(stream, "HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\nCache-Control: no-cache\r\nConnection: close\r\n\r\n", cancellationToken);
        for (var index = 0; index < pieces.Count; index++)
        {
            await WriteAsync(stream, Chunk(new JsonObject { ["content"] = pieces[index] }, null, null), cancellationToken);
            if (index == 0 && gate is not null)
            {
                await gate.Task.WaitAsync(cancellationToken);
            }
        }

        await WriteAsync(stream, Chunk(new JsonObject(), "stop", null), cancellationToken);
        await WriteAsync(stream, Chunk(null, null, Usage()), cancellationToken);
        await WriteAsync(stream, "data: [DONE]\n\n", cancellationToken);
    }

    private static JsonObject Usage() => new() { ["prompt_tokens"] = 12, ["completion_tokens"] = 5, ["total_tokens"] = 17 };

    private static string Chunk(JsonObject? delta, string? finish, JsonObject? usage)
    {
        var chunk = new JsonObject
        {
            ["id"] = "stub",
            ["object"] = "chat.completion.chunk",
            ["created"] = 1,
            ["model"] = "stub",
            ["choices"] = delta is null ? new JsonArray() : new JsonArray(new JsonObject { ["index"] = 0, ["delta"] = delta, ["finish_reason"] = finish }),
        };
        if (usage is not null)
        {
            chunk["usage"] = usage;
        }

        return $"data: {chunk.ToJsonString()}\n\n";
    }

    private static async Task WriteAsync(NetworkStream stream, string text, CancellationToken cancellationToken)
    {
        await stream.WriteAsync(Encoding.UTF8.GetBytes(text), cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    // The head up to its blank line, then a body of its Content-Length or in chunks.
    private static async Task<string> ReadRequestAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var head = new List<byte>();
        var one = new byte[1];
        while (!(head.Count >= 4 && head[^4] == '\r' && head[^3] == '\n' && head[^2] == '\r' && head[^1] == '\n'))
        {
            if (await stream.ReadAsync(one, cancellationToken) == 0)
            {
                throw new IOException("The request ended before its head did.");
            }

            head.Add(one[0]);
        }

        var lines = Encoding.ASCII.GetString([.. head]).Split("\r\n");
        string? Header(string name) => lines.Select(line => line.Split(':', 2)).Where(parts => parts.Length == 2 && parts[0].Trim().Equals(name, StringComparison.OrdinalIgnoreCase)).Select(parts => parts[1].Trim()).FirstOrDefault();

        using var body = new MemoryStream();
        if (Header("Content-Length") is { } length)
        {
            await CopyAsync(stream, body, int.Parse(length, CultureInfo.InvariantCulture), cancellationToken);
        }
        else if (Header("Transfer-Encoding")?.Contains("chunked", StringComparison.OrdinalIgnoreCase) == true)
        {
            while (int.Parse((await LineAsync(stream, cancellationToken)).Split(';')[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture) is var size and > 0)
            {
                await CopyAsync(stream, body, size, cancellationToken);
                await LineAsync(stream, cancellationToken);
            }

            await LineAsync(stream, cancellationToken);
        }

        return Encoding.UTF8.GetString(body.ToArray());
    }

    private static async Task CopyAsync(NetworkStream stream, MemoryStream body, int count, CancellationToken cancellationToken)
    {
        var buffer = new byte[count];
        await stream.ReadExactlyAsync(buffer, cancellationToken);
        await body.WriteAsync(buffer, cancellationToken);
    }

    private static async Task<string> LineAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var line = new StringBuilder();
        var one = new byte[1];
        while (await stream.ReadAsync(one, cancellationToken) > 0 && one[0] != '\n')
        {
            if (one[0] != '\r')
            {
                line.Append((char)one[0]);
            }
        }

        return line.ToString();
    }
}
