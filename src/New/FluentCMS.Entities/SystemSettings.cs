namespace FluentCMS.Entities;

public class SystemSettings : AuditableEntity
{
    public List<string> SuperUsers { get; set; } = [];
}
