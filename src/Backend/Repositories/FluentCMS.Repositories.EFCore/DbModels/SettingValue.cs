namespace FluentCMS.Repositories.EFCore.DbModels;

public class SettingValue
{
    public Guid SettingsId { get; set; }  // Foreign key to Settings
    public string Key { get; set; } = default!;
    public string Value { get; set; } = default!;
}

