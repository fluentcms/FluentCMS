using FluentCMS.Jobs;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Plugins.Media;

// The start of the check of the image sizes at one start of the host, which the media startup tries until it succeeds: it
// claims the check once, then enqueues the first run while the row still holds that claim, so that an enqueue that failed
// is tried again rather than left to a claim that nothing runs.
internal sealed class MediaDimensionsStart(IJobScheduler scheduler, TimeProvider time)
{
    private bool _claimed;
    private DateTimeOffset? _claim;

    // Answers whether it enqueued a run.
    public async Task<bool> RunAsync(PluginDbContext<MediaPlugin> database, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);

        if (!_claimed)
        {
            _claim = await MediaDimensionsJob.ClaimAsync(database, time, cancellationToken);
            _claimed = true;
        }

        if (_claim is not { } claim)
        {
            return false;
        }

        var state = await database.Set<SigningKey>().AsNoTracking().FirstOrDefaultAsync(row => row.Id == MediaDimensionsJob.StateId, cancellationToken);
        if (state is null || state.Key == MediaDimensionsJob.Done || state.CreatedAt != claim)
        {
            // Another start took the claim over, or the check was reset.
            _claim = null;
            return false;
        }

        await scheduler.EnqueueAsync<MediaDimensionsJob, MediaDimensionsPayload>(new MediaDimensionsPayload(), cancellationToken: cancellationToken);
        _claim = null;
        return true;
    }
}
