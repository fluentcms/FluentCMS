using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace FluentCMS.E2eTests.Extensions;

public static class DependencyExtensions
{
    // Configure Services for Tests
    internal static IServiceCollection ConfigureServices(this IServiceCollection services)
    {
        // setup Configuration
        var configuration = BuildConfiguration();
        services.AddSingleton(configuration);

        // setup Clients
        services.AddApiClients(configuration);

        return services;
    }

    // Build Configuration for Tests
    internal static IConfiguration BuildConfiguration()
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddEnvironmentVariables()
            .AddJsonFile("appsettings.json")
            .AddJsonFile("appsettings.Test.json", true)
            .Build();

        return config;
    }
}
