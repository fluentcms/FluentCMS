using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Events;
using FluentCMS.Core.Jobs;
using FluentCMS.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace FluentCMS.Plugins.Webhooks.Tests;

// A host whose job runner has no workers, so that a test runs the outbox and the jobs itself, and the clock moves only
// when the test moves it.
internal static class WebhookTesting
{
    public static CancellationToken Token => TestContext.Current.CancellationToken;

    public static FluentCmsTestHost Host(bool allowPrivateNetworks = true, LogCapture? logs = null) =>
        new FluentCmsTestHost()
            .WithContentTypes<Article, Page>()
            .WithServices(services =>
            {
                services.PostConfigure<WebhooksOptions>(options => options.AllowPrivateNetworks = allowPrivateNetworks);
                if (logs is not null)
                {
                    services.AddSingleton<ILoggerProvider>(logs);
                }
            })
            .Configure(cms =>
            {
                cms.Jobs.Options.Queues[JobRunnerOptions.DefaultQueue] = 0;
                cms.Jobs.Options.Queues[OutboxDrainJob.Queue] = 0;
                cms.Jobs.Options.Queues[WebhookDeliveryJob.Queue] = 0;
            });

    public static async Task<HttpClient> AdminAsync(FluentCmsTestHost host) =>
        host.AsKey(await host.CreateApiKeyAsync(ApiKeyClass.Management, BuiltInRoles.SuperAdminName, cancellationToken: Token));

    public static async Task<JsonNode> CreateAsync(HttpClient client, object webhook)
    {
        using var response = await client.PostAsJsonAsync(new Uri("/api/management/v1/webhooks", UriKind.Relative), webhook, Token);
        var text = await response.Content.ReadAsStringAsync(Token);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, text);
        return JsonNode.Parse(text)!;
    }

    public static Guid Id(JsonNode node, string name = "id") => Guid.Parse(node[name]!.GetValue<string>());

    // Rounds of the outbox and the due jobs until neither has anything left.
    public static async Task<int> PumpAsync(FluentCmsTestHost host)
    {
        var jobs = 0;
        for (var round = 0; round < 20; round++)
        {
            var delivered = await host.Services.GetRequiredService<OutboxDrainer>().DrainAsync(Token);
            var ran = await RunDueJobsAsync(host);
            jobs += ran;
            if (delivered == 0 && ran == 0)
            {
                break;
            }
        }

        return jobs;
    }

    public static async Task<int> RunDueJobsAsync(FluentCmsTestHost host)
    {
        var ran = 0;
        while (true)
        {
            var round = 0;
            foreach (var queue in (string[])[JobRunnerOptions.DefaultQueue, WebhookDeliveryJob.Queue])
            {
                round += await RunNextAsync(host, queue) ? 1 : 0;
            }

            if (round == 0)
            {
                return ran;
            }

            ran += round;
        }
    }

    // Runs the next due job of the queue, if there is one.
    public static async Task<bool> RunNextAsync(FluentCmsTestHost host, string queue)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<JobExecutor>().RunNextAsync(queue, "test", Token);
    }

    public static async Task DrainOutboxAsync(FluentCmsTestHost host)
    {
        while (await host.Services.GetRequiredService<OutboxDrainer>().DrainAsync(Token) > 0)
        {
        }
    }

    public static async Task<List<WebhookDelivery>> DeliveriesAsync(FluentCmsTestHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PluginDbContext<WebhooksPlugin>>().Set<WebhookDelivery>()
            .AsNoTracking()
            .OrderBy(delivery => delivery.CreatedAt).ThenBy(delivery => delivery.Attempt)
            .ToListAsync(Token);
    }

    public static async Task<string> TextAsync(HttpResponseMessage response) => await response.Content.ReadAsStringAsync(Token);

    public static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        JsonNode.Parse(await TextAsync(response))?["code"]?.GetValue<string>();
}

public sealed class LogCapture : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _lines = new();

    public IReadOnlyList<string> Lines => [.. _lines];

    public ILogger CreateLogger(string categoryName) => new Logger(this);

    public void Dispose()
    {
    }

    private sealed class Logger(LogCapture capture) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            capture._lines.Enqueue($"{eventId.Id} {formatter(state, exception)} {exception}");
    }
}
