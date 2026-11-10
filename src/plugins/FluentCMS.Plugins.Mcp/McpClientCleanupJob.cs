using FluentCMS.Agents;
using FluentCMS.Jobs;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Plugins.Mcp;

internal sealed record McpClientCleanupPayload;

// Removes the clients that registered themselves and were not approved within PendingLifetime, the rejected ones
// included, so that a registration nobody answers does not hold a place of the pending cap for good.
internal sealed class McpClientCleanupJob(IMcpClients clients, TimeProvider time, ILogger<McpClientCleanupJob> logger) : IJob<McpClientCleanupPayload>
{
    public const string Type = "mcp.client-cleanup";
    public const string Schedule = "45 4 * * *";

    public static readonly TimeSpan PendingLifetime = TimeSpan.FromDays(7);

    public async Task ExecuteAsync(McpClientCleanupPayload payload, JobContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var removed = await clients.RemoveUnapprovedAsync(time.GetUtcNow() - PendingLifetime, context.CancellationToken);
        if (removed > 0)
        {
            McpLog.UnapprovedClientsRemoved(logger, removed, PendingLifetime.TotalDays);
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
