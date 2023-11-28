using FluentCMS.Entities;
using MongoDB.Driver;

namespace FluentCMS.Repositories.MongoDB;

public class PluginRepository(
    IMongoDBContext mongoDbContext,
    IApplicationContext applicationContext) :
    GenericRepository<Plugin>(mongoDbContext, applicationContext),
    IPluginRepository
{
    public async Task<IEnumerable<Plugin>> GetByPageId(Guid pageId, CancellationToken cancellationToken = default)
    {
        var filter = Builders<Plugin>.Filter.Eq(x => x.PageId, pageId);
        var result = await Collection.FindAsync(filter, cancellationToken: cancellationToken);
        return await result.ToListAsync(cancellationToken);
    }
}
