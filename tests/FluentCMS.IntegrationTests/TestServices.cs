using FluentCMS.Core;
using FluentCMS.Infrastructure;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.IntegrationTests;

internal static class TestServices
{
    public static ServiceProvider Build(
        IEnumerable<KeyValuePair<string, string?>> settings,
        Action<IServiceCollection>? configure = null,
        Action<IServiceCollection>? replace = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();

        services.AddLogging();
        // Test doubles go in first so a test clock wins over the TimeProvider Infrastructure adds with TryAdd.
        configure?.Invoke(services);
        services.AddFluentCmsCore();
        services.AddFluentCmsInfrastructure(configuration);

        // Runs last, to wrap or swap what Infrastructure registered.
        replace?.Invoke(services);

        return services.BuildServiceProvider();
    }

    public static async Task MigrateAsync(IServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<FluentCmsDbContext>().Database.MigrateAsync();
    }
}
