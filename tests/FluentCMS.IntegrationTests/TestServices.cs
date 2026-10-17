using FluentCMS.Infrastructure;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.IntegrationTests;

internal static class TestServices
{
    public static ServiceProvider Build(IEnumerable<KeyValuePair<string, string?>> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddFluentCmsInfrastructure(configuration);

        return services.BuildServiceProvider();
    }

    public static async Task MigrateAsync(IServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<FluentCmsDbContext>().Database.MigrateAsync();
    }
}
