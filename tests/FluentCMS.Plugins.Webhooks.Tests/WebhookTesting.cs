using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Events;
using FluentCMS.Core.Jobs;
using FluentCMS.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace FluentCMS.Plugins.Webhooks.Tests;

// A host whose job runner has no workers, so that a test runs the outbox and the jobs itself, and the clock moves only
// when the test moves it. Its maintenance runs at the start and then once a day of that clock, so a move of a day or more
// can make recurring jobs due behind a test's back; RunDueJobsAsync runs them without counting them.
internal static class WebhookTesting
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

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
                cms.Jobs.Options.MaintenanceInterval = TimeSpan.FromDays(1);
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

    // Rounds of the outbox and the due jobs until neither has anything left. Counts as RunDueJobsAsync does.
    public static async Task<int> PumpAsync(FluentCmsTestHost host, params string[] types)
    {
        var jobs = 0;
        for (var round = 0; round < 20; round++)
        {
            var delivered = await host.Services.GetRequiredService<OutboxDrainer>().DrainAsync(Token);
            var (ran, counted) = await RunDueAsync(host, types);
            jobs += counted;
            if (delivered == 0 && ran == 0)
            {
                break;
            }
        }

        return jobs;
    }

    // Runs every due job and counts the deliveries among them, or the jobs of the types given. The host's maintenance fires
    // the recurring jobs a moved clock made due in the background, at a moment the test does not know, so they run here but
    // count only when the test names their type (#3409).
    public static async Task<int> RunDueJobsAsync(FluentCmsTestHost host, params string[] types) => (await RunDueAsync(host, types)).Counted;

    private static async Task<(int Ran, int Counted)> RunDueAsync(FluentCmsTestHost host, string[] types)
    {
        string[] counting = types.Length > 0 ? types : [WebhookDeliveryJob.Type];
        var (ran, counted) = (0, 0);
        while (true)
        {
            var round = 0;
            foreach (var queue in (string[])[JobRunnerOptions.DefaultQueue, WebhookDeliveryJob.Queue])
            {
                if (await RunNextOfAsync(host, queue) is { } type)
                {
                    round++;
                    counted += counting.Contains(type) ? 1 : 0;
                }
            }

            if (round == 0)
            {
                return (ran, counted);
            }

            ran += round;
        }
    }

    // Runs the next due job of the queue, if there is one, and gives its type.
    private static async Task<string?> RunNextOfAsync(FluentCmsTestHost host, string queue)
    {
        if (await host.Services.GetRequiredService<JobStore>().ClaimAsync(queue, "test", Token) is not { } job)
        {
            return null;
        }

        await host.Services.GetRequiredService<JobExecutor>().RunAsync(job);
        return job.Type;
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

    // The attempts to the webhook, or to every webhook of a host the test has to itself.
    public static async Task<List<WebhookDelivery>> DeliveriesAsync(FluentCmsTestHost host, Guid? webhook = null)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PluginDbContext<WebhooksPlugin>>().Set<WebhookDelivery>()
            .AsNoTracking()
            .Where(delivery => webhook == null || delivery.WebhookId == webhook)
            .OrderBy(delivery => delivery.CreatedAt).ThenBy(delivery => delivery.Attempt)
            .ToListAsync(Token);
    }

    // Completes when the steps the plugin takes once the host has started are done.
    public static async Task StartedAsync(FluentCmsTestHost host) =>
        await host.Services.GetServices<IHostedService>().OfType<WebhookStartup>().Single().ExecuteTask!.WaitAsync(Patience, Token);

    // What a receiver does, as the plugin's README has it: refuse a timestamp more than five minutes from its own clock,
    // recompute the HMAC over t + "." + body, and accept the body when any v1 matches, compared in constant time.
    public static bool Verifies(string signature, string secret, string body, DateTimeOffset now)
    {
        var parts = signature.Split(',').Select(part => part.Split('=', 2)).Where(part => part.Length == 2).ToList();
        if (parts.FirstOrDefault(part => part[0] == "t")?[1] is not { } t
            || !long.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            || Math.Abs(now.ToUnixTimeSeconds() - seconds) > 300)
        {
            return false;
        }

        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(t + "." + body));
        var expected = Encoding.ASCII.GetBytes(Convert.ToHexStringLower(mac));
        return parts.Where(part => part[0] == "v1").Any(part => CryptographicOperations.FixedTimeEquals(expected, Encoding.ASCII.GetBytes(part[1])));
    }

    public static int Signatures(string header) => header.Split(',').Count(part => part.StartsWith("v1=", StringComparison.Ordinal));

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
