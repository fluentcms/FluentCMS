using FluentCMS.Core.Storage.Entities;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Core.Storage;

internal enum SiteDeletion
{
    Deleted,
    NotFound,
    HasContent,
}

// The deletion of a site's row, for SiteManager. In one transaction: the row is locked first, so the deletion waits for every
// write that is adding an entry to the site (EntryStore.WriteDraftAsync), then deleted in one statement that sees the
// entries those writes committed, and what the caller stages commits with it.
internal static class SiteRows
{
    public static async Task<SiteDeletion> DeleteAsync(CmsDbContext context, IDataProvider provider, Guid id, Func<Task> stage, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        if (!await provider.LockSiteAsync(context, id, exclusive: true, cancellationToken))
        {
            return SiteDeletion.NotFound;
        }

        var deleted = await context.Set<Site>()
            .Where(item => item.Id == id && !item.IsDefault && !context.Documents.IgnoreQueryFilters().Any(document => document.SiteId == id))
            .ExecuteDeleteAsync(cancellationToken);
        if (deleted == 0)
        {
            return SiteDeletion.HasContent;
        }

        await stage();
        await transaction.CommitAsync(CancellationToken.None);
        return SiteDeletion.Deleted;
    }
}
