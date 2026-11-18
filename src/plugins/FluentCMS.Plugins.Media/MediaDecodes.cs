using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace FluentCMS.Plugins.Media;

// What bounds the memory that making variants takes on an instance: at most MaxConcurrentDecodes run at once, and requests
// for one variant that come while it is made wait for that one run. A caller that gives up stops waiting. A run still
// queued for a slot when its last caller has left is dropped, and a later request starts a new one; a run that holds a slot
// goes on and stores the variant, and only the host stopping cancels it. A run that fails fails every caller waiting on it
// and is forgotten first, so the next request tries again.
internal sealed class MediaDecodes(IOptions<MediaOptions> options, IHostApplicationLifetime lifetime) : IDisposable
{
    private readonly SemaphoreSlim _slots = new(options.Value.MaxConcurrentDecodes);
    private readonly ConcurrentDictionary<string, Run> _running = new(StringComparer.Ordinal);
    private int _joined;

    // How many calls waited for a run another call started, and how many slots are free, for the tests.
    internal int Joined => Volatile.Read(ref _joined);

    internal int FreeSlots => _slots.CurrentCount;

    public async Task RunAsync(string key, Func<CancellationToken, Task> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);

        Run run;
        while (true)
        {
            var mine = new Run();
            run = _running.GetOrAdd(key, mine);
            lock (run)
            {
                // A run closed between the lookup and the lock is out of the map already; the next lookup starts another.
                if (run.Closed)
                {
                    continue;
                }

                run.Waiters++;
            }

            if (run == mine)
            {
                _ = Task.Run(() => LeadAsync(key, work, run), CancellationToken.None);
            }
            else
            {
                Interlocked.Increment(ref _joined);
            }

            break;
        }

        try
        {
            await run.Done.Task.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Leave(key, run);
            throw;
        }
    }

    public void Dispose() => _slots.Dispose();

    private void Leave(string key, Run run)
    {
        lock (run)
        {
            run.Waiters--;
            if (run.Waiters > 0 || run.HoldsSlot || run.Closed)
            {
                return;
            }

            // Out of the map before it is cancelled, so no caller joins a cancelled run; under the lock, so the run cannot
            // end and dispose the source first.
            run.Closed = true;
            _running.TryRemove(new KeyValuePair<string, Run>(key, run));
            run.Abandoned.Cancel();
        }
    }

    private async Task LeadAsync(string key, Func<CancellationToken, Task> work, Run run)
    {
        var stopping = lifetime.ApplicationStopping;
        Exception? failure = null;
        try
        {
            using (var queued = CancellationTokenSource.CreateLinkedTokenSource(stopping, run.Abandoned.Token))
            {
                await _slots.WaitAsync(queued.Token);
            }

            bool abandoned;
            lock (run)
            {
                abandoned = run.Closed;
                run.HoldsSlot = !abandoned;
            }

            try
            {
                if (abandoned)
                {
                    // The slot came as the last caller left.
                    throw new OperationCanceledException(run.Abandoned.Token);
                }

                await work(stopping);
            }
            finally
            {
                _slots.Release();
            }
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        lock (run)
        {
            run.Closed = true;
            _running.TryRemove(new KeyValuePair<string, Run>(key, run));
        }

        run.Abandoned.Dispose();
        if (failure is null)
        {
            run.Done.SetResult();
        }
        else
        {
            run.Done.SetException(failure);

            // Observed here, since every caller may have stopped waiting.
            _ = run.Done.Task.Exception;
        }
    }

    // Waiters, HoldsSlot and Closed change under the lock of the run. Closed, it is out of the map and takes no caller.
    private sealed class Run
    {
        public TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationTokenSource Abandoned { get; } = new();

        public int Waiters { get; set; }

        public bool HoldsSlot { get; set; }

        public bool Closed { get; set; }
    }
}
