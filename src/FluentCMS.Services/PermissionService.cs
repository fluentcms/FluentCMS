namespace FluentCMS.Services;

public class PermissionService(IPermissionRepository permissionRepository)
{
}

public interface IPermissionManager
{
    Task<bool> HasPermission(string policyName);
}

public class PermissionManager(IAuthContext authContext) : IPermissionManager
{
    public Task<bool> HasPermission(string policyName)
    {
        // get current user's role

        // get permissions for the entities

        // check if current user's role includes in the
        //
        return Task.FromResult(true);
    }
}

public class Policies
{
    public const string GLOBAL_ADMIN = "Global:Admin";
    public const string USERS_MANAGE = "Users:Manage";
}
