using FluentCMS.Content;

namespace FluentCMS.Activity;

/// <summary>
/// Reads the activity log of the site of the current operation, the newest row first. It checks no permission, so
/// <c>activity.read</c> is enforced by the caller.
/// </summary>
public interface IActivityQuery
{
    /// <summary>Reads a page of the log.</summary>
    /// <param name="filter">Which rows to read.</param>
    /// <param name="page">Which page; by default the first, of 50 rows. The size is capped at 100, and a cursor is not supported.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The page.</returns>
    /// <exception cref="InvalidOperationException">No operation is running, so there is no site to read.</exception>
    /// <exception cref="ArgumentException">The page is not valid.</exception>
    Task<PagedResult<ActivityItem>> QueryAsync(ActivityFilter? filter = null, PageRequest? page = null, CancellationToken cancellationToken = default);
}
