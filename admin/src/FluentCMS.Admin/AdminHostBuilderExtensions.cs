using System.Globalization;
using FluentCMS.Admin.Api;
using FluentCMS.Admin.Configuration;
using FluentCMS.Admin.Localization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Admin;

public static class AdminHostBuilderExtensions
{
    // Asynchronous because it reads config.json before anything else registers (spec 2.5).
    public static async Task AddFluentCmsAdmin(this WebAssemblyHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // The wrapped set calls no API, and the component gallery is a root of its own, so it does not wait for config.json.
        builder.Services.AddCmsComponents();

        // A team's app made from the standard template adds its own.
        if (!builder.RootComponents.Any(mapping => mapping.ComponentType == typeof(HeadOutlet)))
        {
            builder.RootComponents.Add<HeadOutlet>("head::after");
        }

        using var http = new HttpClient
        {
            BaseAddress = new Uri(builder.HostEnvironment.BaseAddress),
            Timeout = AdminConfigurationLoader.Timeout,
        };
        var startup = await AdminConfigurationLoader.LoadAsync(http);

        // At start the runtime has set the current culture to the browser's. The culture must be set before the host runs,
        // which loads its resources.
        var browser = CultureInfo.CurrentCulture;
        var culture = new InterfaceCulture(InterfaceCulture.Choose(
            new NoInterfaceCulturePreference().Culture, browser, InterfaceCultureOptions.From(startup.Configuration?.Cultures)));
        culture.Apply(browser);

        builder.Services.AddAdminStartup(startup, culture, new FormatService(browser, TimeZoneInfo.Local));
    }

    // The error page of an invalid configuration needs the culture and the document as well, so these register either way.
    internal static IServiceCollection AddAdminStartup(this IServiceCollection services, AdminStartup startup, InterfaceCulture culture, FormatService formats)
    {
        services.AddSingleton(startup);
        services.AddAdminLocalization(culture, formats).AddAdminAccessibility();
        if (startup.Configuration is { } configuration)
        {
            services.AddAdminServices(configuration);
        }

        return services;
    }

    // Every service that can call the API registers here, and only from a valid configuration, so that none can run without
    // one.
    internal static IServiceCollection AddAdminServices(this IServiceCollection services, AdminConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton(configuration);
        // Before AddManagementApi, whose signed-out default it replaces.
        services.AddScoped<IRateLimitNotice, RateLimitNotice>();
        services.AddManagementApi();
        services.AddOptions<ManagementApiOptions>().Configure(options => options.ApiBaseUrl = configuration.ApiBaseUrl);
        return services;
    }
}
