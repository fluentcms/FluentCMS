namespace FluentCMS.Entities;

public class PageRow : SiteAssociatedEntity
{
    public Guid? SectionId { get; set; }
    public Dictionary<string,string> Styles { get; set; } = [];
    public int Order { get; set; }
}
