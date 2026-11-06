namespace FluentCMS.Providers;

/// <summary>
/// A named lock that one holder at a time has across every instance of the host, for work that must not run twice at
/// once. Core keeps it in the database with a lease that the holder renews while it holds the lock, so a holder that
/// stops without releasing it blocks the others only until its lease runs out. A host or a plugin can replace it.
/// </summary>
public interface IDistributedLock
{
    /// <summary>Takes the lock of a name, waiting for it up to a timeout.</summary>
    /// <param name="name">The name of the lock, at most 128 characters. Names are a fixed set, such as one per task, not one per item.</param>
    /// <param name="timeout">
    /// How long to wait while another holder has the lock: <see cref="TimeSpan.Zero"/> to try once, or
    /// <see cref="Timeout.InfiniteTimeSpan"/> to wait until it is free.
    /// </param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The held lock, which disposing releases, or <see langword="null"/> when the timeout passed first.</returns>
    Task<IDistributedLockHandle?> TryAcquireAsync(string name, TimeSpan timeout, CancellationToken cancellationToken = default);
}

/// <summary>A lock taken through <see cref="IDistributedLock"/>. Disposing it releases the lock.</summary>
public interface IDistributedLockHandle : IAsyncDisposable
{
    /// <summary>The name of the lock.</summary>
    string Name { get; }

    /// <summary>
    /// Canceled when the lock is lost while held, because another holder took it after the lease could not be renewed.
    /// Work that the lock guards stops when it is canceled.
    /// </summary>
    CancellationToken Lost { get; }
}
