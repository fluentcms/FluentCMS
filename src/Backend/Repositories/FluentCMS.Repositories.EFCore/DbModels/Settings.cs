namespace FluentCMS.Repositories.EFCore.DbModels;

public class Settings: AuditableEntity
{
    public ICollection<SettingValue> Values { get; set; } = [];
}
