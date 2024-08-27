namespace FluentCMS.Web.Api.Models;

public class UserRoleUpdateRequest
{
    [Required]
    public Guid SiteId { get; set; }

    [Required]
    public Guid UserId { get; set; }

    [Required]
    public IEnumerable<Guid> RoleIds { get; set; } = default!;
}
