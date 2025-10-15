namespace FluentCMS.Api.Core.Repositories.EntityFramework;

public class RepositoryBase<TEntity>(CmsCoreDbContext dbContext) : Repository<TEntity, CmsCoreDbContext>(dbContext)
    where TEntity : class, IEntity
{

    public virtual async Task<TEntity> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await Query().Single(x => x.Id == id, cancellationToken);
    }

}
