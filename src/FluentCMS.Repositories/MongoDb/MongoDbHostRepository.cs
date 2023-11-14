using FluentCMS.Entities;
using FluentCMS.Repositories.Abstractions;

namespace FluentCMS.Repositories.MongoDb;

public class MongoDbHostRepository : MongoDbGenericRepository<Host>, IHostRepository
{
    public MongoDbHostRepository(IMongoDBContext mongoDbContext) : base(mongoDbContext)
    {
    }

    public async Task<Host> Get(CancellationToken cancellationToken = default)
    {
        var hosts = await GetAll(cancellationToken);
        return hosts.Single();
    }
}
