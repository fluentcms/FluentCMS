using FluentCMS.Core.Events;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Health;

// Reads one row of a system table, which proves the connection, the credentials and that the system migrations ran.
internal sealed class DatabaseHealthCheck(IServiceScopeFactory scopes, ILogger<DatabaseHealthCheck> logger) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
            await db.Set<Site>().AsNoTracking().Select(site => site.Id).FirstOrDefaultAsync(cancellationToken);
            return HealthCheckResult.Healthy("The database answers.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            HealthLog.DatabaseUnreachable(logger, exception.GetType().Name, SecretScrubber.Of(exception.Message));
            return HealthCheckResult.Unhealthy("The database cannot be reached or is not migrated.");
        }
    }
}
