using FluentCMS.Providers.Email;

namespace Microsoft.Extensions.DependencyInjection;

public static class EmailServiceExtensions
{
    public static IServiceCollection AddSmtpEmailProvider(this IServiceCollection services)
    {
        services.AddScoped<IEmailProvider, SmtpEmailProvider>();

        return services;
    }
}
