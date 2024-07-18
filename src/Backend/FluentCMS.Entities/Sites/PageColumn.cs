namespace FluentCMS.Entities;

public class PageColumn : SiteAssociatedEntity
{
    public Guid? RowId { get; set; }
    public int Order { get; set; }
    public Dictionary<string,string> Styles { get; set; } = [];
}
