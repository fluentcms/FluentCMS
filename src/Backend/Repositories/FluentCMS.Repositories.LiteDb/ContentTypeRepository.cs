namespace FluentCMS.Repositories.LiteDb;

public class ContentTypeRepository(ILiteDBContext liteDbContext, IApiExecutionContext apiExecutionContext) : SiteAssociatedRepository<ContentType>(liteDbContext, apiExecutionContext), IContentTypeRepository
{
    public async Task<ContentType?> GetBySlug(Guid siteId, string contentTypeSlug, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Collection.Query().Where(x => x.SiteId == siteId && x.Slug == contentTypeSlug).SingleOrDefaultAsync();
    }
}
