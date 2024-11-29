namespace FluentCMS.Repositories.MongoDB;

public class ContentRepository(IMongoDBContext mongoDbContext, IApiExecutionContext apiExecutionContext) : SiteAssociatedRepository<Content>(mongoDbContext, apiExecutionContext), IContentRepository
{
    public async Task<IEnumerable<Content>> GetAll(Guid contentTypeId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var filter = Builders<Content>.Filter.Eq(x => x.TypeId, contentTypeId);
        var findResult = await Collection.FindAsync(filter, null, cancellationToken);
        return await findResult.ToListAsync(cancellationToken);
    }
}
