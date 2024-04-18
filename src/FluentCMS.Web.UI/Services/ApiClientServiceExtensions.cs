using FluentCMS.Web.UI.Services.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Microsoft.Extensions.DependencyInjection;

public static class ApiClientServiceExtensions
{
    public static IServiceCollection AddApiClients(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddTransient<HttpClientHeaderHandler>();

        services.AddHttpClient("FluentCMS.Web.Api", (sp, client) =>
        {
            client.BaseAddress = new Uri(configuration["urls"]);
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        }).AddHttpMessageHandler<HttpClientHeaderHandler>();

        var baseType = typeof(IApiClient);
        var assembly = baseType.Assembly;

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

                var instance = ctor.Invoke([client]);

                return instance;
            });
        }
        return services;
    }
}

public class HttpClientHeaderHandler(ICookieService cookieService) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // read auth header from cookie and set to the api client's header
        var cookie = cookieService.GetAsync(nameof(UserLoginResponse)).GetAwaiter().GetResult();

        if (cookie != null && !string.IsNullOrEmpty(cookie.Value))
        {
            var jsonData = Encoding.UTF8.GetString(Convert.FromBase64String(cookie.Value));

            var loginResponse = JsonSerializer.Deserialize<UserLoginResponse>(jsonData);
            if (loginResponse != null)
            {
                var token = loginResponse.Token;
                request.Headers.Authorization = new AuthenticationHeaderValue("bearer", token);
            }
        }

        return base.SendAsync(request, cancellationToken);
    }
}
