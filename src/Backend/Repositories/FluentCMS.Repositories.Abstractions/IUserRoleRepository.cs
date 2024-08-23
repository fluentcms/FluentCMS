namespace FluentCMS.Repositories.Abstractions;

public interface IUserRoleRepository : ISiteAssociatedRepository<UserRole>
{
    Task<IEnumerable<UserRole>> GetByUserId(Guid userId, CancellationToken cancellationToken = default);

    Task<IEnumerable<Guid>> GetUserRoleIds(Guid userId, Guid siteId, CancellationToken cancellationToken = default);
}
