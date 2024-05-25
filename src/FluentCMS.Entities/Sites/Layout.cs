namespace FluentCMS.Entities;

public class Layout : AuditableEntity
{
    public string Name { get; set; } = default!;
    public string Body { get; set; } = default!;
    public string Head { get; set; } = default!;
}
