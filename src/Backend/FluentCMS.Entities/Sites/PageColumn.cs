namespace FluentCMS.Entities;

public class PageColumn : SiteAssociatedEntity
{
    public Guid? SectionId { get; set; }
    public int Order { get; set; }
    public Dictionary<string,string> Styles { get; set; } = [];
}
