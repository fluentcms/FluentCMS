namespace FluentCMS.Api.Models;

public class PageUpdateRequest
{
    public Guid Id { get; set; }
    public Guid SiteId { get; set; }
    public Guid? ParentId { get; set; }
    public required string Title { get; set; }
    public required string Path { get; set; }
    public int Order { get; set; }
}
