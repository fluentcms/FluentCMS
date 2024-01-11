using FluentCMS.Web.UI.Components.Resources;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Resources;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Extensions.DependencyInjection;
public static class UIServiceExtensions
{
    public static IServiceCollection AddFluentCmsUiComponents(this IServiceCollection services)
    {
        services.AddKeyedScoped(typeof(Icons).FullName, (_, _) =>
        {
            var iconsType = typeof(Icons);
            return new ResourceManager(iconsType.FullName!, iconsType.Assembly);
        });
        return services;
    }
}
