namespace FluentCMS.Repositories.EntityFramework;

public static class DatabaseRegistrationExtensions
{
    public static IServiceCollection AddGenericRepository<TEntity, TContext>(this IServiceCollection services) where TEntity : class, IEntity where TContext : DbContext
    {
        services.AddScoped<IRepository<TEntity>, Repository<TEntity, TContext>>();
        services.AddScoped<ITransactionalRepository<TEntity>, Repository<TEntity, TContext>>();
        services.AddScoped<ICachedRepository<TEntity>, CachedRepository<TEntity, TContext>>();
        return services;
    }

    public static IServiceCollection AddEfDbContext<TContext>(this IServiceCollection services, Action<DbContextOptionsBuilder>? additionalConfiguration = null) where TContext : DbContext
    {
        services.TryAddScoped<IRepositoryEventPublisher, RepositoryEventPublisher>();
        services.TryAddScoped<AuditableEntityInterceptor>();
        services.TryAddScoped<RepositoryEventBusPublisherInterceptor>();

        services.AddDbContext<TContext>((provider, options) =>
        {
            options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);

            var auditableEntityInterceptor = provider.GetRequiredService<AuditableEntityInterceptor>();
            var eventBusPublisherInterceptor = provider.GetRequiredService<RepositoryEventBusPublisherInterceptor>();

            options.AddInterceptors(auditableEntityInterceptor);
            options.AddInterceptors(eventBusPublisherInterceptor);

            // Apply global configuration first
            var dbConfig = provider.GetRequiredService<IDatabaseConfiguration>();
            dbConfig.ConfigureDbContext(options);

            // Then apply context-specific configuration if provided
            additionalConfiguration?.Invoke(options);
        });

        return services;
    }
}
