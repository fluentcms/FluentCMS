using FluentCMS.Entities.Identity;
using FluentCMS.Repositories.Identity.Abstractions;
using FluentCMS.Repositories.MongoDb;

namespace FluentCMS.Repositories.Identity.MongoDb;

public class MongoDbUserTokenRepository : MongoDbGenericRepository<UserToken>, IUserTokenRepository
{
    // TODO: add index
    public MongoDbUserTokenRepository(IMongoDBContext mongoDbContext) : base(mongoDbContext)
    {
    }
}