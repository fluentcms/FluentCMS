﻿using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using System.Net.Http.Headers;

namespace Microsoft.Extensions.DependencyInjection;

public static class ClientServiceExtensions
{
    public static IServiceCollection AddApiClients(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient("FluentCMS.Web.Api", (sp, client) =>
        {
            // TODO: we should read this from somewhere else
            client.BaseAddress = new Uri(configuration["urls"]);
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            // read access token from local storage
            //var authStateProvider = sp.GetRequiredService<AuthenticationStateProvider>();
            //var token = authStateProvider.GetUserToken().GetAwaiter().GetResult();

            //if (!string.IsNullOrEmpty(token))
            //    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("bearer", token);
        });

        services.AddScoped<HttpClient>(
            x => x.GetRequiredService<IHttpClientFactory>().CreateClient("FluentCMS.Web.Api"));

        var baseType = typeof(IApiClient);
        var assembly = AppDomain.CurrentDomain.GetAssemblies().
           Single(assembly => assembly.GetName().Name == "FluentCMS.Web.ApiClients");

        var derivedTypes = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && baseType.IsAssignableFrom(t) && t.Namespace == baseType.Namespace);

        foreach (var type in derivedTypes)
        {
            services.AddScoped(type, sp =>
            {
                //var clientFactory = sp.GetRequiredService<IHttpClientFactory>();

                //var client = clientFactory.CreateClient("FluentCMS.Web.Api") ??
                //    throw new InvalidOperationException($"Could not create HttpClient for {type.Name}");

                var client = sp.GetRequiredService<HttpClient>();

                var ctor = type.GetConstructor([typeof(HttpClient)]) ??
                    throw new InvalidOperationException($"Could not find constructor for {type.Name}");

                var instance = ctor.Invoke([client]);

                return instance;
            });
        }
        return services;
    }
}