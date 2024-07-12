using FluentCMS.Web.ApiClients;
using FluentCMS.Web.ApiClients.Services;
using Microsoft.Extensions.Configuration;
using System.Net.Http.Headers;
using System.Reflection;

namespace Microsoft.Extensions.DependencyInjection;

public static class ApiClientServiceExtensions
{
    public const string HTTP_CLIENT_API_NAME = "FluentCMS.Web.Api";

    public static IServiceCollection AddApiClients(this IServiceCollection services, IConfiguration configuration)
    {
        // TODO: move this to plugins projects
        services.AddAutoMapper(typeof(MappingProfile));

        // TODO: these DI registration should be done automatically inside plugins
        services.AddScoped<SetupManager>();
        services.AddScoped<AuthManager>();

        services.AddScoped<ApiClientFactory>();

        services.AddHttpClient(HTTP_CLIENT_API_NAME, (sp, client) =>
        {
            string apiServer = configuration?["ApiServer"] ??
                               throw new InvalidOperationException("ApiServer is not configured in appsettings.json");

            client.BaseAddress = new Uri(apiServer);
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        });

        // find all interfaces that inherit from IApiClient
        var apiClientTypes = Assembly.GetAssembly(typeof(IApiClient))?.GetTypes()
            .Where(t => t.IsInterface && typeof(IApiClient).IsAssignableFrom(t));

        // register all IApiClient interfaces with their implementation
        foreach (var apiClientType in apiClientTypes ?? [])
        {
            // find the implementation of the interface
            var implementationType = Assembly.GetAssembly(apiClientType)?.GetTypes()
                .FirstOrDefault(t => apiClientType.IsAssignableFrom(t) && !t.IsInterface);

            // register the interface with the implementation
            if (implementationType != null)
                services.AddScoped(apiClientType, sp =>
                {
                    var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HTTP_CLIENT_API_NAME);

                    var userLogin = sp.GetRequiredService<UserLoginResponse>();

                    if (!string.IsNullOrEmpty(userLogin?.Token))
                        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userLogin.Token);

                    var ctor = implementationType.GetConstructor([typeof(HttpClient)]) ??
                               throw new InvalidOperationException($"Could not find constructor for {implementationType.Name}");

                    return ctor.Invoke([httpClient]);
                });
        }
        return services;
    }
}
