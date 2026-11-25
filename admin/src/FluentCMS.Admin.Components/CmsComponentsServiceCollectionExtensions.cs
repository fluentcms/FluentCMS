using FluentCMS.Admin.Components;
using FluentCMS.Admin.Components.Resources;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.FluentUI.AspNetCore.Components;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers the services of the wrapped component set.</summary>
public static class CmsComponentsServiceCollectionExtensions
{
    /// <summary>Adds the services the <c>Cms*</c> components need, including those of the library under them.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddCmsComponents(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddFluentUIComponents();
        // The host's announcer reads each toast through the shell's live regions; without one a toast is shown only.
        services.AddScoped(provider => new CmsToastService(provider.GetService<FluentCMS.Admin.IAnnouncer>(), provider.GetService<TimeProvider>() ?? TimeProvider.System));
        services.AddScoped<ICmsToastService>(provider => provider.GetRequiredService<CmsToastService>());
        services.TryAddScoped<CmsClipboard>();
        services.TryAddSingleton<ICmsTextSource, CmsStrings>();
        // The library's texts, such as a row's selection state, then follow the culture and the ICmsTextSource of the host.
        services.Replace(ServiceDescriptor.Scoped<IFluentLocalizer, CmsLibraryTexts>());
        return services;
    }
}
