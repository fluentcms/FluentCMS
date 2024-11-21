namespace FluentCMS.Repositories.EFCore.DbModels;

public class GlobalSettings : AuditableEntity
{
    public string SuperAdmins { get; set; } = default!; // comma separated user names
}
