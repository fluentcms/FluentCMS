using FluentCMS.Web.UI.Components;
using FluentCMS.Web.UI.Components.Resources;
using System.Resources;

namespace Microsoft.Extensions.DependencyInjection;

public static class UIServiceExtensions
{
    public static IServiceCollection AddFluentCmsUiComponents(this IServiceCollection services)
    {
        services.AddScoped<ConfirmService>();

        services.AddKeyedScoped(
            typeof(Icons).FullName,
            (_, _) =>
            {
                var iconsType = typeof(Icons);
                return new ResourceManager(iconsType.FullName!, iconsType.Assembly);
            }
        );

        return services;
    }
}
