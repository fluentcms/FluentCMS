﻿using FluentCMS.Entities;
using MongoDB.Driver;

namespace FluentCMS.Repositories.MongoDB;

public abstract class AppAssociatedRepository<TEntity> :
    AuditableEntityRepository<TEntity>,
    IAppAssociatedRepository<TEntity>
    where TEntity : IAppAssociatedEntity
{
    public AppAssociatedRepository(IMongoDBContext mongoDbContext, IApplicationContext applicationContext) : base(mongoDbContext, applicationContext)
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
