using FluentCMS.Web.ApiClients;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Claims;

namespace Microsoft.Extensions.DependencyInjection;

public static class ClientServiceExtensions
{
    public static IServiceCollection AddApiClients(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient("FluentCMS.Web.Api", (sp, client) =>
        {
            client.BaseAddress = new Uri(configuration["urls"]);
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            //set auth header
            var httpContextAccessor = sp.GetService<IHttpContextAccessor>();
            var httpContext = httpContextAccessor?.HttpContext;

            if (httpContext != null && httpContext.User.FindFirstValue("token") is var jwt && !string.IsNullOrEmpty(jwt))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("bearer", jwt);
            }
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
