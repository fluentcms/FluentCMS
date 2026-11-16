using FluentCMS.Admin.Api;
using FluentCMS.Admin.Configuration;
using FluentCMS.Admin.Forms;
using FluentCMS.Admin.Interop;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace FluentCMS.Admin.Authentication;

internal static class AuthenticationServiceCollectionExtensions
{
    // Before AddManagementApi, whose signed-out token source this replaces.
    public static IServiceCollection AddAdminAuthentication(this IServiceCollection services)
    {
        services.AddOptions<AuthorityOptions>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<ISessionStorage, SessionStorageInterop>();
        services.TryAddScoped(provider =>
        {
            var options = provider.GetRequiredService<IOptions<AuthorityOptions>>().Value;
            return new AuthorityClient(
                provider.GetRequiredService<AdminConfiguration>(),
                new HttpClient(options.PrimaryHandler?.Invoke() ?? new HttpClientHandler()) { Timeout = options.Timeout });
        });
        services.TryAddScoped<FormDrafts>();
        services.TryAddScoped<IFormDraftStore, FormDraftStore>();
        services.TryAddScoped<AdminSession>();
        services.TryAddScoped<IAdminSession>(provider => provider.GetRequiredService<AdminSession>());
        services.TryAddScoped<IAccessTokenSource>(provider => provider.GetRequiredService<AdminSession>());
        return services;
    }
}
