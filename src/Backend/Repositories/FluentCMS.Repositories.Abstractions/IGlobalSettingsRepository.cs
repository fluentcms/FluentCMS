namespace FluentCMS.Repositories.Abstractions;

public interface IGlobalSettingsRepository
{
    Task<GlobalSettings?> Get(CancellationToken cancellationToken = default);
    Task<GlobalSettings?> Update(GlobalSettings settings, CancellationToken cancellationToken = default);
}
