using FluentCMS.Core.Entities;
using FluentCMS.Repository.Abstractions;
using LiteDB;
using LiteDB.Async;
using System;
using System.Linq.Expressions;

namespace FluentCMS.Repository.LiteDb
{
    public class LiteDbGenericRepository<TKey, TEntity> : IGenericRepository<TKey, TEntity>
                where TKey : IEquatable<TKey>
                where TEntity : IEntity<TKey>
    {
        protected LiteDatabaseAsync DataBase { get; }
        protected ILiteCollectionAsync<TEntity> Collection { get; }
        protected ILiteCollectionAsync<BsonDocument> BsonCollection { get; }
        protected ILiteDbContext LiteDbContext { get; }


        public LiteDbGenericRepository(ILiteDbContext liteDbContext)
        {
            DataBase = liteDbContext.Database;
            Collection = liteDbContext.Database.GetCollection<TEntity>(GetCollectionName());
            BsonCollection = liteDbContext.Database.GetCollection<BsonDocument>(GetCollectionName());
            LiteDbContext = liteDbContext;

            //todo: ask about these!!
            //ApplicationContext = applicationContext;
            //History = history;
        }

        protected virtual string GetCollectionName()
        {
            return typeof(TEntity).Name;
        }

        public virtual Task Create(TEntity entity, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ////todo: implement ApplicationContext
            ////If the entity is extend from IAuditEntity, the audit properties (CreatedAt, CreatedBy, etc.) should be set
            if (entity is IAuditEntity<TKey> audit) SetPropertiesOnCreate(audit);

            return Collection.InsertAsync(entity);

            ////todo: implement History Manager
            //await History.Write(entity, actionName, cancellationToken);
        }

        private void SetPropertiesOnCreate(IAuditEntity<TKey> audit)
        {
            audit.CreatedAt = DateTime.UtcNow;
            //todo set user
        }

        public virtual Task Delete(TKey id, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();


            return Collection.DeleteAsync(new BsonValue(id));

            //await History.Write(entity, actionName, cancellationToken);
        }

        public virtual Task<IEnumerable<TEntity>> GetAll(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Collection.FindAllAsync();
        }

        public virtual Task<IEnumerable<TEntity>> GetAll(Expression<Func<TEntity, bool>> filter, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            //todo: Implement Pagination
            
            return Collection.FindAsync(filter);
        }

        public virtual Task<TEntity> GetById(TKey id, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Collection.FindByIdAsync(new BsonValue(id));
        }

        public virtual Task<IEnumerable<TEntity>> GetByIds(IEnumerable<TKey> ids, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Collection.FindAsync(x => ids.Contains(x.Id));
        }

        public virtual Task Update(TEntity entity, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ////todo: implement ApplicationContext
            ////If the entity is extend from IAuditEntity, the audit properties (LastUpdatedAt, LastUpdatedBy, etc.) should be set
            if (entity is IAuditEntity<TKey> audit) SetPropertiesOnUpdate(audit);

            return Collection.UpdateAsync(entity);

            //await History.Write(entity, actionName, cancellationToken);
        }

        private void SetPropertiesOnUpdate(IAuditEntity<TKey> audit)
        {
            audit.LastUpdatedAt = DateTime.UtcNow;
            //todo set user
        }
    }
    public class LiteDbGenericRepository<TEntity> : LiteDbGenericRepository<Guid, TEntity>, IGenericRepository<TEntity>
                where TEntity : IEntity
    {
        public LiteDbGenericRepository(ILiteDbContext liteDbContext) : base(liteDbContext)
        {
        }
    }
}
