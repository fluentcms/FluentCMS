using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Plugins.Webhooks.Tests;

public sealed record ReceivedRequest(string Method, string Path, IReadOnlyDictionary<string, string> Headers, string Body);

// A local receiver on a loopback port of its own, which records every request and answers with Status, or with a redirect
// to RedirectTo.
public sealed class TestReceiver : IAsyncDisposable
{
    private readonly ConcurrentQueue<ReceivedRequest> _requests = new();
    private WebApplication? _app;

    public int Status { get; set; } = StatusCodes.Status200OK;

    public string? RedirectTo { get; set; }

    public Uri Root { get; private set; } = null!;

    public IReadOnlyList<ReceivedRequest> Requests => [.. _requests];

    public Uri Url(string path = "/hook") => new(Root, path);

    public static async Task<TestReceiver> StartAsync()
    {
        var receiver = new TestReceiver();
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        app.Run(receiver.HandleAsync);
        await app.StartAsync();

        receiver._app = app;
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        receiver.Root = new Uri(address.TrimEnd('/') + "/");
        return receiver;
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
        var body = await reader.ReadToEndAsync(context.RequestAborted);
        var headers = context.Request.Headers.ToDictionary(header => header.Key, header => header.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        _requests.Enqueue(new ReceivedRequest(context.Request.Method, context.Request.Path, headers, body));

        if (RedirectTo is { } location)
        {
            context.Response.StatusCode = StatusCodes.Status302Found;
            context.Response.Headers.Location = location;
            return;
        }

        context.Response.StatusCode = Status;
        await context.Response.WriteAsync(Status < 300 ? "ok" : "receiver failed", context.RequestAborted);
    }
}
