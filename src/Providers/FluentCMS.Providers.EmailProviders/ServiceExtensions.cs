using FluentCMS.Providers.EmailProviders;

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceExtensions
{
    public static IServiceCollection AddSmtpEmailProvider(this IServiceCollection services, Dictionary<string, string> providerSettings)
    {
        var smtpConfig = new SmtpServerConfig
        {
            // extract values from settings dictionary and set them on config
            Server = providerSettings["Server"],
            Port = int.Parse(providerSettings["Port"]),
            Username = providerSettings["Username"],
            Password = providerSettings["Password"],
            EnableSsl = bool.Parse(providerSettings["EnableSsl"]),
            From = providerSettings["From"]
        };

        services.AddScoped(implementation => smtpConfig);
        services.AddScoped<IEmailProvider, SmtpEmailProvider>();

        return services;
    }
}
