using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluentCMS.IntegrationTests.Webhooks;

// An endpoint on this machine that records every request it gets and answers each with the next status it was given, then 200.
internal sealed class WebhookReceiver : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ConcurrentQueue<int> _answers = new();
    private readonly ConcurrentQueue<Received> _requests = new();

    private WebhookReceiver(WebApplication app)
    {
        _app = app;
    }

    public Uri Url { get; private set; } = default!;

    // The allow-list entry for this endpoint alone: its host and port.
    public string Host => $"{Url.Host}:{Url.Port}";

    public IReadOnlyCollection<Received> Requests => _requests;

    public static async Task<WebhookReceiver> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var app = builder.Build();
        var receiver = new WebhookReceiver(app);

        app.MapPost("/hooks", async (HttpContext context) =>
        {
            using var reader = new StreamReader(context.Request.Body);
            var body = await reader.ReadToEndAsync();
            receiver._requests.Enqueue(new Received(
                context.Request.Headers.ToDictionary(header => header.Key.ToLowerInvariant(), header => header.Value.ToString()),
                body));

            var status = receiver._answers.TryDequeue(out var next) ? next : StatusCodes.Status200OK;
            if (status is >= 300 and < 400)
            {
                context.Response.Headers.Location = "https://elsewhere.example/hooks";
            }

            context.Response.StatusCode = status;
        });

        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.GetRequiredFeature<IServerAddressesFeature>().Addresses.Single();
        receiver.Url = new Uri(new Uri(address), "/hooks");
        return receiver;
    }

    public void Answer(params int[] statuses)
    {
        foreach (var status in statuses)
        {
            _answers.Enqueue(status);
        }
    }

    public async Task<IReadOnlyList<Received>> WaitForAsync(int count)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (_requests.Count < count)
        {
            Assert.True(DateTime.UtcNow < deadline, $"The endpoint got {_requests.Count} of {count} requests in time.");
            await Task.Delay(50);
        }

        return [.. _requests];
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    internal sealed record Received(IReadOnlyDictionary<string, string> Headers, string Body);
}
