using FluentCMS.Web.UI.Services.Cookies;
using Microsoft.Extensions.Configuration;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Web;

namespace Microsoft.Extensions.DependencyInjection;

public static class ApiClientServiceExtensions
{
    public static IServiceCollection AddApiClients(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<HttpClientHeaderHandler>();

        services.AddHttpClient("FluentCMS.Web.Api", (sp, client) =>
        {
            client.BaseAddress = new Uri(configuration["urls"]);
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        }).AddHttpMessageHandler<HttpClientHeaderHandler>();

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
            var jsonData = HttpUtility.UrlDecode(cookie.Value);

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
