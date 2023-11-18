using FluentCMS.Entities;
using LiteDB.Queryable;

namespace FluentCMS.Repositories.LiteDb;

internal class RoleRepository : GenericRepository<Role>, IRoleRepository
{
    public RoleRepository(LiteDbContext dbContext, IApplicationContext applicationContext) : base(dbContext, applicationContext)
    {
    }

    public IQueryable<Role> AsQueryable()
    {
        return Collection.AsQueryable();
    }

    public async Task<Role?> FindByName(string normalizedRoleName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await Collection.FindOneAsync(x => x.NormalizedName == normalizedRoleName);
    }
}
