namespace FluentCMS.Repositories.EFCore.DbModels;

public class SettingValues : Entity
{
    public Guid SettingId { get; set; } // Foreign key
    public string Key { get; set; } = default!;
    public string Value { get; set; } = default!;

    public Settings Setting { get; set; } = default!; // Navigation property
}
