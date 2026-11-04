using FluentCMS.Settings;

namespace FluentCMS.Core.Seeding;

// The instance-wide record of which seeders were applied, in the settings area 'seeding'. It is registered only when a host adds a seeder.
[Settings("seeding")]
internal sealed class SeedingState
{
    public Dictionary<string, SeedRecord> Seeders { get; set; } = [];
}

internal sealed class SeedRecord
{
    // The highest version that ran to the end.
    public int Applied { get; set; }

    // Set while a run is under way, so that another instance skips the seeder; a claim older than the timeout can be taken over.
    public int ClaimedVersion { get; set; }

    public DateTimeOffset? ClaimedAt { get; set; }
}
