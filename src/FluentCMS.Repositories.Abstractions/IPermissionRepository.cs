namespace FluentCMS.Repositories.Abstractions;

public interface IPermissionRepository : IAuditableEntityRepository<Permission>
{
    Task<IEnumerable<Permission>> GetByRole(Guid roleId, CancellationToken cancellationToken = default);
    Task<bool> DeleteByRole(Guid roleId, CancellationToken cancellationToken = default);
}
