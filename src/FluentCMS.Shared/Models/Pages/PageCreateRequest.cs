namespace FluentCMS.Api.Models;

public class PageCreateRequest
{
    public Guid SiteId { get; set; }
    public Guid? ParentId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public int Order { get; set; }
}
