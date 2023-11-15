using FluentCMS.Entities;
using FluentCMS.Repositories.Abstractions;

namespace FluentCMS.Repositories.LiteDb;

public class LiteDbHostRepository : IHostRepository
{
    public LiteDbHostRepository(LiteDbContext dbContext)
    {
    }

    public Task<Host?> Create(Host host, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<Host?> Get(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<Host?> Update(Host host, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}
