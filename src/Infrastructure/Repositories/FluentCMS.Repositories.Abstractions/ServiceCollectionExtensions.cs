using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Repositories.Abstractions;

// IServiceCollection extension
public static class ServiceCollectionExtensions
{
    // Add database manager to DI container
    public static IServiceCollection AddDatabaseManager(this IServiceCollection services, Action<IDatabaseManagerBuilder> configure)
    {
        var builder = new DatabaseManagerBuilder();
        configure(builder);
        var manager = builder.Build();

        // Register the manager
        services.AddSingleton(manager);

        // Register the data initializer
        services.AddScoped<IDataInitializer, DataInitializer>();

        // Register factory for IDataContext<TArea>
        // Libraries will register their own like this:
        // services.AddScoped<IDataContext<ITodoDatabase>>(sp => sp.GetRequiredService<IDatabaseManager>().CreateDataContextForArea<ITodoDatabase>());

        return services;
    }

    // Helper for libraries to register their area (this is in Abstractions)
    public static IServiceCollection AddDataContextForArea<TArea>(this IServiceCollection services) where TArea : IDatabaseArea
    {
        services.AddScoped(sp =>
            sp.GetRequiredService<IDatabaseManager>().CreateDataContextForArea<TArea>());
        return services;
    }

    // Helper for libraries to register their data seeder (this is in Abstractions)
    public static IServiceCollection AddDataSeeder<TDataSeeder, TArea>(this IServiceCollection services)
        where TDataSeeder : class, IDataSeeder<TArea>
        where TArea : IDatabaseArea
    {
        services.AddScoped<IDataSeeder<TArea>, TDataSeeder>();
        services.AddScoped<IDataSeeder, TDataSeeder>();
        return services;
    }
}
