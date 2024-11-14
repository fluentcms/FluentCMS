namespace FluentCMS.Web.Api.Models;

public class FolderCreateRequest
{
    [Required]
    public Guid SiteId { get; set; } = default!;

    [Required]
    public string Name { get; set; } = default!;

    [Required]
    public Guid ParentId { get; set; }
}
