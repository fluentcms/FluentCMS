namespace FluentCMS.Repositories.Decorators;

public abstract class AppAssociatedRepositoryDecorator<TEntity> :
    EntityRepositoryDecorator<TEntity>, IAppAssociatedRepository<TEntity>
    where TEntity : IAppAssociatedEntity
{
    private readonly IAppAssociatedRepository<TEntity> _decorator;

    public AppAssociatedRepositoryDecorator(IAuthContext authContext, IAppAssociatedRepository<TEntity> decorator) : base(authContext, decorator)
    {
        _decorator = decorator;
    }

    public Task<IEnumerable<TEntity>> GetAll(Guid appId, CancellationToken cancellationToken = default)
    {
        return _decorator.GetAll(appId, cancellationToken);
    }
}
