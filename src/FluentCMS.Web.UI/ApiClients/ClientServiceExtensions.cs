using Blazored.LocalStorage;
using FluentCMS.Web.UI.ApiClients;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;

namespace Microsoft.Extensions.DependencyInjection;

public static class ClientServiceExtensions
{
    public static IServiceCollection AddApiClients(this IServiceCollection services)
    {
        services.AddHttpClient("FluentCMS.Web.Api", client =>
        {
            // TODO: Move this to configuration
            client.BaseAddress = new Uri("https://localhost:7164");
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        });

        var assembly = Assembly.GetExecutingAssembly();
        var baseType = typeof(IApiClient);

        var derivedTypes = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && baseType.IsAssignableFrom(t) && t.Namespace == baseType.Namespace);

        foreach (var type in derivedTypes)
        {
            services.AddScoped(type, sp =>
            {
                var clientFactory = sp.GetRequiredService<IHttpClientFactory>();
                var httpClient = clientFactory.CreateClient("FluentCMS.Web.Api");
                var localStorageService = sp.GetRequiredService<ILocalStorageService>();
                //var jwtToken = localStorageService.GetItemAsync<string?>("jwt-access-token").Result;

                //if (!string.IsNullOrEmpty(jwtToken))
                //    httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwtToken);

                var ctor = type.GetConstructor([typeof(HttpClient)]);
                var instance = ctor.Invoke(new[] { httpClient });
                return instance;
            });
        }
        return services;
    }
}
