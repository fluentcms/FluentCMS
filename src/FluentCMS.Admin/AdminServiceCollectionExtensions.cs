using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Admin;

public static class AdminServiceCollectionExtensions
{
    // The Blazor shell arrives with the admin shell issue; until then this registers nothing.
    public static IServiceCollection AddFluentCmsAdmin(this IServiceCollection services)
    {
        return services;
    }
}
