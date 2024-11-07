namespace FluentCMS.Repositories.EFCore.DbModels;

public class SettingsValue
{
    public Guid Id { get; set; }
    public Guid SettingsId { get; set; }  // Foreign key to Settings

    public string Key { get; set; } = default!;
    public string Value { get; set; } = default!;

    public Settings Settings { get; set; } = default!;
}
