using FluentCMS.Repositories.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Repositories.EntityFramework.Extensions;

public static class EfRegistrationExtensions
{
    public static IServiceCollection AddEfDbContext<TContext, TMarker>(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder>? additional = null)
        where TContext : DbContext
        where TMarker : IDatabaseScopeMarker
    {
        services.AddDbContext<TContext>((sp, options) =>
        {
            var resolver = sp.GetRequiredService<IDatabaseConfigurationResolver>();
            resolver.GetFor<TMarker>().ConfigureDbContext(options);
            additional?.Invoke(options);
        });
        return services;
    }

    public static IServiceCollection AddEfDbContext<TContext>(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder>? additional = null)
        where TContext : DbContext
    {
        services.AddDbContext<TContext>((sp, options) =>
        {
            var resolver = sp.GetRequiredService<IDatabaseConfigurationResolver>();
            resolver.GetDefault().ConfigureDbContext(options);
            additional?.Invoke(options);
        });
        return services;
    }
}
