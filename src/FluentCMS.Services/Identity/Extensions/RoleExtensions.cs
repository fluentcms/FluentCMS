using Microsoft.AspNetCore.Identity;
using uBeac.Identity;

namespace Microsoft.Extensions.DependencyInjection;

public static class RoleExtensions
{
    public static IServiceCollection AddRoleService<TRoleService, TRoleKey, TRole>(this IServiceCollection services)
        where TRoleKey : IEquatable<TRoleKey>
        where TRole : Role<TRoleKey>
        where TRoleService : class, IRoleService<TRoleKey, TRole>
    {
        services.AddScoped<IRoleService<TRoleKey, TRole>, TRoleService>();
        return services;
    }

    public static IServiceCollection AddRoleService<TRoleService, TRole>(this IServiceCollection services)
        where TRole : Role
        where TRoleService : class, IRoleService<TRole>
    {
        services.AddScoped<IRoleService<TRole>, TRoleService>();
        return services;
    }

    public static IdentityBuilder AddIdentityRole<TRoleKey, TRole>(this IdentityBuilder builder, Action<RoleOptions<TRoleKey, TRole>> configureOptions = default)
            where TRoleKey : IEquatable<TRoleKey>
            where TRole : Role<TRoleKey>
    {
        // Configure AspNetIdentity
        builder
            .AddRoles<TRole>()
            .AddRoleStore<RoleStore<TRole, TRoleKey>>()
            .AddRoleManager<RoleManager<TRole>>();

        if (configureOptions is not null)
        {
            // Register options
            var options = builder.Services.RegisterRoleOptions(configureOptions);

            // Insert default values
            builder.Services.BuildServiceProvider().CreateScope().ServiceProvider
                .GetRequiredService<IRoleService<TRoleKey, TRole>>()
                .InsertDefaultRoles(options.DefaultValues);
        }

        return builder;
    }

    public static IdentityBuilder AddIdentityRole<TRole>(this IdentityBuilder builder, Action<RoleOptions<TRole>> configureOptions = default)
        where TRole : Role
    {

        // Configure AspNetIdentity
        builder
            .AddRoles<TRole>()
            .AddRoleStore<RoleStore<TRole>>()
            .AddRoleManager<RoleManager<TRole>>();

        if (configureOptions is not null)
        {
            // Register options
            var options = builder.Services.RegisterRoleOptions(configureOptions);

            // Insert default values
            builder.Services.BuildServiceProvider().CreateScope().ServiceProvider
                .GetRequiredService<IRoleService<TRole>>()
                .InsertDefaultRoles(options.DefaultValues);
        }

        return builder;
    }

    private static RoleOptions<TRoleKey, TRole> RegisterRoleOptions<TRoleKey, TRole>(this IServiceCollection services, Action<RoleOptions<TRoleKey, TRole>> configureOptions)
        where TRoleKey : IEquatable<TRoleKey>
        where TRole : Role<TRoleKey>
    {
        // Register IOptions<RoleOptions<,>>
        services.Configure(configureOptions);

        // Get RoleOptions<,> from ServiceProvider
        var options = services.BuildServiceProvider().GetRequiredService<Options.IOptions<RoleOptions<TRoleKey, TRole>>>().Value;

        // Register RoleOptions<,> without IOptions
        services.AddSingleton<RoleOptions<TRoleKey, TRole>>(options);

        return options;
    }

    private static RoleOptions<TRole> RegisterRoleOptions<TRole>(this IServiceCollection services, Action<RoleOptions<TRole>> configureOptions)
        where TRole : Role
    {
        // Register IOptions<RoleOptions<,>>
        services.Configure(configureOptions);

        // Get RoleOptions<,> from ServiceProvider
        var options = services.BuildServiceProvider().GetRequiredService<Options.IOptions<RoleOptions<TRole>>>().Value;

        // Register RoleOptions<,> without IOptions
        services.AddSingleton<RoleOptions<TRole>>(options);

        return options;
    }

    private static void InsertDefaultRoles<TRoleKey, TRole>(this IRoleService<TRoleKey, TRole> service, IEnumerable<TRole> values)
        where TRoleKey : IEquatable<TRoleKey>
        where TRole : Role<TRoleKey>
    {
        if (values is null || values.Any() is false) return;
        foreach (var role in values)
        {
            try
            {
                // If role was not inserted before, insert it
                if (service.Exists(role.Name).Result is false)
                {
                    service.Create(role).Wait();
                }
            }
            catch (Exception)
            {
                // ignored
            }
        }
    }

    private static void InsertDefaultRoles<TRole>(this IRoleService<TRole> service, IEnumerable<TRole> values)
        where TRole : Role
    {
        service.InsertDefaultRoles<Guid, TRole>(values);
    }
}