using FluentCMS.Core.Models;

namespace FluentCMS.Host.Jobs;

// Schedules the retention purge as the host starts, after the database is migrated, so a new install purges without anyone asking
// (AUD-04). A database the host cannot reach yet only delays it to the next start; the host still serves.
internal sealed class RetentionStartup(IServiceScopeFactory scopes, ILogger<RetentionStartup> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IRetentionSchedule>().EnsureScheduledAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "The retention purge could not be scheduled; it is scheduled the next time the host starts.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
