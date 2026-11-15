namespace FluentCMS.Core.Storage.Entities;

// A row of cms_user_preference: how a user wants the admin shown, kept for its sessions on every device. A null value is
// one the user has not chosen, which the admin fills from the browser or its own configuration. UserPreferences writes it.
public sealed class UserPreferenceRow
{
    public Guid UserId { get; set; }

    public string? Culture { get; set; }

    public string? TimeZone { get; set; }

    public string? DateFormat { get; set; }

    public string? NumberFormat { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
