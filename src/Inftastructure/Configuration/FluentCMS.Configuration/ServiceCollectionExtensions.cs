using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using System.Text.Json;

namespace FluentCMS.Configuration;

public static class ServiceCollectionExtensions
{
    private static readonly JsonSerializerOptions jsonSerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static IServiceCollection AddDbOptions<TOptions>(this IServiceCollection services, IConfiguration configuration, string sectionName, bool seedData = true) where TOptions : class, new()
    {
        // Validate inputs
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);

        services.Configure<ProviderCatalogOptions>(options =>
        {
            if (seedData)
            {
                var regType = typeof(TOptions);
                var configSection = configuration.GetSection(sectionName);

                // Get bound configuration or create default instance if section doesn't exist
                var bound = configSection.Exists() ? configSection.Get<TOptions>() : new TOptions();
                var defaultValue = JsonSerializer.Serialize(bound, regType, jsonSerializerOptions);
                var registration = new OptionRegistration(sectionName, typeof(TOptions), defaultValue);
                options.Types.Add(registration);
            }
        });

        services.TryAddSingleton<IOptionsCatalog, OptionsCatalog>();

        services.AddOptions<TOptions>()
            .Bind(configuration.GetSection(sectionName)) // Use GetSection instead of GetRequiredSection to avoid throwing
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Register the hosted service to seed the database at startup
        // Avoid multiple registration for multiple calls of AddDbOptions
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, OptionsDbSeeder>());

        return services;
    }
}
