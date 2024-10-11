using Raven.Client.Documents.Linq;

namespace FluentCMS.Repositories.RavenDB;

public abstract class EntityRepository<TEntity> : IEntityRepository<TEntity> where TEntity : IEntity
{
    //protected readonly IRavenCollection<TEntity> Collection;
    //protected readonly IRavenDatabase RavenDatabase;
    protected readonly IDocumentStore Store;

    public EntityRepository(IRavenDBContext RavenDbContext)
    {
        //RavenDatabase = RavenDbContext.Database;
        //Collection = RavenDbContext.Database.GetCollection<TEntity>(EntityRepository<TEntity>.GetCollectionName());

        Store = RavenDbContext.Store;

        // Ensure index on Id field
        

        // var indexKeysDefinition = Builders<TEntity>.IndexKeys.Ascending(x => x.Id);
        // Collection.Indexes.CreateOne(new CreateIndexModel<TEntity>(indexKeysDefinition));
    }

    // private static string GetCollectionName()
    // {
    //     var entityTypeName = typeof(TEntity).Name;
    //     return entityTypeName.Pluralize().ToLowerInvariant();
    // }

    public virtual async Task<IEnumerable<TEntity>> GetAll(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using (var session = Store.OpenAsyncSession())
        {
            var entities = await session.Query<TEntity>().ToListAsync(cancellationToken);
            return entities.AsEnumerable();
        }
    }

    public virtual async Task<TEntity?> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using (var session = Store.OpenAsyncSession())
        {
            return await session.Query<TEntity>().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        }
    }

    public virtual async Task<IEnumerable<TEntity>> GetByIds(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using (var session = Store.OpenAsyncSession())
        {
            var entities = await session.Query<TEntity>().Where(x => ids.Contains(x.Id)).ToListAsync(cancellationToken);
            return entities.AsEnumerable();
        }
    }

    public virtual async Task<TEntity?> Create(TEntity entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using (var session = Store.OpenAsyncSession())
        {
            await session.StoreAsync(entity);

            await session.SaveChangesAsync(cancellationToken);
        }

        return entity;
    }

    public virtual async Task<IEnumerable<TEntity>> CreateMany(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using (var session = Store.OpenAsyncSession())
        {
            foreach (var entity in entities)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await session.StoreAsync(entity);
            }

            await session.SaveChangesAsync(cancellationToken);
        }

        return entities;
    }

    public virtual async Task<TEntity?> Update(TEntity entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using (var session = Store.OpenAsyncSession())
        {
            var dbEntity = await session.Query<TEntity>().SingleOrDefaultAsync(x => x.Id == entity.Id, cancellationToken);
            if (dbEntity == null)
            {
                await session.StoreAsync(entity);

                dbEntity = entity;
            }
            else
            {
                entity.CopyProperties(dbEntity);
            }

            await session.SaveChangesAsync(cancellationToken);

            return dbEntity;
        }
    }

    public virtual async Task<TEntity?> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using (var session = Store.OpenAsyncSession())
        {
            var entity = await session.Query<TEntity>().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

            session.Delete(entity);

            await session.SaveChangesAsync(cancellationToken);

            return entity;
        }
    }

    public virtual async Task<IEnumerable<TEntity>> DeleteMany(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using (var session = Store.OpenAsyncSession())
        {
            var entities = await session.Query<TEntity>().Where(x => ids.Contains(x.Id)).ToListAsync(cancellationToken);

            foreach (var entity in entities)
            {
                cancellationToken.ThrowIfCancellationRequested();

                session.Delete(entity);
            }

            await session.SaveChangesAsync(cancellationToken);

            return entities;
        }
    }
}
