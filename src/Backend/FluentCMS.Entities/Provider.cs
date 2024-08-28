namespace FluentCMS.Entities;

public class Provider : AuditableEntity
{
    public string Name { get; set; } = default!;
    public string Type { get; set; } = default!;
    public Dictionary<string, string> Settings { get; set; } = [];
}
