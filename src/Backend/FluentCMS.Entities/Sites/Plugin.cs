namespace FluentCMS.Entities;

public class Plugin : SiteAssociatedEntity
{
    public Guid DefinitionId { get; set; }
    public Guid ColumnId { get; set; }
    public int Order { get; set; } = 0;
    public bool Locked { get; set; } = false;
}
