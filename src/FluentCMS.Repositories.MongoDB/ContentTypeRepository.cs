namespace FluentCMS.Repositories.MongoDB;

public class ContentTypeRepository(
    IMongoDBContext mongoDbContext,
    IAuthContext authContext) :
    AuditableEntityRepository<ContentType>(mongoDbContext, authContext),
    IContentTypeRepository
{
    public async Task<ContentType?> GetBySlug(string contentTypeSlug, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var filter = Builders<ContentType>.Filter.Eq(x => x.Slug, contentTypeSlug);

        var findResult = await Collection.FindAsync(filter, null, cancellationToken);

        return findResult.SingleOrDefault(cancellationToken);
    }
}
