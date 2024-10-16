using Raven.Client.Documents.Linq;

namespace FluentCMS.Repositories.RavenDB;

public abstract class EntityRepository<TEntity> : IEntityRepository<TEntity> where TEntity : IEntity
{
    protected readonly IDocumentStore Store;

    public EntityRepository(IRavenDBContext RavenDbContext)
    {
        Store = RavenDbContext.Store;

        // Ensure index on Id field
        // var indexKeysDefinition = Builders<TEntity>.IndexKeys.Ascending(x => x.Id);
        // Collection.Indexes.CreateOne(new CreateIndexModel<TEntity>(indexKeysDefinition));
    }

    public virtual async Task<IEnumerable<TEntity>> GetAll(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using (var session = Store.OpenAsyncSession())
        {
            var entities = await session.Query<RavenEntity<TEntity>>()
                                        .Select(x => x.Data)
                                        .ToListAsync(cancellationToken);
            
            return entities.AsEnumerable();
        }
    }

    public virtual async Task<TEntity?> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using (var session = Store.OpenAsyncSession())
        {
            var entity = await session.Query<RavenEntity<TEntity>>().SingleOrDefaultAsync(x => x.Data.Id == id, cancellationToken);

            return entity.Data;
        }
    }

    public virtual async Task<IEnumerable<TEntity>> GetByIds(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using (var session = Store.OpenAsyncSession())
        {
            var entities = await session.Query<RavenEntity<TEntity>>().Where(x => ids.Contains(x.Data.Id))
                                        .Select(x => x.Data)
                                        .ToListAsync(cancellationToken);

            return entities.AsEnumerable();
        }
    }

    public virtual async Task<TEntity?> Create(TEntity entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using (var session = Store.OpenAsyncSession())
        {
            if (entity.Id == Guid.Empty)
            {
                entity.Id = Guid.NewGuid();
            }

            await session.StoreAsync(new RavenEntity<TEntity>(entity), cancellationToken);

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

                if (entity.Id == Guid.Empty)
                {
                    entity.Id = Guid.NewGuid();
                }

                await session.StoreAsync(new RavenEntity<TEntity>(entity), cancellationToken);
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
            var id = entity.Id; // Needs to be extracted to guid to avoid type casts in query.

            var dbEntity = await session.Query<RavenEntity<TEntity>>().SingleOrDefaultAsync(x => x.Data.Id == id, cancellationToken);
            if (dbEntity == null)
            {
                if (entity.Id == Guid.Empty)
                {
                    entity.Id = Guid.NewGuid();
                }

                dbEntity = new RavenEntity<TEntity>(entity);

                await session.StoreAsync(dbEntity, cancellationToken);
            }
            else
            {
                entity.CopyProperties(dbEntity.Data);
            }

            await session.SaveChangesAsync(cancellationToken);

            return dbEntity.Data;
        }
    }

    public virtual async Task<TEntity?> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using (var session = Store.OpenAsyncSession())
        {
            var entity = await session.Query<RavenEntity<TEntity>>().SingleOrDefaultAsync(x => x.Data.Id == id, cancellationToken);

            session.Delete(entity);

            await session.SaveChangesAsync(cancellationToken);

            return entity.Data; // A bit strange to return to object we just deleted.
        }
    }

    public virtual async Task<IEnumerable<TEntity>> DeleteMany(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using (var session = Store.OpenAsyncSession())
        {
            var entities = await session.Query<RavenEntity<TEntity>>().Where(x => ids.Contains(x.Data.Id)).ToListAsync(cancellationToken);

            foreach (var entity in entities)
            {
                cancellationToken.ThrowIfCancellationRequested();

                session.Delete(entity);
            }

            await session.SaveChangesAsync(cancellationToken);

            return entities.Select(x => x.Data);
        }
    }
}
