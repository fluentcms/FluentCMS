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

// Stores the schedule while the host starts, and keeps trying in the background when the database cannot be reached then,
// as the retention job of the runner does.
internal sealed class OidcPruneScheduleService(IJobScheduler scheduler, TimeProvider time, ILogger<OidcPruneScheduleService> logger) : BackgroundService
{
    private static readonly TimeSpan Retry = TimeSpan.FromSeconds(30);

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!await TryScheduleAsync(cancellationToken))
        {
            await base.StartAsync(cancellationToken);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Retry, time, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (await TryScheduleAsync(stoppingToken))
            {
                return;
            }
        }
    }

    private async Task<bool> TryScheduleAsync(CancellationToken cancellationToken)
    {
        try
        {
            await scheduler.AddRecurringAsync<OidcPruneJob, OidcPrunePayload>(OidcPruneJob.RecurringName, OidcPruneJob.Schedule, new OidcPrunePayload(), cancellationToken: cancellationToken);
            return true;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            OidcLog.PruneScheduleFailed(logger, exception);
            return false;
        }
    }
}

// Event ids in the identity range of the FCMS codes, after the codes themselves.
internal static partial class OidcLog
{
    public const int PruneScheduleFailedId = 3030;

    [LoggerMessage(EventId = PruneScheduleFailedId, EventName = "OidcPruneScheduleFailed", Level = LogLevel.Error, Message = "Could not register the recurring prune job of the authorization server; used tokens stay until it is registered.")]
    public static partial void PruneScheduleFailed(ILogger logger, Exception exception);
}
