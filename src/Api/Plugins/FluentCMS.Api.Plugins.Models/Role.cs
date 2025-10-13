namespace FluentCMS.Api.Plugins.Models;

public class Role : RoleBase
{
    public Role()
    {
    }
    public Role(string roleName) : this()
    {
        Name = roleName;
    }
}
