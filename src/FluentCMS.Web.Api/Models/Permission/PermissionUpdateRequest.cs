namespace FluentCMS.Web.Api.Models;

public class PermissionUpdateRequest
{
    [Required]
    public Guid RoleId { get; set; }

    [Required]
    public List<PolicyRequest> Policies { get; set; } = [];
}
