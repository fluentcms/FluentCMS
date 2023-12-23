using FluentCMS.E2eTests.ApiClients;
using Microsoft.Extensions.Configuration;
using System.Net.Http.Headers;
using System.Reflection;

namespace Microsoft.Extensions.DependencyInjection;

public static class ClientServiceExtensions
{
    public static IServiceCollection AddApiClients(this IServiceCollection services, IConfiguration configuration)
    {
        var baseUrl = configuration["BaseUrl"];
        services.AddHttpClient("FluentCMS.Web.Api", client =>
        {
            // TODO: Move this to configuration
            client.BaseAddress = new Uri(baseUrl);
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
                var client = clientFactory.CreateClient("FluentCMS.Web.Api");
                var ctor = type.GetConstructor([typeof(HttpClient)]);
                var instance = ctor.Invoke(new[] { client });
                return instance;
            });
        }
        return services;
    }
}
