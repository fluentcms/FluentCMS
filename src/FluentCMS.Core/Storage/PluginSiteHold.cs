using System.Runtime.CompilerServices;
using FluentCMS.Content;
using FluentCMS.Core.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace FluentCMS.Core.Storage;

// A save of a plugin's context that adds a row with a site_id column, or moves a row to another site, holds the row of that
// site in cms_site shared to its commit (IDataProvider.LockSiteAsync), as a content write that adds an entry does. The
// deletion of the site takes the row exclusively, so the save either commits first, and the plugin's cleanup of the site,
// which runs after the deletion, finds its row, or waits for the deletion and is refused with FCMS2301, whatever an
// instance's SiteCache still holds (#3485). A save outside a transaction gets one of its own for it; one inside the plugin's
// own transaction holds the site to the end of that. Core adds it to every PluginDbContext<T> it registers.
internal sealed class PluginSiteHold(IDataProvider provider) : SaveChangesInterceptor
{
    private const string SiteColumn = "site_id";

    private readonly ConditionalWeakTable<DbContext, IDbContextTransaction> _own = new();

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        HoldAsync(eventData.Context, CancellationToken.None).GetAwaiter().GetResult();
        return result;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        await HoldAsync(eventData.Context, cancellationToken);
        return result;
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        EndAsync(eventData.Context, commit: true).GetAwaiter().GetResult();
        return result;
    }

    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        await EndAsync(eventData.Context, commit: true);
        return result;
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData) => EndAsync(eventData.Context, commit: false).GetAwaiter().GetResult();

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default) => EndAsync(eventData.Context, commit: false);

    public override void SaveChangesCanceled(DbContextEventData eventData) => EndAsync(eventData.Context, commit: false).GetAwaiter().GetResult();

    public override Task SaveChangesCanceledAsync(DbContextEventData eventData, CancellationToken cancellationToken = default) => EndAsync(eventData.Context, commit: false);

    private async Task HoldAsync(DbContext? context, CancellationToken cancellationToken)
    {
        if (context is null || SitesOf(context) is not { Count: > 0 } sites)
        {
            return;
        }

        if (context.Database.CurrentTransaction is null)
        {
            _own.AddOrUpdate(context, await context.Database.BeginTransactionAsync(cancellationToken));
        }

        try
        {
            foreach (var site in sites)
            {
                if (!await provider.LockSiteAsync(context, site, exclusive: false, cancellationToken))
                {
                    throw new ValidationException([new ValidationError("site", SiteErrorCodes.SiteNotFound, $"There is no site {site}.")]);
                }
            }
        }
        catch
        {
            await EndAsync(context, commit: false);
            throw;
        }
    }

    // Only the transaction this began for a save; the plugin's own is the plugin's to end.
    private async Task EndAsync(DbContext? context, bool commit)
    {
        if (context is null || !_own.TryGetValue(context, out var transaction))
        {
            return;
        }

        _own.Remove(context);
        await using (transaction)
        {
            if (commit)
            {
                await transaction.CommitAsync(CancellationToken.None);
            }
        }
    }

    // In order, so that two saves hold the sites they share in one order.
    private static List<Guid> SitesOf(DbContext context)
    {
        SortedSet<Guid> sites = [];
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified) || SiteOf(entry.Metadata) is not { } property)
            {
                continue;
            }

            var member = entry.Property(property);
            if ((entry.State == EntityState.Added || member.IsModified) && member.CurrentValue is Guid site && site != Guid.Empty)
            {
                sites.Add(site);
            }
        }

        return [.. sites];
    }

    private static IProperty? SiteOf(IEntityType type) =>
        type.GetProperties().FirstOrDefault(property => (property.ClrType == typeof(Guid) || property.ClrType == typeof(Guid?)) && property.GetColumnName() == SiteColumn);
}
