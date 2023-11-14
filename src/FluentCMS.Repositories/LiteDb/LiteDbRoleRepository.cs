using FluentCMS.Entities;
using FluentCMS.Repositories.Abstractions;

namespace FluentCMS.Repositories.LiteDb;

internal class LiteDbRoleRepository : LiteDbGenericRepository<Role>, IRoleRepository
{
    public LiteDbRoleRepository(LiteDbContext dbContext) : base(dbContext)
    {
    }

    public IQueryable<Role> AsQueryable()
    {
        throw new NotImplementedException();
    }

    public Task<Role?> FindByName(string normalizedRoleName, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}
