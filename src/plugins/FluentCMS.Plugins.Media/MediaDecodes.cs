using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace FluentCMS.Plugins.Media;

// What bounds the memory that making variants takes on an instance: at most MaxConcurrentDecodes run at once, and requests
// for one variant that come while it is made wait for that one run. A caller that gives up stops waiting, and the run goes
// on for the others and stores the variant; only the host stopping cancels it. A run that fails fails every caller waiting
// on it and is forgotten first, so the next request tries again.
internal sealed class MediaDecodes(IOptions<MediaOptions> options, IHostApplicationLifetime lifetime) : IDisposable
{
    private readonly SemaphoreSlim _slots = new(options.Value.MaxConcurrentDecodes);
    private readonly ConcurrentDictionary<string, Task> _running = new(StringComparer.Ordinal);
    private int _joined;

    // How many calls waited for a run another call started, for the tests.
    internal int Joined => Volatile.Read(ref _joined);

    public Task RunAsync(string key, Func<CancellationToken, Task> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shared = _running.GetOrAdd(key, started.Task);
        if (shared == started.Task)
        {
            _ = Task.Run(() => LeadAsync(key, work, started), CancellationToken.None);
        }
        else
        {
            Interlocked.Increment(ref _joined);
        }

        return shared.WaitAsync(cancellationToken);
    }

    public void Dispose() => _slots.Dispose();

    private async Task LeadAsync(string key, Func<CancellationToken, Task> work, TaskCompletionSource started)
    {
        Exception? failure = null;
        try
        {
            var stopping = lifetime.ApplicationStopping;
            await _slots.WaitAsync(stopping);
            try
            {
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

        _running.TryRemove(new KeyValuePair<string, Task>(key, started.Task));
        if (failure is null)
        {
            started.SetResult();
        }
        else
        {
            started.SetException(failure);

            // Observed here, since every caller may have stopped waiting.
            _ = started.Task.Exception;
        }
    }
}
