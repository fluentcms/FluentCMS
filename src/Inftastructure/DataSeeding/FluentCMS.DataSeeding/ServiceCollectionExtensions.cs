using FluentCMS.DataSeeding.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace FluentCMS.DataSeeding;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDataSeeder<TDataSeeder>(this IServiceCollection services) where TDataSeeder : class, IDataSeeder
    {
        services.AddLogging();

        services.AddScoped<IDataSeeder, TDataSeeder>();

        GetOrAddDataSeederRegistry(services).Add(typeof(TDataSeeder));

        return services;
    }

    public static IServiceCollection AddSchemaValidator<TSchemaValidator>(this IServiceCollection services) where TSchemaValidator : class, ISchemaValidator
    {
        services.AddLogging();

        services.AddScoped<ISchemaValidator, TSchemaValidator>();

        GetOrAddDataSchemaValidatorRegistry(services).Add(typeof(TSchemaValidator));

        return services;
    }

    public static IServiceCollection AddDataSeeders(this IServiceCollection services, Action<DataSeederOptions>? configure = null)
    {
        AddInternalHostedServices(services);

        // Configure options
        var opts = new DataSeederOptions();
        configure?.Invoke(opts);

        services.AddOptions<DataSeederOptions>()
            .Configure(o =>
            {
                o.Conditions = opts.Conditions;
                o.IgnoreExceptions = opts.IgnoreExceptions;
                o.Timeout = opts.Timeout;
            });

        return services;
    }

    public static IServiceCollection AddSchemaValidators(this IServiceCollection services, Action<SchemaValidatorOptions>? configure = null)
    {
        AddInternalHostedServices(services);

        // Configure options
        var opts = new SchemaValidatorOptions();
        configure?.Invoke(opts);

        services.AddOptions<SchemaValidatorOptions>()
            .Configure(o =>
            {
                o.Conditions = opts.Conditions;
                o.IgnoreExceptions = opts.IgnoreExceptions;
                o.Timeout = opts.Timeout;
            });

        return services;
    }

    private static DataSeederRegistry GetOrAddDataSeederRegistry(IServiceCollection services)
    {
        var existing = services
            .FirstOrDefault(d => d.ServiceType == typeof(DataSeederRegistry))
            ?.ImplementationInstance as DataSeederRegistry;

        if (existing is not null)
            return existing;

        var reg = new DataSeederRegistry();
        services.AddSingleton(reg);
        return reg;
    }

    private static SchemaValidatorRegistry GetOrAddDataSchemaValidatorRegistry(IServiceCollection services)
    {
        var existing = services
            .FirstOrDefault(d => d.ServiceType == typeof(SchemaValidatorRegistry))
            ?.ImplementationInstance as SchemaValidatorRegistry;

        if (existing is not null)
            return existing;

        var reg = new SchemaValidatorRegistry();
        services.AddSingleton(reg);
        return reg;
    }

    private static void AddInternalHostedServices(IServiceCollection services)
    {
        services.AddLogging();

        GetOrAddDataSeederRegistry(services);
        GetOrAddDataSchemaValidatorRegistry(services);

        services.TryAddScoped<SchemaValidatorService>();
        services.TryAddScoped<DataSeederService>();

        // Register the hosted service to seed the database at startup
        // Avoid multiple registration for multiple calls of AddDbOptions
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, DataSeedingHostedService>());
    }

}
