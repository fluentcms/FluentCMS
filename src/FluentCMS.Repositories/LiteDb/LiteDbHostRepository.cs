using FluentCMS.Entities;
using FluentCMS.Repositories.Abstractions;

namespace FluentCMS.Repositories.LiteDb;

public class LiteDbHostRepository : LiteDbGenericRepository<Host>, IHostRepository
{
    public LiteDbHostRepository(LiteDbContext dbContext) : base(dbContext)
    {
    }

    public Task<Host> Get(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}
