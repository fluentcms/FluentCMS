using System.Collections.Frozen;
using System.Threading.Channels;

namespace FluentCMS.Core.Jobs;

// Wakes an idle worker of this instance when a job of its queue is due now, so that it claims at once instead of at its
// next poll. A queue holds at most one wake per worker, so a burst wakes every idle worker once and no more. Only this
// process is woken: the workers of other instances find the job when they poll.
internal sealed class JobWake(JobRunnerOptions options)
{
    private readonly FrozenDictionary<string, Channel<bool>> _queues = options.Queues
        .Where(queue => queue.Value > 0)
        .ToFrozenDictionary(
            queue => queue.Key,
            queue => Channel.CreateBounded<bool>(new BoundedChannelOptions(queue.Value) { FullMode = BoundedChannelFullMode.DropWrite }),
            StringComparer.Ordinal);

    public void Signal(string queue)
    {
        if (_queues.TryGetValue(queue, out var wake))
        {
            wake.Writer.TryWrite(true);
        }
    }

    // Until a wake of the queue or the end of the period; false when stopping was cancelled.
    public async Task<bool> WaitAsync(string queue, TimeSpan period, TimeProvider time, CancellationToken stopping)
    {
        if (!_queues.TryGetValue(queue, out var wake))
        {
            return await DelayAsync(period, time, stopping);
        }

        using var timeout = new CancellationTokenSource(period, time);
        using var either = CancellationTokenSource.CreateLinkedTokenSource(stopping, timeout.Token);
        try
        {
            await wake.Reader.ReadAsync(either.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return !stopping.IsCancellationRequested;
        }
    }

    private static async Task<bool> DelayAsync(TimeSpan period, TimeProvider time, CancellationToken stopping)
    {
        try
        {
            await Task.Delay(period, time, stopping);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
