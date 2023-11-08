namespace FluentCMS.Entities;

public class Host : AuditEntity
{
    public List<string> SuperUsers { get; set; } = [];
}
