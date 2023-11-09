using uBeac.Services;

namespace uBeac.Identity;

public interface IUserRoleService<TUserKey, TUser> : IService
    where TUserKey : IEquatable<TUserKey>
    where TUser : User<TUserKey>
{
    Task<bool> AddRoles(TUserKey userId, IEnumerable<string> roleNames, CancellationToken cancellationToken = default);
    Task<bool> RemoveRoles(TUserKey userId, IEnumerable<string> roleNames, CancellationToken cancellationToken = default);
    Task<IList<string>> GetRolesForUser(TUserKey userId, CancellationToken cancellationToken = default);
    Task<IList<TUser>> GetUsersInRole(string roleNames, CancellationToken cancellationToken = default);
}

public interface IUserRoleService<TUser> : IUserRoleService<Guid, TUser>
   where TUser : User
{
}