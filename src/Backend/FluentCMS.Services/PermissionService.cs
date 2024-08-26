namespace FluentCMS.Services;

public interface IPermissionService : IAutoRegisterService
{
    Task<IEnumerable<Permission>> SetPermissions<TEntity>(TEntity entity, string action, IEnumerable<Guid> roleIds, CancellationToken cancellationToken = default) where TEntity : ISiteAssociatedEntity;

    Task<IEnumerable<Permission>> GetPermissions(Guid entityId, string action, CancellationToken cancellationToken = default);
}

public class PermissionService(IPermissionRepository permissionRepository) : IPermissionService,
    IMessageHandler<Page>
{
    public async Task<IEnumerable<Permission>> SetPermissions<TEntity>(TEntity entity, string action, IEnumerable<Guid> roleIds, CancellationToken cancellationToken = default) where TEntity : ISiteAssociatedEntity
    {
        return await permissionRepository.SetPermissions(entity, action, roleIds, cancellationToken);
    }

    public async Task<IEnumerable<Permission>> GetPermissions(Guid entityId, string action, CancellationToken cancellationToken = default)
    {
        return await permissionRepository.GetByActionAndEntityId(action, entityId, cancellationToken);
    }

    public async Task Handle(Message<Page> message, CancellationToken cancellationToken)
    {
        switch (message.Action)
        {
            case ActionNames.PageDeleted:
                await DeletePermissions(message.Payload, cancellationToken);
                break;
        }
    }

    private async Task DeletePermissions(Page page, CancellationToken cancellationToken)
    {
        var permissions = await permissionRepository.GetByEntityId(page.Id, cancellationToken);
        await permissionRepository.DeleteMany(permissions.Select(x => x.Id), cancellationToken);
    }

}
