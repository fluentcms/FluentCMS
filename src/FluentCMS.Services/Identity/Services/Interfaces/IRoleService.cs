using uBeac.Services;

namespace uBeac.Identity;

public interface IRoleService<TKey, TRole> : IService
    where TKey : IEquatable<TKey>
    where TRole : Role<TKey>
{
    Task Create(TRole role, CancellationToken cancellationToken = default);
    Task<bool> Delete(TKey id, CancellationToken cancellationToken = default);
    Task<bool> Update(TRole role, CancellationToken cancellationToken = default);
    Task<bool> Exists(string roleName, CancellationToken cancellationToken = default);
    Task<IEnumerable<TRole>> GetAll(CancellationToken cancellationToken = default);
    Task<TRole> GetById(TKey id, CancellationToken cancellationToken = default);
}

public interface IRoleService<TRole> : IRoleService<Guid, TRole>
   where TRole : Role
{
}