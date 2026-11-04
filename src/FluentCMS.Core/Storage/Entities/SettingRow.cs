namespace FluentCMS.Core.Storage.Entities;

// A row of cms_setting: the settings of one area for one site, or the instance default when SiteId is null. Write it through
// the settings service, which checks the permission and tells the cache and the event bus.
public sealed class SettingRow
{
    public Guid Id { get; set; }

    // Null for the instance default.
    public Guid? SiteId { get; set; }

    // The area of the settings class.
    public string Key { get; set; } = "";

    // The whole document, in the canonical form of the settings type.
    public string Json { get; set; } = "{}";

    // Starts at 1 and goes up by one with every write, so that a writer can say which row it read.
    public int Version { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public Guid UpdatedBy { get; set; }
}
