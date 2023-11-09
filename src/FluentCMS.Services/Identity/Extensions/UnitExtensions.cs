using Microsoft.AspNetCore.Identity;
using uBeac.Identity;

namespace Microsoft.Extensions.DependencyInjection;

public static class UnitExtensions
{
    public static IServiceCollection AddUnitService<TUnitService, TUnitKey, TUnit>(this IServiceCollection services)
        where TUnitKey : IEquatable<TUnitKey>
        where TUnit : Unit<TUnitKey>
        where TUnitService : class, IUnitService<TUnitKey, TUnit>
    {
        services.AddScoped<IUnitService<TUnitKey, TUnit>, TUnitService>();
        return services;
    }

    public static IServiceCollection AddUnitService<TUnitService, TUnit>(this IServiceCollection services)
        where TUnit : Unit
        where TUnitService : class, IUnitService<TUnit>
    {
        services.AddScoped<IUnitService<TUnit>, TUnitService>();
        return services;
    }

    public static IdentityBuilder AddIdentityUnit<TUnitKey, TUnit>(this IdentityBuilder builder, Action<UnitOptions<TUnitKey, TUnit>> configureOptions = default)
            where TUnitKey : IEquatable<TUnitKey>
            where TUnit : Unit<TUnitKey>
    {
        if (configureOptions is not null)
        {
            // Register options
            var options = builder.Services.RegisterUnitOptions(configureOptions);

            // Insert default values
            builder.Services.BuildServiceProvider().CreateScope().ServiceProvider
                .GetRequiredService<IUnitService<TUnitKey, TUnit>>()
                .InsertDefaultUnits(options.DefaultValues);
        }

        return builder;
    }

    public static IdentityBuilder AddIdentityUnit<TUnit>(this IdentityBuilder builder, Action<UnitOptions<TUnit>> configureOptions = default)
        where TUnit : Unit
    {
        if (configureOptions is not null)
        {
            // Register options
            var options = builder.Services.RegisterUnitOptions(configureOptions);

            // Insert default values
            builder.Services.BuildServiceProvider().CreateScope().ServiceProvider
                .GetRequiredService<IUnitService<TUnit>>()
                .InsertDefaultUnits(options.DefaultValues);
        }

        return builder;
    }

    private static UnitOptions<TUnitKey, TUnit> RegisterUnitOptions<TUnitKey, TUnit>(this IServiceCollection services, Action<UnitOptions<TUnitKey, TUnit>> configureOptions)
        where TUnitKey : IEquatable<TUnitKey>
        where TUnit : Unit<TUnitKey>
    {
        // Register IOptions<UnitOptions<,>>
        services.Configure(configureOptions);

        // Get UnitOptions<,> from ServiceProvider
        var options = services.BuildServiceProvider().GetRequiredService<Options.IOptions<UnitOptions<TUnitKey, TUnit>>>().Value;

        // Register UnitOptions<,> without IOptions
        services.AddSingleton<UnitOptions<TUnitKey, TUnit>>(options);

        return options;
    }

    private static UnitOptions<TUnit> RegisterUnitOptions<TUnit>(this IServiceCollection services, Action<UnitOptions<TUnit>> configureOptions)
        where TUnit : Unit
    {
        // Register IOptions<UnitOptions<,>>
        services.Configure(configureOptions);

        // Get UnitOptions<,> from ServiceProvider
        var options = services.BuildServiceProvider().GetRequiredService<Options.IOptions<UnitOptions<TUnit>>>().Value;

        // Register UnitOptions<,> without IOptions
        services.AddSingleton<UnitOptions<TUnit>>(options);

        return options;
    }

    private static void InsertDefaultUnits<TUnitKey, TUnit>(this IUnitService<TUnitKey, TUnit> service, IEnumerable<TUnit> values)
        where TUnitKey : IEquatable<TUnitKey>
        where TUnit : Unit<TUnitKey>
    {
        if (values is null || values.Any() is false) return;

        // Insert default values
        foreach (var unit in values)
        {
            try
            {
                // If unit was not inserted before, insert it
                if (service.Exists(unit.Code, unit.Type).Result is false)
                {
                    // Set parent id
                    var parent = unit.GetParentUnit();
                    if (parent != null) unit.ParentUnitId = parent.Id;

                    // Insert
                    service.Create(unit).Wait();
                }
            }
            catch (Exception)
            {
                // ignored
            }
        }
    }

    private static void InsertDefaultUnits<TUnit>(this IUnitService<TUnit> service, IEnumerable<TUnit> values)
        where TUnit : Unit
    {
        service.InsertDefaultUnits<Guid, TUnit>(values);
    }
}