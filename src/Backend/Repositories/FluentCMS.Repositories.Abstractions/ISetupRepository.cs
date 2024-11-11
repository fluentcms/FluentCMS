namespace FluentCMS.Repositories.Abstractions;

public interface ISetupRepository
{
    Task<bool> Initialized(CancellationToken cancellationToken = default);
    Task<bool> InitializeDb(CancellationToken cancellationToken = default);
}
