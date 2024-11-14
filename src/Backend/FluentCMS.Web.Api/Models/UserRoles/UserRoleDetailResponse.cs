namespace FluentCMS.Web.Api.Models;

public class UserRoleDetailResponse
{
    public string Username { get; set; } = string.Empty;
    public bool IsAuthenticated { get; set; } = false;
    public bool IsSuperAdmin { get; set; } = false;
    public Guid UserId { get; set; } = Guid.Empty;
    public List<RoleDetailResponse> Roles { get; set; } = [];
}
