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
// to RedirectTo. Hold makes it keep every request open until Release, as a receiver that hangs does.
public sealed class TestReceiver : IAsyncDisposable
{
    private readonly ConcurrentQueue<ReceivedRequest> _requests = new();
    private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SemaphoreSlim _arrived = new(0);
    private TaskCompletionSource? _held;
    private WebApplication? _app;
    private int _open;
    private int _mostOpen;

    public int Status { get; set; } = StatusCodes.Status200OK;

    public string? RedirectTo { get; set; }

    public Uri Root { get; private set; } = null!;

    public IReadOnlyList<ReceivedRequest> Requests => [.. _requests];

    // The most requests that were open at once.
    public int MostOpen => Volatile.Read(ref _mostOpen);

    // Completes when the first request has arrived.
    public Task Entered => _entered.Task;

    public Uri Url(string path = "/hook") => new(Root, path);

    // Completes when this many requests have arrived since the last call.
    public async Task ArrivedAsync(int requests)
    {
        for (var count = 0; count < requests; count++)
        {
            await _arrived.WaitAsync();
        }
    }

    public void Hold() => _held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Release() => _held?.TrySetResult();

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
        Release();
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }

        _arrived.Dispose();
    }

    private async Task HandleAsync(HttpContext context)
    {
        var open = Interlocked.Increment(ref _open);
        try
        {
            for (var most = Volatile.Read(ref _mostOpen); open > most; most = Volatile.Read(ref _mostOpen))
            {
                Interlocked.CompareExchange(ref _mostOpen, open, most);
            }

            using var reader = new StreamReader(context.Request.Body);
            var body = await reader.ReadToEndAsync(context.RequestAborted);
            var headers = context.Request.Headers.ToDictionary(header => header.Key, header => header.Value.ToString(), StringComparer.OrdinalIgnoreCase);
            _requests.Enqueue(new ReceivedRequest(context.Request.Method, context.Request.Path, headers, body));
            _entered.TrySetResult();
            _arrived.Release();

            if (_held is { } held)
            {
                await held.Task.WaitAsync(context.RequestAborted);
            }

            if (RedirectTo is { } location)
            {
                context.Response.StatusCode = StatusCodes.Status302Found;
                context.Response.Headers.Location = location;
                return;
            }

            context.Response.StatusCode = Status;
            await context.Response.WriteAsync(Status < 300 ? "ok" : "receiver failed", context.RequestAborted);
        }
        finally
        {
            Interlocked.Decrement(ref _open);
        }
    }
}
