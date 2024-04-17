using FluentCMS.Web.ApiClients;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Web;
using BitzArt.Blazor.Cookies;

namespace Microsoft.Extensions.DependencyInjection;

public static class ClientServiceExtensions
{
    public static IServiceCollection AddApiClients(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddTransient<HttpClientHeaderHandler>();
        services.AddHttpClient("FluentCMS.Web.Api", (sp, client) =>
        {
            client.BaseAddress = new Uri(configuration["urls"]);
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            //set auth header

            // http context
            //if (sp.GetService<IHttpContextAccessor>() is { HttpContext: { } httpContext } && httpContext.Request.Cookies.ContainsKey("UserLoginResponse"))
            //{
            //    var loginResponse = JsonSerializer.Deserialize<UserLoginResponse>(httpContext.Request.Cookies["UserLoginResponse"] ?? throw new Exception("Cookie 'UserLoginResponse' is null!")) ?? throw new Exception("Unable to deserialize UserLoginResponse");
            //    var token = loginResponse.Token;
            //    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("bearer", token);
            //}

            //service
            //if (sp.GetService<ICookieService>() is { } cookieService &&
            //    cookieService.GetAsync(nameof(UserLoginResponse)).GetAwaiter().GetResult() is { } cookie &&
            //    JsonSerializer.Deserialize<UserLoginResponse>(HttpUtility.UrlDecode(cookie.Value)) is { } response)
            //{
            //    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", response.Token);
            //}


        }).AddHttpMessageHandler<HttpClientHeaderHandler>();

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

public class HttpClientHeaderHandler(ICookieService cookieService):DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (cookieService.GetAsync(nameof(UserLoginResponse)).GetAwaiter().GetResult() is { } cookie &&
            JsonSerializer.Deserialize<UserLoginResponse>(HttpUtility.UrlDecode(cookie.Value)) is { } response)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", response.Token);
        }
        return base.SendAsync(request, cancellationToken);
    }
}
