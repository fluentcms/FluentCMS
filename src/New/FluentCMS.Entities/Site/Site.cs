namespace FluentCMS.Entities;
public class Site : AuditableEntity
{
    public required string Name { get; set; }
    public string? Description { get; set; }
    public List<string> Urls { get; set; } = [];
    public Guid DefaultLayoutId { get; set; }
}
