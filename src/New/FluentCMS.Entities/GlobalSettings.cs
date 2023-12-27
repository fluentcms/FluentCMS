namespace FluentCMS.Entities;

public class GlobalSettings : AuditableEntity
{
    public List<string> SuperUsers { get; set; } = [];
}
