namespace FluentCMS.Repositories.Abstractions;

public interface IPermissionRepository : ISiteAssociatedRepository<Permission>
{
    Task<IEnumerable<Permission>> GetByEntityId(Guid entityId, CancellationToken cancellationToken);
    Task<IEnumerable<Permission>> GetByActionAndEntityId(string action, Guid entityId, CancellationToken cancellationToken);
    Task<IEnumerable<Permission>> SetPermissions<TEntity>(TEntity entity, string action, IEnumerable<Guid> roleIds, CancellationToken cancellationToken = default) where TEntity : ISiteAssociatedEntity;
}
