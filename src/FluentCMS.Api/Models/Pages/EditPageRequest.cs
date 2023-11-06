namespace FluentCMS.Api.Models.Pages;

public class EditPageRequest
{
    public Guid Id { get; set; }
    public Guid SiteId { get; set; }
    public Guid? ParentId { get; set; }
    public required string Title { get; set; }
    public int Order { get; set; }
    public required string Path { get; set; }
}