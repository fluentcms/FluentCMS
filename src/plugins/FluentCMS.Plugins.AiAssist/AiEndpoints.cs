using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace FluentCMS.Plugins.AiAssist;

// /api/management/v1/ai. The endpoints carry the request; AiAssistService and AiUsageService check and decide.
internal static class AiEndpoints
{
    public const string EventStream = "text/event-stream";

    public static RouteGroupBuilder MapAiEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/actions/{action}", ActAsync);

        group.MapGet("/usage", async (string? principalId, string? action, string? from, string? to, string? page, string? pageSize, AiUsageService usage, CancellationToken cancellationToken) =>
            Results.Ok(await usage.QueryAsync(new AiUsageQuery(principalId, action, from, to, page, pageSize), cancellationToken)));

        return group;
    }

    // A refusal before the provider is asked is a problem, whatever the request accepts. From then on a request that
    // accepts text/event-stream is answered with events: delta for each piece of text, then done with the body of the
    // plain answer, or error with the problem.
    private static async Task<IResult> ActAsync(
        string action,
        AiActionRequest request,
        HttpContext http,
        AiAssistService ai,
        IProblemDescriber problems,
        IOptions<JsonOptions> json,
        ILoggerFactory loggers,
        CancellationToken cancellationToken)
    {
        var call = await ai.PrepareAsync(action, request, cancellationToken);
        if (!http.Request.GetTypedHeaders().Accept.Any(accepted => accepted.MediaType.Equals(EventStream, StringComparison.OrdinalIgnoreCase)))
        {
            return Results.Ok(await ai.RunAsync(call, null, cancellationToken));
        }

        var serializer = json.Value.SerializerOptions;
        http.Response.ContentType = EventStream;
        http.Response.Headers.CacheControl = "no-store";

        // Tells a proxy such as nginx to pass each event on as it is written.
        http.Response.Headers["X-Accel-Buffering"] = "no";
        try
        {
            var result = await ai.RunAsync(call, (text, token) => WriteAsync(http.Response, "delta", new AiDelta(text), serializer, token), cancellationToken);
            await WriteAsync(http.Response, "done", result, serializer, cancellationToken);
        }
        catch (Exception exception)
        {
            // A caller that went away reads no event.
            if (cancellationToken.IsCancellationRequested)
            {
                return Results.Empty;
            }

            // Core's handler never sees a failure that is answered as an event, so one that is not a refusal is logged
            // here, by its type alone.
            var problem = problems.Describe(exception);
            if (exception is not PluginException)
            {
                AiLog.StreamFailed(loggers.CreateLogger(typeof(AiEndpoints)), action, exception.GetType().Name, problem.Code);
            }

            await WriteAsync(http.Response, "error", new AiStreamProblem($"urn:fluentcms:error:{problem.Code}", problem.Title, problem.Status, problem.Detail, problem.Code), serializer, cancellationToken);
        }

        return Results.Empty;
    }

    // JSON has no line break outside its strings, where it is escaped, so an event is one data line.
    private static async ValueTask WriteAsync<T>(HttpResponse response, string name, T data, JsonSerializerOptions serializer, CancellationToken cancellationToken)
    {
        await response.WriteAsync($"event: {name}\ndata: {JsonSerializer.Serialize(data, serializer)}\n\n", cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }

    private sealed record AiDelta(string Text);

    private sealed record AiStreamProblem(string Type, string Title, int Status, string Detail, string Code);
}
