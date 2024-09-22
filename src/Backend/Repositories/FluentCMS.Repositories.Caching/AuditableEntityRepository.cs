namespace FluentCMS.Repositories.Caching;

public abstract class AuditableEntityRepository<TEntity>(IAuditableEntityRepository<TEntity> auditableEntityRepository, ICacheProvider cacheProvider) : EntityRepository<TEntity>(auditableEntityRepository, cacheProvider), IAuditableEntityRepository<TEntity> where TEntity : IAuditableEntity
{
}
