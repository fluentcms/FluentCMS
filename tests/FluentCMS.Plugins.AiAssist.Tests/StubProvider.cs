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

// A local stand-in for a provider on a loopback port of its own, so that the default client factory is run for real and
// no provider is called. It records every request and answers Answer in the wire format of the path it is asked on: the
// chat completions of OpenAI, the chat of Ollama or the messages of Anthropic. Status answers a failure instead, and
// RedirectTo a redirect.
public sealed class StubProvider : IAsyncDisposable
{
    public const int PromptTokens = 11;
    public const int CompletionTokens = 7;

    private readonly ConcurrentQueue<StubRequest> _requests = new();
    private WebApplication? _app;

    public string Answer { get; set; } = "From the stub.";

    public int Status { get; set; } = StatusCodes.Status200OK;

    public Uri? RedirectTo { get; set; }

    public Uri Root { get; private set; } = null!;

    public IReadOnlyList<StubRequest> Requests => [.. _requests];

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
        if (path.EndsWith("/chat/completions", StringComparison.Ordinal) && body?["stream"]?.GetValue<bool>() == true)
        {
            await StreamCompletionAsync(context, model);
        }
        else if (path.EndsWith("/chat/completions", StringComparison.Ordinal))
        {
            await context.Response.WriteAsJsonAsync(
                new
                {
                    id = "chatcmpl-stub",
                    @object = "chat.completion",
                    created = 1_700_000_000,
                    model,
                    choices = new[] { new { index = 0, message = new { role = "assistant", content = Answer }, finish_reason = "stop" } },
                    usage = new { prompt_tokens = PromptTokens, completion_tokens = CompletionTokens, total_tokens = PromptTokens + CompletionTokens },
                },
                context.RequestAborted);
        }
        else if (path.EndsWith("/api/chat", StringComparison.Ordinal))
        {
            await context.Response.WriteAsJsonAsync(
                new
                {
                    model,
                    created_at = "2026-11-01T00:00:00Z",
                    message = new { role = "assistant", content = Answer },
                    done = true,
                    done_reason = "stop",
                    prompt_eval_count = PromptTokens,
                    eval_count = CompletionTokens,
                },
                context.RequestAborted);
        }
        else if (path.EndsWith("/v1/messages", StringComparison.Ordinal))
        {
            await context.Response.WriteAsJsonAsync(
                new
                {
                    id = "msg_stub",
                    type = "message",
                    role = "assistant",
                    model,
                    content = new[] { new { type = "text", text = Answer } },
                    stop_reason = "end_turn",
                    stop_sequence = (string?)null,
                    usage = new { input_tokens = PromptTokens, output_tokens = CompletionTokens },
                },
                context.RequestAborted);
        }
        else
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
        }
    }

    // The answer in two chunks, then the usage in a chunk of its own, as the API sends it when it is asked to include it.
    private async Task StreamCompletionAsync(HttpContext context, string model)
    {
        context.Response.ContentType = "text/event-stream";
        var half = Answer.Length / 2;
        object[] chunks =
        [
            new { id = "chatcmpl-stub", @object = "chat.completion.chunk", created = 1_700_000_000, model, choices = new[] { new { index = 0, delta = new { role = "assistant", content = Answer[..half] }, finish_reason = (string?)null } } },
            new { id = "chatcmpl-stub", @object = "chat.completion.chunk", created = 1_700_000_000, model, choices = new[] { new { index = 0, delta = new { content = Answer[half..] }, finish_reason = "stop" } } },
            new { id = "chatcmpl-stub", @object = "chat.completion.chunk", created = 1_700_000_000, model, choices = Array.Empty<object>(), usage = new { prompt_tokens = PromptTokens, completion_tokens = CompletionTokens, total_tokens = PromptTokens + CompletionTokens } },
        ];
        foreach (var chunk in chunks)
        {
            await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(chunk)}\n\n", context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
        }

        await context.Response.WriteAsync("data: [DONE]\n\n", context.RequestAborted);
    }
}
