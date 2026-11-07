using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace FluentCMS.Core.Content;

// What a context tracks before a write, so that a write that failed can be taken back off it before it runs again: what
// the write left tracked is detached, and what the caller tracked goes back to the state and values it had, since the
// rollback undid whatever of it was saved. The context may hold the caller's entries, so it is not cleared.
internal sealed class TrackerSnapshot
{
    private readonly DbContext _context;
    private readonly HashSet<object> _known;
    private readonly List<Tracked> _entries;

    private TrackerSnapshot(DbContext context)
    {
        _context = context;
        context.ChangeTracker.DetectChanges();
        _entries = [.. context.ChangeTracker.Entries().Select(Track)];
        _known = _entries.Select(item => item.Entry.Entity).ToHashSet(ReferenceEqualityComparer.Instance);
    }

    public static TrackerSnapshot Take(DbContext context) => new(context);

    public void Restore()
    {
        foreach (var left in _context.ChangeTracker.Entries().Where(item => !_known.Contains(item.Entity)).ToList())
        {
            left.State = EntityState.Detached;
        }

        foreach (var item in _entries)
        {
            item.Restore();
        }
    }

    private static Tracked Track(EntityEntry entry) => new(
        entry,
        entry.State,
        entry.CurrentValues.Clone(),
        entry.State == EntityState.Added ? null : entry.OriginalValues.Clone(),
        [.. entry.Properties.Where(property => property.IsModified).Select(property => property.Metadata.Name)]);

    private sealed record Tracked(EntityEntry Entry, EntityState State, PropertyValues Current, PropertyValues? Original, List<string> Modified)
    {
        public void Restore()
        {
            Entry.State = State == EntityState.Added ? EntityState.Added : EntityState.Unchanged;
            if (Original is not null)
            {
                Entry.OriginalValues.SetValues(Original);
            }

            Entry.CurrentValues.SetValues(Current);
            if (State != EntityState.Modified)
            {
                Entry.State = State;
                return;
            }

            foreach (var name in Modified)
            {
                Entry.Property(name).IsModified = true;
            }
        }
    }
}
