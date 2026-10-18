namespace FluentCMS.Infrastructure.Checks;

// A check, the number of its latest version and the number of the version that runs; each version's definition is its own
// CheckVersionRow.
public sealed class CheckRow
{
    public Guid Id { get; set; }

    public Guid SiteId { get; set; }

    public string Name { get; set; } = string.Empty;

    public int Version { get; set; }

    // Null while the check is not active (CHK-02).
    public int? ActiveVersion { get; set; }
}
