using FluentCMS.Providers.ApiTokenProviders;
using FluentCMS.Providers.MessageBusProviders;

namespace FluentCMS.Services;

public interface IApiTokenService : IAutoRegisterService
{
    Task<ApiToken> Create(ApiToken apiToken, CancellationToken cancellationToken = default);
    Task<ApiToken> Delete(Guid tokenId, CancellationToken cancellationToken = default);
    Task<IEnumerable<ApiToken>> GetAll(CancellationToken cancellationToken = default);
    Task<ApiToken?> GetById(Guid tokenId, CancellationToken cancellationToken = default);
    Task<ApiToken> Update(ApiToken apiToken, CancellationToken cancellationToken = default);
    Task<ApiToken> RegenerateSecret(Guid id, CancellationToken cancellationToken = default);
    Task<ApiToken> Validate(string apiKey, string apiSecret, CancellationToken cancellationToken = default);
}

public class ApiTokenService(IApiTokenRepository apiTokenRepository, IApiTokenProvider apiTokenProvider, IMessagePublisher messagePublisher) : IApiTokenService
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
        var existingApiToken = await apiTokenRepository.GetByName(apiToken.Name, cancellationToken);
        if (existingApiToken != null)
            throw new AppException(ExceptionCodes.ApiTokenNameIsDuplicated);

        apiToken.Key = apiTokenProvider.GenerateKey();
        apiToken.Secret = apiTokenProvider.GenerateSecret(apiToken.Key);

        apiToken = await apiTokenRepository.Create(apiToken, cancellationToken) ??
            throw new AppException(ExceptionCodes.ApiTokenUnableToCreate);

        await messagePublisher.Publish(new Message<ApiToken>(ActionNames.ApiTokenCreated, apiToken), cancellationToken);

        return apiToken;
    }

    public async Task<ApiToken> Update(ApiToken apiToken, CancellationToken cancellationToken = default)
    {
        var existingApiToken = await apiTokenRepository.GetById(apiToken.Id, cancellationToken) ??
            throw new AppException(ExceptionCodes.ApiTokenNotFound);

        var isSameApiTokenExist = await apiTokenRepository.GetByName(apiToken.Name, cancellationToken);
        if (isSameApiTokenExist != null && apiToken.Id != isSameApiTokenExist.Id)
            throw new AppException(ExceptionCodes.ApiTokenNameIsDuplicated);

        //apiKey is not updated here as it should be generated automatically only
        apiToken.Secret = existingApiToken.Secret;
        apiToken.Key = existingApiToken.Key;

        var updated = await apiTokenRepository.Update(apiToken, cancellationToken) ??
            throw new AppException(ExceptionCodes.ApiTokenUnableToUpdate);

        await messagePublisher.Publish(new Message<ApiToken>(ActionNames.ApiTokenUpdated, updated), cancellationToken);

        return updated;
    }

    public async Task<ApiToken> Delete(Guid tokenId, CancellationToken cancellationToken = default)
    {
        var deleted = await apiTokenRepository.Delete(tokenId, cancellationToken) ??
            throw new AppException(ExceptionCodes.ApiTokenUnableToDelete);

        await messagePublisher.Publish(new Message<ApiToken>(ActionNames.ApiTokenDeleted, deleted), cancellationToken);

        return deleted;
    }

    public async Task<ApiToken> Validate(string apiKey, string apiSecret, CancellationToken cancellationToken = default)
    {
        var apiToken = await apiTokenRepository.GetByKey(apiKey, cancellationToken) ??
            throw new AppException(ExceptionCodes.ApiTokenNotFound);

        // check if token expired or not
        if (apiToken.ExpireAt.HasValue && apiToken.ExpireAt < DateTime.UtcNow)
            throw new AppException(ExceptionCodes.ApiTokenExpired);

        // check if the token is active or not
        if (!apiToken.Enabled)
            throw new AppException(ExceptionCodes.ApiTokenInactive);

        // check if the secret is valid or not
        if (apiToken.Secret != apiSecret)
            throw new AppException(ExceptionCodes.ApiTokenInvalidSecret);

        return apiToken;
    }

    public async Task<ApiToken> RegenerateSecret(Guid id, CancellationToken cancellationToken = default)
    {
        var apiToken = await apiTokenRepository.GetById(id, cancellationToken) ??
            throw new AppException(ExceptionCodes.ApiTokenNotFound);

        apiToken.Secret = apiTokenProvider.GenerateSecret(apiToken.Key);

        var updated = await apiTokenRepository.Update(apiToken, cancellationToken) ??
            throw new AppException(ExceptionCodes.ApiTokenUnableToUpdate);

        await messagePublisher.Publish(new Message<ApiToken>(ActionNames.ApiTokenUpdated, updated), cancellationToken);

        return updated;
    }
}
