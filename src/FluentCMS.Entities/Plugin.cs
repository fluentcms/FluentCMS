namespace FluentCMS.Entities;

public class Plugin : AuditEntity
{
    public string Title { get; set; } = default!;
    public Guid DefinitionId { get; set; }
    public Guid PageId { get; set; }
    public string Fragment { get; set; } = default!;
    public Dictionary<string, string> Setting { get; set; } = [];
}
