using FluentCMS.Entities;

namespace FluentCMS.Repository.LiteDb;

internal class LiteDbUserRepository : LiteDbGenericRepository<User>, IUserRepository
{
    public LiteDbUserRepository(LiteDbContext dbContext) : base(dbContext)
    {
    }

    public async Task<User?> GetByUsername(string username)
    {
        var data = await this.Collection.FindOneAsync(X => X.Username == username);
        return data;
    }
}
