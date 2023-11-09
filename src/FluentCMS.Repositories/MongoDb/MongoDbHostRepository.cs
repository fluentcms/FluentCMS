using FluentCMS.Entities;
using FluentCMS.Repositories.Abstractions;

namespace FluentCMS.Repositories.MongoDb;

public class MongoDbHostRepository : MongoDbGenericRepository<Host>, IHostRepository
{
    public MongoDbHostRepository(IMongoDBContext mongoDbContext) : base(mongoDbContext)
    {
    }
}
