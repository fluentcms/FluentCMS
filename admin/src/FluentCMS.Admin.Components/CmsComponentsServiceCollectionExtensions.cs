using FluentCMS.Admin.Components;
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
        services.AddScoped<ICmsToastService, CmsToastService>();
        return services;
    }
}
