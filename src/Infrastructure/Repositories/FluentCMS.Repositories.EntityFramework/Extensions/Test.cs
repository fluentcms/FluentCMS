using System.Collections.Concurrent;
using FluentCMS.Repositories.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FluentCMS.Repositories.EntityFramework.Extensions;

public interface IDatabaseManagerOptions
{
    IDatabaseScopeBuilder Default();
    IDatabaseScopeBuilder For<TMarker>() where TMarker : IDatabaseScopeMarker;
}

public interface IDatabaseScopeBuilder
{
    IDatabaseScopeBuilder Use(IDatabaseConfiguration configuration);
    IDatabaseScopeBuilder Use(Func<IServiceProvider, IDatabaseConfiguration> factory);
    IDatabaseScopeBuilder Use(Action<DbContextOptionsBuilder> apply);
}

public interface IDatabaseConfiguration
{
    void ConfigureDbContext(DbContextOptionsBuilder optionsBuilder);
}

public interface IDatabaseConfigurationResolver
{
    IDatabaseConfiguration GetDefault();
    IDatabaseConfiguration GetFor<TMarker>() where TMarker : IDatabaseScopeMarker;
}

public static class DatabaseManagerRegistration
{
    public static IServiceCollection AddDatabaseManager(this IServiceCollection services, Action<IDatabaseManagerOptions> configure)
    {
        var opts = new OptionsImpl();
        configure(opts);

        if (opts.DefaultFactory is null)
            throw new InvalidOperationException("DatabaseManager: Default() must be configured.");

        services.TryAddSingleton<IDatabaseConfigurationResolver>(sp =>
            new ResolverImpl(sp, opts.DefaultFactory, opts.MarkerFactories));

        return services;
    }

    private sealed class OptionsImpl : IDatabaseManagerOptions
    {
        public Func<IServiceProvider, IDatabaseConfiguration>? DefaultFactory { get; private set; }
        public Dictionary<Type, Func<IServiceProvider, IDatabaseConfiguration>> MarkerFactories { get; } = [];

        public IDatabaseScopeBuilder Default() =>
            new DatabaseScopeBuilder(cfg => DefaultFactory = cfg);

        public IDatabaseScopeBuilder For<TMarker>() where TMarker : IDatabaseScopeMarker =>
            new DatabaseScopeBuilder(cfg => MarkerFactories[typeof(TMarker)] = cfg);
    }

    private sealed class DatabaseScopeBuilder(Action<Func<IServiceProvider, IDatabaseConfiguration>> set) : IDatabaseScopeBuilder
    {
        public IDatabaseScopeBuilder Use(IDatabaseConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            set(_ => configuration);

            return this;
        }

        public IDatabaseScopeBuilder Use(Func<IServiceProvider, IDatabaseConfiguration> factory)
        {
            set(factory ?? throw new ArgumentNullException(nameof(factory)));
            return this;
        }

        public IDatabaseScopeBuilder Use(Action<DbContextOptionsBuilder> apply)
        {
            ArgumentNullException.ThrowIfNull(apply);
            // wrap once; resolver will cache per-marker/default
            var cfg = new InlineDatabaseConfiguration(apply);
            set(_ => cfg);
            return this;
        }
    }

    // Simple wrapper for an EF Core options action
    private sealed class InlineDatabaseConfiguration(Action<DbContextOptionsBuilder> apply) : IDatabaseConfiguration
    {
        public void ConfigureDbContext(DbContextOptionsBuilder optionsBuilder) => apply(optionsBuilder);
    }

    private sealed class ResolverImpl : IDatabaseConfigurationResolver
    {
        private readonly IServiceProvider _sp;
        private readonly Func<IServiceProvider, IDatabaseConfiguration> _defaultFactory;
        private readonly IReadOnlyDictionary<Type, Func<IServiceProvider, IDatabaseConfiguration>> _markerFactories;

        private readonly Lazy<IDatabaseConfiguration> _default;
        private readonly ConcurrentDictionary<Type, IDatabaseConfiguration> _cache = new();

        public ResolverImpl(
            IServiceProvider sp,
            Func<IServiceProvider, IDatabaseConfiguration> defaultFactory,
            IDictionary<Type, Func<IServiceProvider, IDatabaseConfiguration>> markerFactories)
        {
            _sp = sp;
            _defaultFactory = defaultFactory;
            _markerFactories = new Dictionary<Type, Func<IServiceProvider, IDatabaseConfiguration>>(markerFactories);
            _default = new Lazy<IDatabaseConfiguration>(() => _defaultFactory(_sp));
        }

        public IDatabaseConfiguration GetDefault() => _default.Value;

        public IDatabaseConfiguration GetFor<TMarker>() where TMarker : IDatabaseScopeMarker
        {
            var key = typeof(TMarker);
            if (_markerFactories.TryGetValue(key, out var f))
                return _cache.GetOrAdd(key, _ => f(_sp));
            return _default.Value;
        }
    }
}
