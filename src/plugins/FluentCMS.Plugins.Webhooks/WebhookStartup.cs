using FluentCMS.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Plugins.Webhooks;

// Once the host has started, which is after the migrations: protects the headers that webhooks written before headers were
// protected hold in plain JSON, and stores the daily schedule of the retention job. Each step is tried again while the
// database is out of reach.
internal sealed class WebhookStartup(
    IServiceScopeFactory scopes,
    IJobScheduler scheduler,
    IHostApplicationLifetime lifetime,
    TimeProvider time,
    ILogger<WebhookStartup> logger) : BackgroundService
{
    private static readonly TimeSpan Retry = TimeSpan.FromSeconds(30);

    // The update names the plain value it replaces, so that a write in between, or another instance doing the same, wins.
    public static async Task<int> ProtectHeadersAsync(PluginDbContext<WebhooksPlugin> database, WebhookSigning signing, CancellationToken cancellationToken)
    {
        var plain = await database.Set<Webhook>().AsNoTracking()
            .Where(webhook => webhook.Headers.StartsWith('{'))
            .Select(webhook => new { webhook.Id, webhook.Headers })
            .ToListAsync(cancellationToken);

        var count = 0;
        foreach (var row in plain)
        {
            var headers = signing.ProtectHeaders(WebhookJson.Map(row.Headers));
            count += await database.Set<Webhook>()
                .Where(webhook => webhook.Id == row.Id && webhook.Headers == row.Headers)
                .ExecuteUpdateAsync(item => item.SetProperty(webhook => webhook.Headers, headers), cancellationToken);
        }

        return count;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using (lifetime.ApplicationStarted.Register(() => started.TrySetResult()))
        using (stoppingToken.Register(() => started.TrySetCanceled(stoppingToken)))
        {
            try
            {
                await started.Task;
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        var headersProtected = false;
        var scheduled = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!headersProtected)
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var services = scope.ServiceProvider;
                    var count = await ProtectHeadersAsync(services.GetRequiredService<PluginDbContext<WebhooksPlugin>>(), services.GetRequiredService<WebhookSigning>(), stoppingToken);
                    headersProtected = true;
                    if (count > 0)
                    {
                        WebhookLog.HeadersProtected(logger, count);
                    }
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    WebhookLog.HeadersNotProtected(logger, exception);
                }
            }

            if (!scheduled)
            {
                try
                {
                    await scheduler.AddRecurringAsync<WebhookRetentionJob, WebhookRetentionPayload>(WebhookRetentionJob.Type, WebhookRetentionJob.Schedule, new WebhookRetentionPayload(), queue: null, cancellationToken: stoppingToken);
                    scheduled = true;
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    WebhookLog.RetentionNotScheduled(logger, exception);
                }
            }

            if (headersProtected && scheduled)
            {
                return;
            }

            try
            {
                await Task.Delay(Retry, time, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
