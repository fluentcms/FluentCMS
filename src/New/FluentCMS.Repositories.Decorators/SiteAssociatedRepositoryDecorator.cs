namespace FluentCMS.Repositories.Decorators;

public abstract class SiteAssociatedRepositoryDecorator<TEntity> : EntityRepositoryDecorator<TEntity>, ISiteAssociatedRepository<TEntity> where TEntity : ISiteAssociatedEntity
{
    private readonly ISiteAssociatedRepository<TEntity> _decorator;

    public SiteAssociatedRepositoryDecorator(IAuthContext authContext, ISiteAssociatedRepository<TEntity> decorator) : base(authContext, decorator)
    {
        _decorator = decorator;
    }

    public Task<IEnumerable<TEntity>> GetAllForSite(Guid siteId, CancellationToken cancellationToken = default)
    {
        return _decorator.GetAllForSite(siteId, cancellationToken);
    }

    public Task<TEntity?> GetByIdForSite(Guid id, Guid siteId, CancellationToken cancellationToken = default)
    {
        return _decorator.GetByIdForSite(id, siteId, cancellationToken);
    }
}
