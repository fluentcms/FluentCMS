namespace FluentCMS.Core.Storage.Entities;

// A row of cms_lock: one named lock of IDistributedLock. A free lock keeps its row with no owner, so that taking it is one
// conditional update and only the first use of a name inserts.
public sealed class LockRow
{
    public const int NameMaxLength = 128;

    public string Name { get; set; } = "";

    // Who holds the lock, one value per acquisition; null when it is free.
    public string? Owner { get; set; }

    public DateTimeOffset? AcquiredAt { get; set; }

    // The holder renews it while it holds the lock; once it has passed, another may take the lock.
    public DateTimeOffset? LeaseUntil { get; set; }
}
