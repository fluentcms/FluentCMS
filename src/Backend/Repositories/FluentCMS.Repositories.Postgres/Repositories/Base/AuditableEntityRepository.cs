namespace FluentCMS.Repositories.Postgres.Repositories.Base;

public abstract class AuditableEntityRepository<TEntity>(PostgresDbContext context) : EntityRepository<TEntity>(context), IAuditableEntityRepository<TEntity> where TEntity : AuditableEntity
{


}
