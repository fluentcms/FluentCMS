using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace FluentCMS.Core.Content;

// A context may hold what its caller has not saved. That is not the save of another write to make, or to lose if that
// write fails, so it is set aside while the write saves and put back after, and whatever the write added is detached.
internal sealed class HeldChanges
{
    private readonly DbContext _context;
    private readonly HashSet<object> _known;
    private readonly List<HeldEntry> _held;

    private HeldChanges(DbContext context)
    {
        _context = context;
        context.ChangeTracker.DetectChanges();
        _known = context.ChangeTracker.Entries().Select(item => item.Entity).ToHashSet(ReferenceEqualityComparer.Instance);
        _held = context.ChangeTracker.Entries().Where(item => item.State != EntityState.Unchanged).Select(Hold).ToList();
    }

    public static HeldChanges Take(DbContext context) => new(context);

    public void Release()
    {
        foreach (var own in _context.ChangeTracker.Entries().Where(item => !_known.Contains(item.Entity)).ToList())
        {
            own.State = EntityState.Detached;
        }

        foreach (var pending in _held)
        {
            pending.Restore();
        }
    }

    private static HeldEntry Hold(EntityEntry entry)
    {
        var original = entry.State == EntityState.Modified ? entry.OriginalValues.Clone() : null;
        var modified = entry.Properties.Where(property => property.IsModified).Select(property => property.Metadata.Name).ToList();
        var state = entry.State;
        entry.State = EntityState.Detached;
        return new HeldEntry(entry, state, original, modified);
    }

    private sealed record HeldEntry(EntityEntry Entry, EntityState State, PropertyValues? Original, List<string> Modified)
    {
        public void Restore()
        {
            if (Original is null)
            {
                Entry.State = State;
                return;
            }

            Entry.State = EntityState.Unchanged;
            Entry.OriginalValues.SetValues(Original);
            foreach (var name in Modified)
            {
                Entry.Property(name).IsModified = true;
            }
        }
    }
}
