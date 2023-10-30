using FluentCMS.Services;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS;

public static class Extensions
{
    public static FluentCMSBuilder AddFluentCMSCore(this IServiceCollection services)
    {
        // register domain services
        services.AddTransient<UserService>();
        services.AddTransient<ContentTypeService>();

        return FluentCMSBuilder.Create(services);
    }
}

public class FluentCMSBuilder
{
    public IServiceCollection Services { get; private set; }

    private FluentCMSBuilder(IServiceCollection services)
    {
        Services = services;
    }

    internal static FluentCMSBuilder Create(IServiceCollection services)
    {
        return new FluentCMSBuilder(services);
    }
}
