using FluentCMS.Agents;
using FluentCMS.Jobs;
using FluentCMS.Settings;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Plugins.Mcp;

internal sealed record McpClientCleanupPayload;

// Removes the clients that registered themselves and were not approved within PendingLifetime, the rejected ones
// included, so that a registration nobody answers does not hold a place of the pending cap for good; and the clients the
// open policy approved that no user consented to within NeverUsedClientHours, or that have no token or authorization left
// once UnusedClientDays have passed, so that they do not fill MaxClients. It runs every hour, so that the short period
// holds to within an hour.
internal sealed class McpClientCleanupJob(IMcpClients clients, ISettings<McpSettings> settings, TimeProvider time, ILogger<McpClientCleanupJob> logger) : IJob<McpClientCleanupPayload>
{
    public const string Type = "mcp.client-cleanup";
    public const string Schedule = "45 * * * *";

    public static readonly TimeSpan PendingLifetime = TimeSpan.FromDays(7);

    // Far past a run, which reads at most MaxClients clients, so that only a run that hangs is cut off and frees its worker.
    public static readonly TimeSpan AttemptTimeout = TimeSpan.FromMinutes(10);

    public async Task ExecuteAsync(McpClientCleanupPayload payload, JobContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var now = time.GetUtcNow();
        var removed = await clients.RemoveUnapprovedAsync(now - PendingLifetime, context.CancellationToken);
        if (removed > 0)
        {
            McpLog.UnapprovedClientsRemoved(logger, removed, PendingLifetime.TotalDays);
        }

        var current = await settings.GetDefaultAsync(context.CancellationToken);
        var unused = await clients.RemoveUnusedAsync(now - TimeSpan.FromHours(current.NeverUsedClientHours), now - TimeSpan.FromDays(current.UnusedClientDays), context.CancellationToken);
        if (unused > 0)
        {
            McpLog.UnusedClientsRemoved(logger, unused, current.NeverUsedClientHours, current.UnusedClientDays);
        }
    }
}

// Stores the daily schedule of the cleanup once the host has started, which is after the migrations, and keeps trying
// while the database is out of reach.
internal sealed class McpClientCleanupSchedule(IJobScheduler scheduler, IHostApplicationLifetime lifetime, TimeProvider time, ILogger<McpClientCleanupSchedule> logger) : BackgroundService
{
    private static readonly TimeSpan Retry = TimeSpan.FromSeconds(30);

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

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await scheduler.AddRecurringAsync<McpClientCleanupJob, McpClientCleanupPayload>(McpClientCleanupJob.Type, McpClientCleanupJob.Schedule, new McpClientCleanupPayload(), queue: null, cancellationToken: stoppingToken);
                return;
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                McpLog.ClientCleanupNotScheduled(logger, exception);
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
