﻿using FluentCMS.Web.UI.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Extensions.DependencyInjection;

public static class AdminUIServiceExtensions
{
    public static IServiceCollection AddAdminUIServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddLocalStorage();
        services.AddApiClients(configuration);
        services.AddScoped<SetupManager>();
        services.AddErrorMessageFactory();
        services.AddCascadingAuthenticationState();

        return services;
    }
}
