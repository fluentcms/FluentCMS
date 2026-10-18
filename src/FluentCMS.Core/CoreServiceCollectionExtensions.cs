using FluentCMS.Core.Audit;
using FluentCMS.Core.Extensions;
using FluentCMS.Core.Schema;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FluentCMS.Core;

public static class CoreServiceCollectionExtensions
{
    // The composition seam for domain and application services; each epic adds its registrations here.
    public static IServiceCollection AddFluentCmsCore(this IServiceCollection services)
    {
        // Built-ins under FluentCMS.Core.Builtin are discovered the same way a plugin assembly would be (EXT-02).
        services.AddFluentCmsExtensions(typeof(CoreServiceCollectionExtensions).Assembly);

        services.TryAddSingleton<FieldTypeRegistry>();

        // Reads through IAuditReader, which Infrastructure provides.
        services.AddScoped<IAuditExporter, AuditExporter>();

        return services;
    }
}
