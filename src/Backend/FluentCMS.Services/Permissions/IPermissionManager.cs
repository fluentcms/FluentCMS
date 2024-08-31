namespace FluentCMS.Services.Permissions;

public interface IPermissionManager
{
    Task<bool> HasAccess<TEntity>(TEntity entity, string action, CancellationToken cancellationToken = default) where TEntity : ISiteAssociatedEntity;
    Task<IEnumerable<TEntity>> HasAccess<TEntity>(IEnumerable<TEntity> entities, string action, CancellationToken cancellationToken = default) where TEntity : ISiteAssociatedEntity;
}
