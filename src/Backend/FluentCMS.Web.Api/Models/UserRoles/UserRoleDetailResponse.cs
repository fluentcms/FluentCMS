namespace FluentCMS.Web.Api.Models;

public class UserRoleDetailResponse
{
    public string Username { get; private set; } = string.Empty;
    public bool IsAuthenticated { get; private set; } = false;
    public Guid UserId { get; private set; } = Guid.Empty;
    public List<RoleDetailResponse> Roles { get; set; } = [];
}
