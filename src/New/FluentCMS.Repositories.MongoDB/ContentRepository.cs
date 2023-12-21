using FluentCMS.Entities;
using MongoDB.Driver;

namespace FluentCMS.Repositories.MongoDB;

public class ContentRepository(
    IMongoDBContext mongoDbContext,
    IAuthContext authContext) :
    AppAssociatedRepository<Content>(mongoDbContext, authContext),
    IContentRepository
{
    public async Task<IEnumerable<Content>> GetAll(Guid appId, Guid contentTypeId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var filter = Builders<Content>.Filter.Eq(x => x.TypeId, contentTypeId);
        var findResult = await Collection.FindAsync(filter, null, cancellationToken);
        return await findResult.ToListAsync(cancellationToken);
    }
}
