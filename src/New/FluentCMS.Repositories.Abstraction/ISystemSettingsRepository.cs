using FluentCMS.Entities;

namespace FluentCMS.Repositories;

public interface ISystemSettingsRepository
{
    Task<SystemSettings?> Get(CancellationToken cancellationToken = default);
    Task<SystemSettings?> Update(SystemSettings systemSettings, CancellationToken cancellationToken = default);
    Task<SystemSettings?> Create(SystemSettings systemSettings, CancellationToken cancellationToken = default);
}
