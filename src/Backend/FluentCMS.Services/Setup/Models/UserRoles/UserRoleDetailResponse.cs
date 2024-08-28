namespace FluentCMS.Services.Setup.Models;

public class UserRoleDetailModel
{
    public string Username { get; private set; } = string.Empty;
    public bool IsAuthenticated { get; private set; } = false;
    public Guid UserId { get; private set; } = Guid.Empty;
    public List<RoleDetailModel> Roles { get; set; } = [];
}
