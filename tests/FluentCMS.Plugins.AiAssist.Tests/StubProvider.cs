using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Plugins.AiAssist.Tests;

public sealed record StubRequest(string Method, string Path, IReadOnlyDictionary<string, string> Headers, JsonNode? Body);

// Where a stream carries its usage. OwnChunk is a chunk of its own after the one that ends the answer, as OpenAI and
// Ollama's /v1 send it when they are asked to include it; LastChunk puts it on the chunk that ends the answer, as some
// compatible servers do; None sends no usage, streamed or not.
public enum StubUsage
{
    OwnChunk,
    LastChunk,
    None,
}

// A local stand-in for a provider on a loopback port of its own, so that the default client factory is run for real and
// no provider is called. It records every request and answers Answer in the chat completions format of OpenAI, streamed or
// not. Status answers a failure instead, and RedirectTo a redirect.
public sealed class StubProvider : IAsyncDisposable
{
    public const int PromptTokens = 11;
    public const int CompletionTokens = 7;

    // An answer without usage leaves the member out.
    private static readonly JsonSerializerOptions WithoutNulls = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    private readonly ConcurrentQueue<StubRequest> _requests = new();
    private TaskCompletionSource _received = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private WebApplication? _app;

    public string Answer { get; set; } = "From the stub.";

    public int Status { get; set; } = StatusCodes.Status200OK;

    public Uri? RedirectTo { get; set; }

    public StubUsage Usage { get; set; } = StubUsage.OwnChunk;

    // How long a stream waits before the chunk of its own that carries the usage.
    public TimeSpan UsageDelay { get; set; }

    // How long a slow provider waits before it answers, and a stream after its first chunk; the wait ends when the
    // request is aborted.
    public TimeSpan Pause { get; set; }

    public Uri Root { get; private set; } = null!;

    public IReadOnlyList<StubRequest> Requests => [.. _requests];

    // Completes when the next request has arrived.
    public Task Received => _received.Task;

    public string Url(string path = "") => new Uri(Root, path).ToString();

    public static async Task<StubProvider> StartAsync()
    {
        var stub = new StubProvider();
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        app.Run(stub.HandleAsync);
        await app.StartAsync();

        stub._app = app;
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        stub.Root = new Uri(address.TrimEnd('/') + "/");
        return stub;
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    private async Task HandleAsync(HttpContext context)
    {
        using var reader = new StreamReader(context.Request.Body);
        var text = await reader.ReadToEndAsync(context.RequestAborted);
        var body = text.Length == 0 ? null : JsonNode.Parse(text);
        _requests.Enqueue(new StubRequest(
            context.Request.Method,
            context.Request.Path,
            context.Request.Headers.ToDictionary(header => header.Key, header => header.Value.ToString(), StringComparer.OrdinalIgnoreCase),
            body));
        Interlocked.Exchange(ref _received, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();

        if (RedirectTo is not null)
        {
            context.Response.Redirect(RedirectTo.ToString());
            return;
        }

        if (Status != StatusCodes.Status200OK)
        {
            context.Response.StatusCode = Status;
            await context.Response.WriteAsJsonAsync(new { error = new { message = "The stub refused: " + text, type = "invalid_request_error" } }, context.RequestAborted);
            return;
        }

        var model = body?["model"]?.GetValue<string>() ?? "";
        var path = context.Request.Path.Value ?? "";
        try
        {
            if (path.EndsWith("/chat/completions", StringComparison.Ordinal) && body?["stream"]?.GetValue<bool>() == true)
            {
                await StreamCompletionAsync(context, model);
            }
            else if (path.EndsWith("/chat/completions", StringComparison.Ordinal))
            {
                await WaitAsync(Pause, context);
                await context.Response.WriteAsJsonAsync(
                    new
                    {
                        id = "chatcmpl-stub",
                        @object = "chat.completion",
                        created = 1_700_000_000,
                        model,
                        choices = new[] { new { index = 0, message = new { role = "assistant", content = Answer }, finish_reason = "stop" } },
                        usage = Usage == StubUsage.None ? null : Used(),
                    },
                    WithoutNulls,
                    context.RequestAborted);
            }
            else
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
            }
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
        }
    }

    // The answer in two chunks, the second ending it, and the usage where Usage says.
    private async Task StreamCompletionAsync(HttpContext context, string model)
    {
        context.Response.ContentType = "text/event-stream";
        var half = Answer.Length / 2;
        await ChunkAsync(context, new { id = "chatcmpl-stub", @object = "chat.completion.chunk", created = 1_700_000_000, model, choices = new[] { new { index = 0, delta = new { role = "assistant", content = Answer[..half] }, finish_reason = (string?)null } } });
        await WaitAsync(Pause, context);
        await ChunkAsync(context, new { id = "chatcmpl-stub", @object = "chat.completion.chunk", created = 1_700_000_000, model, choices = new[] { new { index = 0, delta = new { content = Answer[half..] }, finish_reason = "stop" } }, usage = Usage == StubUsage.LastChunk ? Used() : null });
        if (Usage == StubUsage.OwnChunk)
        {
            await WaitAsync(UsageDelay, context);
            await ChunkAsync(context, new { id = "chatcmpl-stub", @object = "chat.completion.chunk", created = 1_700_000_000, model, choices = Array.Empty<object>(), usage = Used() });
        }

        await context.Response.WriteAsync("data: [DONE]\n\n", context.RequestAborted);
    }

    private static async Task ChunkAsync(HttpContext context, object chunk)
    {
        await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(chunk)}\n\n", context.RequestAborted);
        await context.Response.Body.FlushAsync(context.RequestAborted);
    }

    private static Task WaitAsync(TimeSpan pause, HttpContext context) =>
        pause > TimeSpan.Zero ? Task.Delay(pause, context.RequestAborted) : Task.CompletedTask;

    private static object Used() => new { prompt_tokens = PromptTokens, completion_tokens = CompletionTokens, total_tokens = PromptTokens + CompletionTokens };
}
