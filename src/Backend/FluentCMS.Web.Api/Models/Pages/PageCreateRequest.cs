namespace FluentCMS.Web.Api.Models;

public class PageCreateRequest
{
    [Required]
    public Guid SiteId { get; set; }

    public Guid? ParentId { get; set; }

    public Guid? LayoutId { get; set; } = default!;

    public Guid? DetailLayoutId { get; set; } = default!;

    public Guid? EditLayoutId { get; set; } = default!;

    [Required]
    public string Title { get; set; } = string.Empty;

    [Required]
    public string Path { get; set; } = string.Empty;

    [Required]
    public int Order { get; set; }

    [Required]
    public IEnumerable<Guid> ViewRoleIds { get; set; } = [];

    [Required]
    public IEnumerable<Guid> ContributorRoleIds { get; set; } = [];

    [Required]
    public IEnumerable<Guid> AdminRoleIds { get; set; } = [];
}
