using FluentCMS.Jobs;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;

namespace FluentCMS.Core.Identity.Oidc;

internal sealed record OidcPrunePayload;

// Removes the tokens and authorizations that can no longer be used: expired, redeemed or revoked tokens, and authorizations
// that are revoked or were made for one sign-in and have no valid token left. OpenIddict prunes only rows created before the
// threshold, so an ended session stays a while for whoever looks into it.
internal sealed class OidcPruneJob(IOpenIddictTokenManager tokens, IOpenIddictAuthorizationManager authorizations, TimeProvider time) : IJob<OidcPrunePayload>
{
    public const string Type = "oidc.prune";

    public const string RecurringName = "oidc.prune";

    public const string Schedule = "15 4 * * *";

    public static readonly TimeSpan MinimumAge = TimeSpan.FromDays(14);

    public async Task ExecuteAsync(OidcPrunePayload payload, JobContext jobContext)
    {
        ArgumentNullException.ThrowIfNull(jobContext);

        var threshold = time.GetUtcNow() - MinimumAge;
        await tokens.PruneAsync(threshold, jobContext.CancellationToken);
        await authorizations.PruneAsync(threshold, jobContext.CancellationToken);
    }
}

// Stores the schedule a minute after the host starts and then keeps trying while the database cannot be reached. A daily
// prune need not hold up the start, and a write made while the host starts competes with its own writes for the database.
internal sealed class OidcPruneScheduleService(IJobScheduler scheduler, TimeProvider time, ILogger<OidcPruneScheduleService> logger) : BackgroundService
{
    private static readonly TimeSpan FirstAttempt = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Retry = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var wait = FirstAttempt;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(wait, time, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (await TryScheduleAsync(stoppingToken))
            {
                return;
            }

            wait = Retry;
        }
    }

    private async Task<bool> TryScheduleAsync(CancellationToken cancellationToken)
    {
        try
        {
            await scheduler.AddRecurringAsync<OidcPruneJob, OidcPrunePayload>(OidcPruneJob.RecurringName, OidcPruneJob.Schedule, new OidcPrunePayload(), queue: null, cancellationToken: cancellationToken);
            return true;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            OidcLog.PruneScheduleFailed(logger, exception);
            return false;
        }
    }
}
