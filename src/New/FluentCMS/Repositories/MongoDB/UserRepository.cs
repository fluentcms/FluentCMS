using FluentCMS.Entities;
using MongoDB.Driver;

namespace FluentCMS.Repositories.MongoDB;

public class UserRepository : AuditableEntityRepository<User>, IUserRepository
{
    public UserRepository(IMongoDBContext mongoDbContext, IApplicationContext applicationContext) : base(mongoDbContext, applicationContext)
    {
    }

    public IQueryable<User> AsQueryable()
    {
        return Collection.AsQueryable();
    }
}
