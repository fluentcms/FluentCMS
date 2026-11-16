using FluentCMS.Admin.Accessibility;
using FluentCMS.Admin.Interop;
using FluentCMS.Admin.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Admin;

internal static class AdminServiceCollectionExtensions
{
    public static IServiceCollection AddAdminLocalization(this IServiceCollection services, InterfaceCulture culture, FormatService formats)
    {
        services.AddLocalization();
        services.AddSingleton(culture);
        services.AddSingleton(formats);
        return services;
    }

    public static IServiceCollection AddAdminAccessibility(this IServiceCollection services)
    {
        services.AddSingleton<DocumentInterop>();
        services.AddSingleton<Announcer>();
        services.AddSingleton<IAnnouncer>(provider => provider.GetRequiredService<Announcer>());
        services.AddSingleton<IFocusService, FocusService>();
        return services;
    }
}
