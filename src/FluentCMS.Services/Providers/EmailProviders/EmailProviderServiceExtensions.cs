using FluentCMS.Providers;

namespace Microsoft.Extensions.DependencyInjection;

public static class EmailProviderServiceExtensions
{
    public static IServiceCollection AddSmtpEmailProvider(this IServiceCollection services)
    {
        services.AddScoped<ISmtpEmailProvider, SmtpEmailProvider>();
        services.AddScoped(sp => new SmtpServerConfiguration
        {
            EnableSsl = true,
            Port = 2525,
            Server = "sandbox.smtp.mailtrap.io",
            UseDefaultCredentials = false,
            Username = "33b3d4be6eff7c",
            Password = "3bc847fa1f6d59"
        });

        return services;
    }
}
