using BlazorBlueprint.Primitives.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Admin;

public static class AdminServiceCollectionExtensions
{
    public static IServiceCollection AddFluentCmsAdmin(this IServiceCollection services, Action<AdminOptions>? configure = null)
    {
        var options = new AdminOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);

        // Spike only (ADR-0011): the Blazor Blueprint variant on /ui-spike. Removed with the losing variant.
        services.AddBlazorBlueprintPrimitives();

        return services;
    }
}
