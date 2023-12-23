using FluentCMS.Entities;

namespace FluentCMS.Repositories;

public interface IGlobalSettingsRepository
{
    Task<GlobalSettings?> Get(CancellationToken cancellationToken = default);
    Task<GlobalSettings?> Update(GlobalSettings settings, CancellationToken cancellationToken = default);
    Task<bool> Reset(CancellationToken cancellationToken = default);
}
