using FluentCMS.Web.ApiClients;
using Microsoft.Extensions.Configuration;
using System.Net.Http.Headers;
using System.Reflection;

namespace Microsoft.Extensions.DependencyInjection;

public static class ClientServiceExtensions
{
    public static IServiceCollection AddApiClients(this IServiceCollection services , IConfiguration configuration)
    {
        services.AddHttpClient("FluentCMS.Web.Api", client =>
        {
            client.BaseAddress = new Uri(configuration["BaseUrl"]?? "http://localhost:5000");
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

                var client = clientFactory.CreateClient("FluentCMS.Web.Api") ??
                    throw new InvalidOperationException($"Could not create HttpClient for {type.Name}");

                var ctor = type.GetConstructor([typeof(HttpClient)]) ??
                    throw new InvalidOperationException($"Could not find constructor for {type.Name}");

                var instance = ctor.Invoke(new[] { client });

                return instance;
            });
        }
        return services;
    }
}
