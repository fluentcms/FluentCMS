namespace FluentCMS.Api.Core.Api;

public static class ServiceCollectionExtensions
{
    public const string SESSION_ID_HEADER_KEY = "X_Session_Id";
    public const string UNIQUE_USER_ID_HEADER_KEY = "X-Unique-Id";
    public const string DEFAULT_LANGUAGE = "en-US";
    public const string USER_IP_FORWARDED_HEADER_KEY = "X-Forwarded-For";

    public static IServiceCollection AddSecurityContext(this IServiceCollection services)
    {

        // Replace the default role validator with our site-scoped one
        services.AddTransient<IRoleValidator<Role>, EnhancedSiteScopedRoleValidator>();
        services.AddTransient<SecurityContextResolver>();
        services.AddHttpContextAccessor();
        services.AddScoped<IUserContext>(sp => sp.GetRequiredService<ISecurityContext>());
        services.AddScoped(sp => sp.GetRequiredService<SecurityContextResolver>().Resolve());

        return services;
    }
}
