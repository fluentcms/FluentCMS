using System.Resources;

namespace Microsoft.Extensions.DependencyInjection;

public static class UIServiceExtensions
{
    public static IServiceCollection AddFluentCmsUiComponents(this IServiceCollection services)
    {
        services.AddKeyedScoped(
            typeof(IconResource).FullName,
            (_, _) =>
            {
                var iconsType = typeof(IconResource);
                return new ResourceManager(iconsType.FullName!, iconsType.Assembly);
            }
        );

        return services;
    }
}
