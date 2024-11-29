namespace FluentCMS.Web.Api.Models;

public class RoleCreateRequest
{
    [Required]
    public Guid SiteId { get; set; } = Guid.Empty;

    [Required]
    public string Name { get; set; } = default!;

    public string? Description { get; set; }
}
