using FluentCMS.Activity;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;

namespace FluentCMS.Core.ActivityLog;

// The activity log for the Management API: activity.read in the site of the operation reads that site's rows, and only
// activity.read for the instance adds the rows of no site, whose text can name internals of the host.
internal sealed class ActivityReader(IActivityQuery query, ManagementAuthority authority, IOperationContextAccessor accessor)
{
    public async Task<PagedResult<ActivityItem>> QueryAsync(ActivityFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var site = (accessor.Current ?? throw new InvalidOperationException("The activity log is read for the site of an operation, and none is running.")).Site.Value;
        await authority.RequireAsync(CorePermissions.ActivityRead, site, cancellationToken);
        var instance = await authority.AllowsAsync(CorePermissions.ActivityRead, site: null, cancellationToken);

        return await query.QueryAsync(filter with { IncludeInstance = instance }, page, cancellationToken);
    }
}
