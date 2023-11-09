using Microsoft.AspNetCore.Identity;
using uBeac.Identity;

namespace Microsoft.Extensions.DependencyInjection;

public static class UnitTypeExtensions
{
    public static IServiceCollection AddUnitTypeService<TUnitTypeService, TUnitTypeKey, TUnitType>(this IServiceCollection services)
        where TUnitTypeKey : IEquatable<TUnitTypeKey>
        where TUnitType : UnitType<TUnitTypeKey>
        where TUnitTypeService : class, IUnitTypeService<TUnitTypeKey, TUnitType>
    {
        services.AddScoped<IUnitTypeService<TUnitTypeKey, TUnitType>, TUnitTypeService>();
        return services;
    }

    public static IServiceCollection AddUnitTypeService<TUnitTypeService, TUnitType>(this IServiceCollection services)
        where TUnitType : UnitType
        where TUnitTypeService : class, IUnitTypeService<TUnitType>
    {
        services.AddScoped<IUnitTypeService<TUnitType>, TUnitTypeService>();
        return services;
    }

    public static IdentityBuilder AddIdentityUnitType<TUnitTypeKey, TUnitType>(this IdentityBuilder builder, Action<UnitTypeOptions<TUnitTypeKey, TUnitType>> configureOptions = default)
        where TUnitTypeKey : IEquatable<TUnitTypeKey>
        where TUnitType : UnitType<TUnitTypeKey>
    {
        if (configureOptions is not null)
        {
            // Register options
            var options = builder.Services.RegisterUnitTypeOptions(configureOptions);

            // Insert default values
            builder.Services.BuildServiceProvider().CreateScope().ServiceProvider
                .GetRequiredService<IUnitTypeService<TUnitTypeKey, TUnitType>>()
                .InsertDefaultUnitTypes(options.DefaultValues);
        }

        return builder;
    }

    public static IdentityBuilder AddIdentityUnitType<TUnitType>(this IdentityBuilder builder, Action<UnitTypeOptions<TUnitType>> configureOptions = default)
        where TUnitType : UnitType
    {
        if (configureOptions is not null)
        {
            // Register options
            var options = builder.Services.RegisterUnitTypeOptions(configureOptions);

            // Insert default values
            builder.Services.BuildServiceProvider().CreateScope().ServiceProvider
                .GetRequiredService<IUnitTypeService<TUnitType>>()
                .InsertDefaultUnitTypes(options.DefaultValues);
        }

        return builder;
    }

    private static UnitTypeOptions<TUnitTypeKey, TUnitType> RegisterUnitTypeOptions<TUnitTypeKey, TUnitType>(this IServiceCollection services, Action<UnitTypeOptions<TUnitTypeKey, TUnitType>> configureOptions)
        where TUnitTypeKey : IEquatable<TUnitTypeKey>
        where TUnitType : UnitType<TUnitTypeKey>
    {
        // Register IOptions<UnitTypeOptions<,>>
        services.Configure(configureOptions);

        // Get UnitTypeOptions<,> from ServiceProvider
        var options = services.BuildServiceProvider().GetRequiredService<Options.IOptions<UnitTypeOptions<TUnitTypeKey, TUnitType>>>().Value;

        // Register UnitTypeOptions<,> without IOptions
        services.AddSingleton<UnitTypeOptions<TUnitTypeKey, TUnitType>>(options);

        return options;
    }

    private static UnitTypeOptions<TUnitType> RegisterUnitTypeOptions<TUnitType>(this IServiceCollection services, Action<UnitTypeOptions<TUnitType>> configureOptions)
        where TUnitType : UnitType
    {
        // Register IOptions<UnitTypeOptions<,>>
        services.Configure(configureOptions);

        // Get UnitTypeOptions<,> from ServiceProvider
        var options = services.BuildServiceProvider().GetRequiredService<Options.IOptions<UnitTypeOptions<TUnitType>>>().Value;

        // Register UnitTypeOptions<,> without IOptions
        services.AddSingleton<UnitTypeOptions<TUnitType>>(options);

        return options;
    }

    private static void InsertDefaultUnitTypes<TUnitTypeKey, TUnitType>(this IUnitTypeService<TUnitTypeKey, TUnitType> service, IEnumerable<TUnitType> values)
        where TUnitTypeKey : IEquatable<TUnitTypeKey>
        where TUnitType : UnitType<TUnitTypeKey>
    {
        if (values is null || values.Any() is false) return;
        foreach (var unitType in values)
        {
            try
            {
                // If unit type was not inserted before, insert it
                if (service.Exists(unitType.Code).Result is false)
                {
                    service.Create(unitType).Wait();
                }
            }
            catch (Exception)
            {
                // ignored
            }
        }
    }

    private static void InsertDefaultUnitTypes<TUnitType>(this IUnitTypeService<TUnitType> service, IEnumerable<TUnitType> values)
        where TUnitType : UnitType
    {
        service.InsertDefaultUnitTypes<Guid, TUnitType>(values);
    }
}