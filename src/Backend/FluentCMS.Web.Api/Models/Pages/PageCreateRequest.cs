namespace FluentCMS.Web.Api.Models;

public class PageCreateRequest
{
    public Guid SiteId { get; set; }
    public Guid? ParentId { get; set; }
    public Guid? LayoutId { get; set; } = default!;
    public Guid? DetailLayoutId { get; set; } = default!;
    public Guid? EditLayoutId { get; set; } = default!;
    public string Title { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public int Order { get; set; }

    [Required]
    public IEnumerable<Guid> ViewRoleIds { get; set; }

    [Required]
    public IEnumerable<Guid> ContributerRoleIds { get; set; }

    [Required]
    public IEnumerable<Guid> AdminRoleIds { get; set; }
}
