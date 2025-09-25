namespace FluentCMS.Providers.Repositories.Abstractions;

public interface IProviderRepository
{
    Task AddMany(IEnumerable<Provider> providers, CancellationToken cancellationToken = default);
    Task Update(Provider provider, CancellationToken cancellationToken = default);
    Task Remove(Provider provider, CancellationToken cancellationToken = default);
    Task<IEnumerable<Provider>> GetAll(CancellationToken cancellationToken = default);
}