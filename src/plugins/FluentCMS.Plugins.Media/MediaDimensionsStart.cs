using FluentCMS.Jobs;

namespace FluentCMS.Plugins.Media;

// The start of the check of the image sizes at one start of the host, which the media startup tries until it succeeds: it
// claims the check once, then enqueues the first run while the value is still at the version of that claim, so that an
// enqueue that failed is tried again rather than left to a claim that nothing runs.
internal sealed class MediaDimensionsStart(IJobScheduler scheduler, TimeProvider time)
{
    private bool _claimed;
    private long? _claim;

    // Answers whether it enqueued a run.
    public async Task<bool> RunAsync(IPluginStore<MediaPlugin> store, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);

        if (!_claimed)
        {
            _claim = await MediaDimensionsJob.ClaimAsync(store, time, cancellationToken);
            _claimed = true;
        }

        if (_claim is not { } claim)
        {
            return false;
        }

        var state = await store.GetAsync(MediaDimensionsJob.StateKey, cancellationToken);
        if (state is null || state.Value == MediaDimensionsJob.Done || state.Version != claim)
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
