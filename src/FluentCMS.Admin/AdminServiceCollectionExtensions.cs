using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Admin;

public static class AdminServiceCollectionExtensions
{
    public static IServiceCollection AddFluentCmsAdmin(this IServiceCollection services, Action<AdminOptions>? configure = null)
    {
        var options = new AdminOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);

        return services;
    }
}
