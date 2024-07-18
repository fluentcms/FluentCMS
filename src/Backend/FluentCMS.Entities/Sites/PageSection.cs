namespace FluentCMS.Entities;

public class PageSection : SiteAssociatedEntity
{
    public Guid? PageId { get; set; }
    public Dictionary<string,string> Styles { get; set; } = [];
    public int Order { get; set; }
}
