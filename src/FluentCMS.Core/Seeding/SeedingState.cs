using FluentCMS.Settings;

namespace FluentCMS.Core.Seeding;

// The instance-wide record of which seeders were applied, in the settings area "seeding". It is registered only when a host adds a seeder.
[Settings("seeding")]
internal sealed class SeedingState
{
    public Dictionary<string, SeedRecord> Seeders { get; set; } = [];

    // Held by the one run that is seeding, whatever seeders it runs; null when nobody is.
    public SeedClaim? Claim { get; set; }
}

internal sealed class SeedRecord
{
    // The highest version that ran to the end.
    public int Applied { get; set; }
}

// Renewed by its owner while the run lasts, so that a claim older than the timeout means the owner is gone.
internal sealed class SeedClaim
{
    public Guid Id { get; set; }

    public DateTimeOffset At { get; set; }
}
