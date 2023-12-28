namespace FluentCMS.Entities;

public class Site : AuditableEntity
{
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public List<string> Urls { get; set; } = [];
    public List<string> Languages { get; set; } = [];
}
