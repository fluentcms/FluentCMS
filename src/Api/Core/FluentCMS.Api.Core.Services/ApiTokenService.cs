namespace FluentCMS.Api.Core.Services;

public interface IApiTokenService
{
    Task<ApiToken> Add(ApiToken apiToken, CancellationToken cancellationToken = default);
    Task<ApiToken> Remove(Guid tokenId, CancellationToken cancellationToken = default);
    Task<IEnumerable<ApiToken>> GetAll(CancellationToken cancellationToken = default);
    Task<ApiToken> GetById(Guid tokenId, CancellationToken cancellationToken = default);
    Task<ApiToken> Update(ApiToken apiToken, CancellationToken cancellationToken = default);
    Task<ApiToken> RegenerateSecret(Guid id, CancellationToken cancellationToken = default);
    Task<ApiToken?> Validate(string apiKey, string apiSecret, CancellationToken cancellationToken = default);
}

internal class ApiTokenService(IApiTokenRepository apiTokenRepository, IEventPublisher eventPublisher, IOptions<ApiTokenOptions> options) : IApiTokenService
{
    private readonly ApiTokenOptions _options = options.Value;

    public async Task<ApiToken> Add(ApiToken apiToken, CancellationToken cancellationToken = default)
    {
        // remove policies with empty actions
        apiToken.Policies = [.. apiToken.Policies.Where(x => x.Actions.Count != 0)];

        var existingApiToken = await apiTokenRepository.GetByName(apiToken.Name, cancellationToken);
        if (existingApiToken != null)
            throw new EnhancedException(ExceptionCodes.ApiTokenNameIsDuplicated);

        apiToken.Key = GenerateKey();
        apiToken.Secret = GenerateSecret(apiToken.Key);

        await apiTokenRepository.Add(apiToken, cancellationToken);
        await eventPublisher.Publish(new ApiTokenAddedEvent(apiToken), cancellationToken);

        return apiToken;
    }

    public async Task<ApiToken> Update(ApiToken apiToken, CancellationToken cancellationToken = default)
    {
        var existingApiToken = await apiTokenRepository.GetById(apiToken.Id, cancellationToken);

        var isSameApiTokenExist = await apiTokenRepository.GetByName(apiToken.Name, cancellationToken);
        if (isSameApiTokenExist != null && apiToken.Id != isSameApiTokenExist.Id)
            throw new EnhancedException(ExceptionCodes.ApiTokenNameIsDuplicated);

        // remove policies with empty actions
        apiToken.Policies = [.. apiToken.Policies.Where(x => x.Actions.Count != 0)];

        //apiKey is not updated here as it should be generated automatically only
        apiToken.Secret = existingApiToken.Secret;
        apiToken.Key = existingApiToken.Key;

        await apiTokenRepository.Update(apiToken, cancellationToken);

        await eventPublisher.Publish(new ApiTokenUpdatedEvent(apiToken), cancellationToken);

        return apiToken;
    }

    public async Task<ApiToken> Remove(Guid tokenId, CancellationToken cancellationToken = default)
    {
        var deleted = await apiTokenRepository.Remove(tokenId, cancellationToken);

        await eventPublisher.Publish(new ApiTokenRemovedEvent(deleted), cancellationToken);

        return deleted;
    }

    public async Task<ApiToken?> Validate(string apiKey, string apiSecret, CancellationToken cancellationToken = default)
    {
        if (!Validate(apiKey, apiSecret))
            throw new EnhancedException(ExceptionCodes.ApiTokenInvalid);

        // there is not need to check permissions here
        // as this method is used for API authentication
        var apiToken = await apiTokenRepository.GetByKey(apiKey, cancellationToken) ??
            throw new EntityNotFoundException<ApiToken>();

        // check if token expired or not
        if (apiToken.ExpireAt.HasValue && apiToken.ExpireAt < DateTime.UtcNow)
            throw new EnhancedException(ExceptionCodes.ApiTokenExpired);

        // check if the token is active or not
        if (!apiToken.Enabled)
            throw new EnhancedException(ExceptionCodes.ApiTokenInactive);

        // check if the secret is valid or not
        if (apiToken.Secret != apiSecret)
            throw new EnhancedException(ExceptionCodes.ApiTokenInvalidSecret);

        return apiToken;
    }

    public async Task<IEnumerable<ApiToken>> GetAll(CancellationToken cancellationToken = default)
    {
        return await apiTokenRepository.GetAll(cancellationToken);
    }

    public async Task<ApiToken> GetById(Guid tokenId, CancellationToken cancellationToken = default)
    {
        return await apiTokenRepository.GetById(tokenId, cancellationToken);
    }

    public async Task<ApiToken> RegenerateSecret(Guid id, CancellationToken cancellationToken = default)
    {
        var apiToken = await apiTokenRepository.GetById(id, cancellationToken);

        apiToken.Secret = GenerateSecret(apiToken.Key);

        await apiTokenRepository.Update(apiToken, cancellationToken);

        await eventPublisher.Publish(new ApiTokenSecretRegeneratedEvent(apiToken), cancellationToken);

        return apiToken;
    }

    private static string GenerateKey()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    }

    private string GenerateSecret(string apiKey)
    {
        var signingKey = Encoding.ASCII.GetBytes(apiKey + "." + _options.Secret);
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Expires = DateTime.Now.AddYears(10),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(signingKey), SecurityAlgorithms.HmacSha256Signature)
        };

        var tokenHandler = new JwtSecurityTokenHandler();

        return tokenHandler.WriteToken(tokenHandler.CreateToken(tokenDescriptor));
    }

    private bool Validate(string apiKey, string secretKey)
    {
        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var signingKey = Encoding.ASCII.GetBytes(apiKey + "." + _options.Secret);
            tokenHandler.ValidateToken(secretKey, new TokenValidationParameters
            {
                IssuerSigningKey = new SymmetricSecurityKey(signingKey),
                ValidateIssuer = false,
                ValidateAudience = false,
            }, out SecurityToken validatedToken);

            return true;
        }
        catch (Exception)
        {
            return false;
        }

    }
}
