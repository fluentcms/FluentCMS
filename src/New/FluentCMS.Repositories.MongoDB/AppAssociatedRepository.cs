namespace FluentCMS.Repositories.MongoDB;

public abstract class AppAssociatedRepository<TEntity> :
    EntityRepository<TEntity>,
    IAppAssociatedRepository<TEntity>
    where TEntity : IAppAssociatedEntity
{
    public AppAssociatedRepository(IMongoDBContext mongoDbContext) : base(mongoDbContext)
    {
    }

    public async Task<IEnumerable<TEntity>> GetAll(Guid appId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var filter = Builders<TEntity>.Filter.Eq(x => x.AppId, appId);
        var findResult = await Collection.FindAsync(filter, null, cancellationToken);
        return await findResult.ToListAsync(cancellationToken);
    }
}
