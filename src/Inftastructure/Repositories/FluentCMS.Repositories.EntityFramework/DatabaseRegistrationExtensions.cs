namespace FluentCMS.Repositories.EntityFramework;

public static class DatabaseRegistrationExtensions
{
    public static IServiceCollection AddGenericRepository<TEntity, TContext>(this IServiceCollection services) where TEntity : class, IEntity where TContext : DbContext
    {
        services.AddScoped<IRepository<TEntity>, Repository<TEntity, TContext>>();
        return services;
    }

    public static IServiceCollection AddEfDbContext<TContext>(this IServiceCollection services, Action<DbContextOptionsBuilder>? additionalConfiguration = null, ServiceLifetime contextLifetime = ServiceLifetime.Scoped) where TContext : DbContext
    {
        services.TryAddScoped<AuditableEntityInterceptor>();
        
        services.AddDbContext<TContext>((provider, options) =>
        {
            options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);

            var auditableEntityInterceptor = provider.GetRequiredService<AuditableEntityInterceptor>();

            options.AddInterceptors(auditableEntityInterceptor);

            // Apply global configuration first
            var dbConfig = provider.GetRequiredService<IDatabaseConfiguration>();
            dbConfig.ConfigureDbContext(options);

            // Then apply context-specific configuration if provided
            additionalConfiguration?.Invoke(options);
        }, contextLifetime);

        return services;
    }
}
