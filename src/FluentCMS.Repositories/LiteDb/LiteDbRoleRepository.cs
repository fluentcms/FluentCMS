using FluentCMS.Entities;
using FluentCMS.Repositories.Abstractions;
using LiteDB.Queryable;

namespace FluentCMS.Repositories.LiteDb;

internal class LiteDbRoleRepository : LiteDbGenericRepository<Role>, IRoleRepository
{
    public LiteDbRoleRepository(LiteDbContext dbContext) : base(dbContext)
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
