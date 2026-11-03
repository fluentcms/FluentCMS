using FluentCMS.Events;

namespace FluentCMS.Activity;

/// <summary>
/// Writes rows of the activity log. The log is written for the events the CMS raises without anyone calling this, so
/// that a feature cannot forget to; call it for what no event covers. It writes at once on a connection of its own,
/// so call it after a write has committed, and not inside a transaction.
/// </summary>
public interface IActivityLog
{
    /// <summary>Writes a row for the current operation, or for the system when none is running.</summary>
    /// <param name="entry">What happened.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the row is stored.</returns>
    /// <exception cref="ArgumentException">The action is empty or longer than 64 characters.</exception>
    ValueTask WriteAsync(ActivityEntry entry, CancellationToken cancellationToken);

    /// <summary>Writes a row for the operation an <see cref="EventSource"/> describes.</summary>
    /// <param name="entry">What happened.</param>
    /// <param name="source">Who did it, where, when and from which origin.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the row is stored.</returns>
    /// <exception cref="ArgumentException">The action is empty or longer than 64 characters.</exception>
    ValueTask WriteAsync(ActivityEntry entry, EventSource source, CancellationToken cancellationToken);
}
