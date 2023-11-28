namespace FluentCMS.Entities;

public class Plugin : AuditEntity
{
    public Guid DefinitionId { get; set; }
    public Guid PageId { get; set; }
    public int Order { get; set; } = 0;
    public string Section { get; set; } = default!;
}
