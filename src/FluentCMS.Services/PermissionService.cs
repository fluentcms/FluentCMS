namespace FluentCMS.Services;

public interface IPermissionService : IAutoRegisterService
{
    Task<IEnumerable<Permission>> GetByRole(Guid roleId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Permission>> UpdateByRole(Guid roleId, IEnumerable<Tuple<string, string>> areaActions, CancellationToken cancellationToken);
}

public class PermissionService(IPermissionRepository permissionRepository) : IPermissionService
{
    public async Task<IEnumerable<Permission>> GetByRole(Guid roleId, CancellationToken cancellationToken = default)
    {
        return await permissionRepository.GetByRole(roleId, cancellationToken);
    }

    public async Task<IEnumerable<Permission>> UpdateByRole(Guid roleId, IEnumerable<Tuple<string, string>> areaActions, CancellationToken cancellationToken)
    {
        // delete all permissions for the role
        await permissionRepository.DeleteByRole(roleId, cancellationToken);

        // insert new permissions for the role
        return await permissionRepository.CreateMany(areaActions.Select(x => new Permission { RoleId = roleId, Area = x.Item1, Action = x.Item2 }), cancellationToken) ??
            throw new AppException(ExceptionCodes.PermissionUnableToCreate);
    }
}

//public interface IPermissionManager
//{
//    Task<bool> HasPermission(string policyName);
//}

//public class PermissionManager(IAuthContext authContext) : IPermissionManager
//{
//    public Task<bool> HasPermission(string policyName)
//    {
//        // get current user's role

//        // get permissions for the entities

//        // check if current user's role includes in the
//        //
//        return Task.FromResult(true);
//    }
//}
