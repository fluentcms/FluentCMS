namespace FluentCMS.Repositories.EFCore.DbModels;

public class Settings : AuditableEntity
{
    public ICollection<SettingValues> Values { get; set; } = []; // Navigation property
}
