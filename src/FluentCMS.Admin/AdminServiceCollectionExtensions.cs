using FluentCMS.Admin.Components.Ui;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FluentCMS.Admin;

public static class AdminServiceCollectionExtensions
{
    // The pages use Core's services and what the host reads from the request (the initiator, the theme); the admin adds its toasts.
    public static IServiceCollection AddFluentCmsAdmin(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<ToastService>();
        return services;
    }
}
