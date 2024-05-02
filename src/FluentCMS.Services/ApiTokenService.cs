namespace FluentCMS.Services;

public interface IApiTokenService
{
    Task<ApiToken> Create(ApiToken apiToken, CancellationToken cancellationToken = default);
    Task<ApiToken> Delete(Guid tokenId, CancellationToken cancellationToken = default);
    Task<IEnumerable<ApiToken>> GetAll(CancellationToken cancellationToken = default);
    Task<ApiToken?> GetById(Guid tokenId, CancellationToken cancellationToken = default);
    Task<ApiToken> Update(Guid tokenId, string name, string? description, bool enabled, List<Policy> policies, CancellationToken cancellationToken = default);
}

public class ApiTokenService(IApiTokenRepository apiTokenRepository) : IApiTokenService
{
    public async Task<IEnumerable<ApiToken>> GetAll(CancellationToken cancellationToken = default)
    {
        return await apiTokenRepository.GetAll(cancellationToken);
    }

    public async Task<ApiToken?> GetById(Guid tokenId, CancellationToken cancellationToken = default)
    {
        return await apiTokenRepository.GetById(tokenId, cancellationToken);
    }

    public async Task<ApiToken> Create(ApiToken apiToken, CancellationToken cancellationToken = default)
    {
        return await apiTokenRepository.Create(apiToken, cancellationToken) ??
            throw new AppException(ExceptionCodes.ApiTokenUnableToCreate);
    }

    public async Task<ApiToken> Update(Guid tokenId, string name, string? description, bool enabled, List<Policy> policies, CancellationToken cancellationToken = default)
    {
        var apiToken = await apiTokenRepository.GetById(tokenId, cancellationToken) ??
            throw new AppException(ExceptionCodes.ApiTokenNotFound);

        apiToken.Name = name;
        apiToken.Description = description;
        apiToken.Enabled = enabled;
        apiToken.Policies = policies;

        return await apiTokenRepository.Update(apiToken, cancellationToken) ??
            throw new AppException(ExceptionCodes.ApiTokenUnableToUpdate);
    }

    public async Task<ApiToken> Delete(Guid tokenId, CancellationToken cancellationToken = default)
    {
        _ = await apiTokenRepository.GetById(tokenId, cancellationToken) ??
            throw new AppException(ExceptionCodes.ApiTokenNotFound);

        return await apiTokenRepository.Delete(tokenId, cancellationToken) ??
            throw new AppException(ExceptionCodes.ApiTokenUnableToDelete);
    }
}
