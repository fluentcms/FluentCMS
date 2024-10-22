namespace FluentCMS.Repositories.Postgres.Repositories;

public class ContentTypeRepository(PostgresDbContext context) : AuditableEntityRepository<ContentType>(context), IContentTypeRepository, IService
{
    public async Task<ContentType?> GetBySlug(string contentTypeSlug, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await GetByExpression(x => x.Slug == contentTypeSlug, cancellationToken);
    }
}
