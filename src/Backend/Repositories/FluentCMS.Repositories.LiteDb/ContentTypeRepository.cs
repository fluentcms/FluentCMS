namespace FluentCMS.Repositories.LiteDb;

public class ContentTypeRepository(ILiteDBContext liteDbContext, IApiExecutionContext apiExecutionContext) : AuditableEntityRepository<ContentType>(liteDbContext, apiExecutionContext), IContentTypeRepository
{
    public async Task<ContentType?> GetBySlug(string contentTypeSlug, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Collection.Query().Where(x => x.Slug == contentTypeSlug).SingleOrDefaultAsync();
    }
}
