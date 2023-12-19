namespace FluentCMS.Entities;

public class Settings : AuditableEntity
{
    public List<string> SuperUsers { get; set; } = [];
}
