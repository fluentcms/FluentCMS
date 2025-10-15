namespace FluentCMS.Api.Core.Repositories.Abstractions;

public interface ISetupRepository
{
    Task<bool> IsInitialized(CancellationToken cancellationToken = default);
    Task<bool> SetInitialized(CancellationToken cancellationToken = default);
}
