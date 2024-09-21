using Microsoft.Extensions.Caching.Memory;

namespace FluentCMS.Repositories.Caching;

public abstract class AuditableEntityRepository<TEntity>(IAuditableEntityRepository<TEntity> auditableEntityRepository, IMemoryCache memoryCache) : EntityRepository<TEntity>(auditableEntityRepository, memoryCache), IAuditableEntityRepository<TEntity> where TEntity : IAuditableEntity
{
}
