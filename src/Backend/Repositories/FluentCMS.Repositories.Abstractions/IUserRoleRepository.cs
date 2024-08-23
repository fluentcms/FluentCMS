namespace FluentCMS.Repositories.Abstractions;

public interface IUserRoleRepository : ISiteAssociatedRepository<UserRole>
{
    Task<IEnumerable<UserRole>> GetByUserId(Guid userId, CancellationToken cancellationToken = default);

    Task<IEnumerable<UserRole>> GetBySiteAndUserId(Guid siteId, Guid userId, CancellationToken cancellationToken = default);
}
