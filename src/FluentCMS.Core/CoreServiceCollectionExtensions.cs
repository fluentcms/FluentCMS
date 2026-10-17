using FluentCMS.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core;

public static class CoreServiceCollectionExtensions
{
    // The composition seam for domain and application services; each epic adds its registrations here.
    public static IServiceCollection AddFluentCmsCore(this IServiceCollection services)
    {
        // Built-ins under FluentCMS.Core.Builtin are discovered the same way a plugin assembly would be (EXT-02).
        services.AddFluentCmsExtensions(typeof(CoreServiceCollectionExtensions).Assembly);

        return services;
    }
}
