namespace FluentCMS.Repositories.LiteDb;

public class ContentTypeRepository(
    ILiteDBContext liteDbContext,
    IAuthContext authContext) :
    AuditableEntityRepository<ContentType>(liteDbContext, authContext),
    IContentTypeRepository
{
    public async Task<ContentType?> GetBySlug(string contentTypeSlug, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Collection.Query().Where(x => x.Slug == contentTypeSlug).SingleOrDefaultAsync();
    }
}
