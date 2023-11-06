namespace FluentCMS.Api.Models.Pages;

public class CreatePageRequest
{
    public Guid SiteId { get; set; }
    public Guid? ParentId { get; set; }
    public string Title { get; set; }
    public int Order { get; set; }
    public string Path { get; set; }
}