using FluentCMS.Providers.EmailProviders;

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceExtensions
{
    public static IServiceCollection AddSmtpEmailProvider(this IServiceCollection services)
    {
        services.AddScoped<IEmailProvider, SmtpEmailProvider>();
        return services;
    }
}
