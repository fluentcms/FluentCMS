#:sdk Microsoft.NET.Sdk.Web
#:property PublishAot=false

// A stand-in for an OpenAI-compatible endpoint, for a dry run of the walk-through in docs/ai-assist-verification.md:
// dotnet run eng/ai-stand-in.cs -- [port], 18434 by default, on 127.0.0.1. It answers in the chat completions format with
// 21 prompt and 6 completion tokens, the usage of a stream in a chunk of its own, and prints the shape of each request
// without its text. A model whose name holds "slow" waits 30 s before a plain answer, and a stream after its first chunk.
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

var port = args.Length > 0 ? int.Parse(args[0], CultureInfo.InvariantCulture) : 18434;
var builder = WebApplication.CreateSlimBuilder();
builder.Logging.ClearProviders();
builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
var app = builder.Build();
app.MapPost("/{**path}", AnswerAsync);
Console.WriteLine($"Listening on http://127.0.0.1:{port}/v1");
await app.RunAsync();

static async Task AnswerAsync(HttpContext context)
{
    var body = await JsonNode.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted) ?? new JsonObject();
    Console.WriteLine($"{context.Request.Path} authorization={(context.Request.Headers.Authorization.Count > 0 ? "present" : "absent")} {Shape(body)}");
    if (!(context.Request.Path.Value ?? "").EndsWith("/chat/completions", StringComparison.Ordinal))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    var model = body["model"]?.GetValue<string>() ?? "";
    var system = Text(body["messages"]?[0]?["content"]);
    var answer = system.Contains("metaTitle", StringComparison.Ordinal)
        ? """{"metaTitle": "Ships at sea", "metaDescription": "Where ships sail."}"""
        : "A suggestion of the stand-in.";
    var usage = new { prompt_tokens = 21, completion_tokens = 6, total_tokens = 27 };
    var slow = model.Contains("slow", StringComparison.Ordinal);
    try
    {
        if (body["stream"]?.GetValue<bool>() != true)
        {
            await PauseAsync(slow, context);
            await context.Response.WriteAsJsonAsync(new { id = "stand-in", @object = "chat.completion", created = 1, model, choices = new[] { new { index = 0, message = new { role = "assistant", content = answer }, finish_reason = "stop" } }, usage }, context.RequestAborted);
            return;
        }

        context.Response.ContentType = "text/event-stream";
        var half = answer.Length / 2;
        await ChunkAsync(context, new { id = "stand-in", @object = "chat.completion.chunk", created = 1, model, choices = new[] { new { index = 0, delta = new { role = "assistant", content = answer[..half] }, finish_reason = (string?)null } } });
        await PauseAsync(slow, context);
        await ChunkAsync(context, new { id = "stand-in", @object = "chat.completion.chunk", created = 1, model, choices = new[] { new { index = 0, delta = new { content = answer[half..] }, finish_reason = "stop" } } });
        await ChunkAsync(context, new { id = "stand-in", @object = "chat.completion.chunk", created = 1, model, choices = Array.Empty<object>(), usage });
        await context.Response.WriteAsync("data: [DONE]\n\n", context.RequestAborted);
    }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
    {
        Console.WriteLine("  the caller went away");
    }
}

static async Task ChunkAsync(HttpContext context, object chunk)
{
    await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(chunk)}\n\n", context.RequestAborted);
    await context.Response.Body.FlushAsync(context.RequestAborted);
}

static Task PauseAsync(bool slow, HttpContext context) => slow ? Task.Delay(TimeSpan.FromSeconds(30), context.RequestAborted) : Task.CompletedTask;

// The parameters of a request, and each message as its role and parts: a text by its length, an image by its type and size.
static string Shape(JsonNode body)
{
    var messages = body["messages"]?.AsArray().Select(message => $"{message?["role"]}:{Parts(message?["content"])}") ?? [];
    return $"model={body["model"]} stream={body["stream"]?.ToJsonString() ?? "false"} stream_options={body["stream_options"]?.ToJsonString() ?? "none"} max_completion_tokens={body["max_completion_tokens"]} max_tokens={body["max_tokens"]} messages=[{string.Join(", ", messages)}]";
}

static string Parts(JsonNode? content) => content is JsonArray parts
    ? string.Join("+", parts.Select(part => part?["type"]?.GetValue<string>() == "image_url" ? Image(part["image_url"]?["url"]?.GetValue<string>() ?? "") : $"text({Text(part).Length})"))
    : $"text({Text(content).Length})";

static string Image(string url) => $"image_url({url[..Math.Max(0, url.IndexOf(';', StringComparison.Ordinal))]}, {url.Length} chars)";

static string Text(JsonNode? content) => content switch
{
    JsonValue value => value.GetValue<string>(),
    JsonArray parts => string.Concat(parts.Select(part => part?["text"]?.GetValue<string>())),
    JsonObject part => part["text"]?.GetValue<string>() ?? "",
    _ => "",
};
