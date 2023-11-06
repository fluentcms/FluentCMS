namespace FluentCMS.Api.Models.Pages;

public class PageSearchRequest
{
    public Guid? ParentId { get; set; }
    public Guid SiteId { get; set; }
}