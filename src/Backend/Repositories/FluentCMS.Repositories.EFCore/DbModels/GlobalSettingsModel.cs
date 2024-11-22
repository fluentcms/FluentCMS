namespace FluentCMS.Repositories.EFCore.DbModels;

[Table("GlobalSettings")]
public class GlobalSettingsModel : AuditableEntity
{
    public string SuperAdmins { get; set; } = default!; // comma separated user names
}
