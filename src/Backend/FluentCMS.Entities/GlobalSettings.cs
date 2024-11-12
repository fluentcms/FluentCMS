namespace FluentCMS.Entities;

public class GlobalSettings : AuditableEntity
{
    public List<string> SuperAdmins { get; set; } = [];
}
