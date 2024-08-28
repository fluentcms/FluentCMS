namespace FluentCMS.Services;

public interface IProviderService : IAutoRegisterService
{
    Task<Provider> Create(Provider provider, CancellationToken cancellationToken = default);
    Task<IEnumerable<Provider>> GetAll(CancellationToken cancellationToken = default);
}

public class ProviderService(IProviderRepository providerRepository) : IProviderService
{
    public async Task<IEnumerable<Provider>> GetAll(CancellationToken cancellationToken = default)
    {
        return await providerRepository.GetAll(cancellationToken);
    }

    public async Task<Provider> Create(Provider provider, CancellationToken cancellationToken = default)
    {
        return await providerRepository.Create(provider, cancellationToken) ??
            throw new AppException(ExceptionCodes.ProviderUnableToCreate);
    }
}
