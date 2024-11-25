namespace FluentCMS.Repositories.EFCore.DbModels;

[Table("GlobalSettings")]
public class GlobalSettingsModel : AuditableEntityModel
{
    public string SuperAdmins { get; set; } = default!; // comma separated user names
}
