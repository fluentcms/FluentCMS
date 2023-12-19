using FluentCMS.Entities;
using MongoDB.Driver;

namespace FluentCMS.Repositories.MongoDB;

public class ContentRepository<TContent> :
    AppAssociatedRepository<TContent>,
    IContentRepository<TContent>
    where TContent : Content, new()
{

    public ContentRepository(IMongoDBContext mongoDbContext, IApplicationContext applicationContext) : base(mongoDbContext, applicationContext)
    {
    }

    public async Task<IEnumerable<TContent>> GetAll(Guid appId, Guid contentTypeId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var filter = Builders<TContent>.Filter.Eq(x => x.TypeId, contentTypeId);
        var findResult = await Collection.FindAsync(filter, null, cancellationToken);
        return await findResult.ToListAsync(cancellationToken);
    }
}
