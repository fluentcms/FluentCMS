﻿using FluentCMS.Identity;
using FluentCMS.Services;
using FluentCMS.Services.Setup;
using Microsoft.AspNetCore.Identity;

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddPermissions();

        services.AddScoped<ISetupManager, SetupManager>();

        services.AddScoped<IUserTokenProvider, JwtUserTokenProvider>();

        AddIdentity(services);

        RegisterServices(services);

        services.AddSetupServices();

        return services;
    }

    private static IdentityBuilder AddIdentity(IServiceCollection services)
    {
        var builder = services.AddIdentityCore<User>();

        builder
            .AddUserStore<UserStore>()
            .AddUserManager<UserManager<User>>()
            .AddDefaultTokenProviders()
            .AddTokenProvider<DataProtectorTokenProvider<User>>(UserService.PASSWORD_RESET_TOKEN_PROVIDER);

        return builder;
    }

    private static void RegisterServices(IServiceCollection services)
    {
        var serviceTypes = typeof(ServiceExtensions).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.Name.EndsWith("Service") && t.GetInterfaces().Contains(typeof(IAutoRegisterService)))
            .ToList();

        foreach (var serviceType in serviceTypes)
        {
            var interfaceType = serviceType.GetInterfaces().FirstOrDefault(i => i.Name.EndsWith(serviceType.Name))
                ?? throw new InvalidOperationException($"Interface for service '{serviceType.Name}' not found.");

            services.AddScoped(interfaceType, serviceType);
        }
    }

}
