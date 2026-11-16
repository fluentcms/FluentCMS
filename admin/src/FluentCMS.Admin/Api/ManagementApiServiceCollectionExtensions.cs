using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace FluentCMS.Admin.Api;

internal static class ManagementApiServiceCollectionExtensions
{
    // The sign-in, the shell and the notifications replace the defaults of SignedOutSession by registering their own.
    public static IServiceCollection AddManagementApi(this IServiceCollection services)
    {
        services.AddOptions<ManagementApiOptions>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<SignedOutSession>();
        services.TryAddScoped<IAccessTokenSource>(provider => provider.GetRequiredService<SignedOutSession>());
        services.TryAddScoped<ICurrentSite>(provider => provider.GetRequiredService<SignedOutSession>());
        services.TryAddScoped<IContentLocale>(provider => provider.GetRequiredService<SignedOutSession>());
        services.TryAddScoped<IRateLimitNotice>(provider => provider.GetRequiredService<SignedOutSession>());
        services.TryAddScoped<IManagementApi>(provider => new ManagementApi(CreateClient(provider)));
        services.TryAddScoped<ManagementClient>();
        services.TryAddScoped<SiteMetadata>();
        return services;
    }

    // Outermost first: the wait on a 429 resends through the headers, so a retry carries a fresh token.
    private static HttpClient CreateClient(IServiceProvider provider)
    {
        var options = provider.GetRequiredService<IOptions<ManagementApiOptions>>().Value;
        var headers = new SessionHeadersHandler(
            provider.GetRequiredService<IAccessTokenSource>(),
            provider.GetRequiredService<ICurrentSite>(),
            provider.GetRequiredService<IContentLocale>())
        {
            InnerHandler = options.PrimaryHandler?.Invoke() ?? new HttpClientHandler(),
        };
        var limits = new RateLimitHandler(provider.GetRequiredService<TimeProvider>(), provider.GetRequiredService<IRateLimitNotice>())
        {
            InnerHandler = headers,
        };

        return new HttpClient(limits) { BaseAddress = options.ManagementAddress() };
    }
}
