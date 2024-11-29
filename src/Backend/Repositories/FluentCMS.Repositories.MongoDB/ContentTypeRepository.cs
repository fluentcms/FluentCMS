namespace FluentCMS.Repositories.MongoDB;

public class ContentTypeRepository(IMongoDBContext mongoDbContext, IApiExecutionContext apiExecutionContext) : SiteAssociatedRepository<ContentType>(mongoDbContext, apiExecutionContext), IContentTypeRepository
{
    public async Task<ContentType?> GetBySlug(Guid siteId, string contentTypeSlug, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var filter = Builders<ContentType>.Filter.Eq(x => x.SiteId, siteId);
        filter &= Builders<ContentType>.Filter.Eq(x => x.Slug, contentTypeSlug);

        var findResult = await Collection.FindAsync(filter, null, cancellationToken);

        return findResult.SingleOrDefault(cancellationToken);
    }
}
