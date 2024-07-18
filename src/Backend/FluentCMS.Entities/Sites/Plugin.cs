namespace FluentCMS.Entities;

public class Plugin : SiteAssociatedEntity
{
    public Guid DefinitionId { get; set; }
    public Guid ColumnId { get; set; }
    public int Order { get; set; } = 0;
    public int Cols { get; set; } = 12;
    public int ColsMd { get; set; } = 0;
    public int ColsLg { get; set; } = 0;
    public string Section { get; set; } = default!;
    public bool Locked { get; set; } = false;
}
