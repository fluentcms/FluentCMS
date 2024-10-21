namespace FluentCMS.Repositories.Postgres.Repositories;

public class ContentRepository(PostgresDbContext context) : AuditableEntityRepository<Content>(context), IContentRepository, IService
{
    public async Task<IEnumerable<Content>> GetAll(Guid contentTypeId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Table.Where(x => x.TypeId == contentTypeId)
            .ToListAsync(cancellationToken);
    }
}
