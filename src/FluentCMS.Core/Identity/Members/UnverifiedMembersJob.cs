using FluentCMS.Core.Storage;
using FluentCMS.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Identity.Members;

internal sealed record UnverifiedMembersPayload;

// Removes the members that never verified their address: a registration nobody answered, or a member an administrator
// created who never chose a password. Such a member never signed in, so it has no session to end, and the job raises no
// event for it.
internal sealed class UnverifiedMembersJob(SystemDbContext context, MemberOptions options, TimeProvider time, ILogger<UnverifiedMembersJob> logger) : IJob<UnverifiedMembersPayload>
{
    public const string Type = "members.remove_unverified";

    public const string RecurringName = "members.remove_unverified";

    public const string Schedule = "45 3 * * *";

    // From when the job may remove a member, which is when a link sent to it stops working; null for a verified member.
    public static DateTimeOffset? RemovableAt(CmsMember member, MemberOptions options) =>
        member.EmailConfirmed ? null : member.CreatedAt + options.UnverifiedMemberLifetime;

    public async Task ExecuteAsync(UnverifiedMembersPayload payload, JobContext jobContext)
    {
        ArgumentNullException.ThrowIfNull(jobContext);

        var threshold = time.GetUtcNow() - options.UnverifiedMemberLifetime;
        var removed = await context.Set<CmsMember>()
            .Where(member => !member.EmailConfirmed && member.CreatedAt < threshold)
            .ExecuteDeleteAsync(jobContext.CancellationToken);
        if (removed > 0)
        {
            MemberLog.UnverifiedMembersRemoved(logger, removed, threshold);
        }
    }
}

// Stores the schedule a minute after the host starts and then keeps trying while the database cannot be reached, as the
// other daily jobs of core do.
internal sealed class UnverifiedMembersScheduleService(IJobScheduler scheduler, TimeProvider time, ILogger<UnverifiedMembersScheduleService> logger) : BackgroundService
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
            await scheduler.AddRecurringAsync<UnverifiedMembersJob, UnverifiedMembersPayload>(UnverifiedMembersJob.RecurringName, UnverifiedMembersJob.Schedule, new UnverifiedMembersPayload(), queue: null, cancellationToken: cancellationToken);
            return true;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            MemberLog.CleanupScheduleFailed(logger, exception);
            return false;
        }
    }
}
