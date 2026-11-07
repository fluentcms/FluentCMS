using System.Runtime.CompilerServices;
using FluentCMS.Content;
using FluentCMS.Core.Events;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Authorization;

// Raises Role.PermissionsVersion by one in the same SaveChanges that changes a role or any of its grants, so the
// stamp and the change commit together. PermissionsVersion is a concurrency token: two writers of one role cannot
// both land on the same new version, which would leave one instance's cache holding stale grants under it.
// A bulk ExecuteUpdate or ExecuteDelete bypasses the change tracker and must bump the stamp itself.
// The same save stages a RolePermissionsChangedEvent per role for the outbox, with the role's site or, for a role of the
// instance, every site, so that what was served under the old grants, such as the Delivery output cache, is let go of
// even when the process stops right after the commit.
// The outbox is resolved when a role first changes, so that making a CmsDbContext does not build it.
internal sealed class RoleVersionInterceptor(IServiceProvider services, IOperationContextAccessor operations, TimeProvider time) : SaveChangesInterceptor
{
    private readonly ConditionalWeakTable<DbContext, List<OutboxMessage>> _staged = new();

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is { } context)
        {
            List<Role> changed = [];
            foreach (var roleId in ChangedRoles(context))
            {
                if (context.Set<Role>().Find(roleId) is { } role)
                {
                    Bump(context, role);
                    changed.Add(role);
                }
            }

            // EF calls the synchronous path for a synchronous save, which blocks on its own I/O as this does.
            var sites = changed.Any(role => role.SiteId is null) ? context.Set<Site>().Select(site => site.Id).OrderBy(id => id).ToList() : [];
            Stage(context, changed.Count == 0 ? [] : Outbox.BuildAsync(Events(changed, sites), CancellationToken.None).AsTask().GetAwaiter().GetResult());
        }

        return result;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context)
        {
            List<Role> changed = [];
            foreach (var roleId in ChangedRoles(context))
            {
                if (await context.Set<Role>().FindAsync([roleId], cancellationToken) is { } role)
                {
                    Bump(context, role);
                    changed.Add(role);
                }
            }

            var sites = changed.Any(role => role.SiteId is null) ? await context.Set<Site>().Select(site => site.Id).OrderBy(id => id).ToListAsync(cancellationToken) : [];
            Stage(context, changed.Count == 0 ? [] : await Outbox.BuildAsync(Events(changed, sites), cancellationToken));
        }

        return result;
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Saved(eventData.Context);
        return result;
    }

    // The rows are committed when there is no transaction left open, so the drain is asked for now; inside one, the
    // recurring drain finds them once it commits. Waiting here would block on SQLite's write lock that the transaction holds.
    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        if (Saved(eventData.Context) && eventData.Context!.Database.CurrentTransaction is null)
        {
            await services.GetRequiredService<OutboxKick>().KickAsync();
        }

        return result;
    }

    // A save that fails leaves its rows to the next one, which stages its own.
    public override void SaveChangesFailed(DbContextErrorEventData eventData) => Unstage(eventData.Context);

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Unstage(eventData.Context);
        return Task.CompletedTask;
    }

    private OutboxRows Outbox => services.GetRequiredService<OutboxRows>();

    private IEnumerable<CmsEvent> Events(List<Role> roles, List<Guid> sites)
    {
        var source = operations.Current is { } operation ? EventSource.From(operation) : EventSource.FromSystem(time.GetUtcNow());
        return roles.Select(role => new RolePermissionsChangedEvent(role.Id, role.SiteId is { } site ? [site] : sites) { Source = source });
    }

    private void Stage(DbContext context, List<OutboxMessage> rows)
    {
        if (rows.Count > 0)
        {
            context.AddRange(rows);
            _staged.AddOrUpdate(context, rows);
        }
    }

    private bool Saved(DbContext? context) => context is not null && _staged.Remove(context);

    private void Unstage(DbContext? context)
    {
        if (context is not null && _staged.TryGetValue(context, out var rows))
        {
            _staged.Remove(context);
            foreach (var row in rows)
            {
                context.Entry(row).State = EntityState.Detached;
            }
        }
    }

    private static void Bump(DbContext context, Role role)
    {
        var version = context.Entry(role).Property(item => item.PermissionsVersion);
        version.CurrentValue = version.OriginalValue + 1;
    }

    // The roles whose stamp must rise: those with a changed grant, and those edited themselves. A role that is
    // added keeps its starting version, and one that is deleted needs none.
    private static HashSet<Guid> ChangedRoles(DbContext context)
    {
        var roles = new HashSet<Guid>();
        var excluded = new HashSet<Guid>();

        foreach (var entry in context.ChangeTracker.Entries())
        {
            switch (entry.Entity)
            {
                case Role role when entry.State is EntityState.Added or EntityState.Deleted:
                    excluded.Add(role.Id);
                    break;
                case Role role when entry.State == EntityState.Modified && EditsMoreThanTheStamp(entry):
                    roles.Add(role.Id);
                    break;
                case RoleGrant grant when entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted:
                    roles.Add(grant.RoleId);
                    if (entry.State == EntityState.Modified && entry.Property(nameof(RoleGrant.RoleId)).OriginalValue is Guid previous)
                    {
                        roles.Add(previous);
                    }

                    break;
            }
        }

        roles.ExceptWith(excluded);
        return roles;
    }

    private static bool EditsMoreThanTheStamp(EntityEntry entry) =>
        entry.Properties.Any(property => property.IsModified && property.Metadata.Name != nameof(Role.PermissionsVersion));
}
