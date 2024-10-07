namespace FluentCMS.Repositories.Abstractions;

public interface ISettingsRepository
{
    Task<Settings?> GetById(Guid entityId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Settings>> GetByIds(IEnumerable<Guid> entityIds, CancellationToken cancellationToken = default);
    Task<IEnumerable<Settings>> GetAll(CancellationToken cancellationToken = default);
    Task<Settings?> Update(Guid entityId, Dictionary<string, string> settings, CancellationToken cancellationToken = default);
}
