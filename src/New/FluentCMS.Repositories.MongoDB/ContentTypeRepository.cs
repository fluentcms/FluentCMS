namespace FluentCMS.Repositories.MongoDB;

public class ContentTypeRepository(
    IMongoDBContext mongoDbContext,
    IAuthContext authContext) :
    AppAssociatedRepository<ContentType>(mongoDbContext, authContext),
    IContentTypeRepository
{
    public async Task<ContentType?> GetBySlug(Guid appId, string contentTypeSlug, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var filter = Builders<ContentType>.Filter.Eq(x => x.Slug, contentTypeSlug);

        var findResult = await Collection.FindAsync(filter, null, cancellationToken);

        return findResult.SingleOrDefault(cancellationToken);
    }
}
