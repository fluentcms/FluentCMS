namespace FluentCMS.Repositories.Abstractions;

public interface IUserRoleRepository : ISiteAssociatedRepository<UserRole>
{
    Task<IEnumerable<UserRole>> GetByUserId(Guid userId, CancellationToken cancellationToken = default);

    Task<IEnumerable<UserRole>> GetUserRoles(Guid userId, Guid siteId, CancellationToken cancellationToken = default);

    Task<IEnumerable<UserRole>> GetByRoleId(Guid roleId, CancellationToken cancellationToken = default);
}
