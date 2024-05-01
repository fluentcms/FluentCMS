using System.Net.Http.Headers;
using FluentCMS.Web.ApiClients;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Extensions.DependencyInjection;

public static class ApiClientServiceExtensions
{
    public static IServiceCollection AddApiClients(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient(HttpClientFactoryHelper.HTTP_CLIENT_API_NAME, (sp, client) =>
        {
            client.BaseAddress = new Uri(configuration["urls"]);
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        });
        return services;
    }
}
