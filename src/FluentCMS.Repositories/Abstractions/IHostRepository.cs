using FluentCMS.Entities;

namespace FluentCMS.Repositories;

public interface IHostRepository
{
    Task<Host?> Get(CancellationToken cancellationToken = default);
    Task<Host?> Update(Host host, CancellationToken cancellationToken = default);
    Task<Host?> Create(Host host, CancellationToken cancellationToken = default);
}
