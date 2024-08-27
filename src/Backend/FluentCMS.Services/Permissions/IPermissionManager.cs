namespace FluentCMS.Services.Permissions;

public interface IPermissionManager<TEntity> where TEntity : ISiteAssociatedEntity
{
    Task<bool> HasAccess(TEntity entity, string action, CancellationToken cancellationToken = default);
}
