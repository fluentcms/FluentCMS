using FluentCMS.Services;

namespace Microsoft.Extensions.DependencyInjection;

public static class PolicyServiceExtensions
{
    public static void AddPolicies(this IServiceCollection services)
    {
        services.AddSingleton<SitePolicies>();
        services.AddSingleton<PagePolicies>();
    }
}
