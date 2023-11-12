namespace FluentCMS.Api.Models;

public class PageSearchRequest
{
    public Guid? ParentId { get; set; }
    public Guid SiteId { get; set; }
}
