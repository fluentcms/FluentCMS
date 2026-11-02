using FluentCMS.Core.Storage.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FluentCMS.Core.Authorization;

// Raises Role.PermissionsVersion by one in the same SaveChanges that changes a role or any of its grants, so the
// stamp and the change commit together. PermissionsVersion is a concurrency token: two writers of one role cannot
// both land on the same new version, which would leave one instance's cache holding stale grants under it.
// A bulk ExecuteUpdate or ExecuteDelete bypasses the change tracker and must bump the stamp itself.
internal sealed class RoleVersionInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is { } context)
        {
            foreach (var roleId in ChangedRoles(context))
            {
                var role = context.Set<Role>().Find(roleId);
                if (role is not null)
                {
                    Bump(context, role);
                }
            }
        }

        return result;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context)
        {
            foreach (var roleId in ChangedRoles(context))
            {
                var role = await context.Set<Role>().FindAsync([roleId], cancellationToken);
                if (role is not null)
                {
                    Bump(context, role);
                }
            }
        }

        return result;
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
