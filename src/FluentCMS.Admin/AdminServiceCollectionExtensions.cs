using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Admin;

public static class AdminServiceCollectionExtensions
{
    // The pages use Core's services and what the host reads from the request (the initiator, the theme); nothing of the admin's own yet.
    public static IServiceCollection AddFluentCmsAdmin(this IServiceCollection services)
    {
        return services;
    }
}
