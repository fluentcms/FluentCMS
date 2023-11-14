using FluentCMS.Entities;

namespace FluentCMS.Repositories.Abstractions;

public interface IHostRepository : IGenericRepository<Host>
{
    Task<Host> Get(CancellationToken cancellationToken = default);
}
