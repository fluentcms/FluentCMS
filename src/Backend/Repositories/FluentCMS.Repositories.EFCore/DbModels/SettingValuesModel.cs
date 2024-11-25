namespace FluentCMS.Repositories.EFCore.DbModels;

[Table("SettingValues")]
public class SettingValuesModel : EntityModel
{
    public Guid SettingId { get; set; } // Foreign key
    public string Key { get; set; } = default!;
    public string Value { get; set; } = default!;

    public SettingsModel Setting { get; set; } = default!; // Navigation property
}
