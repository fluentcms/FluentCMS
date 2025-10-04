using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Repositories.Abstractions;

/// <summary>
/// Base implementation for database area configurations
/// </summary>
public abstract class BaseDatabaseAreaConfiguration : IDatabaseAreaConfiguration
{
    protected readonly IServiceCollection _services;

    protected BaseDatabaseAreaConfiguration(IServiceCollection services)
    {
        _services = services;
    }

    public IDatabaseProvider? DatabaseProvider { get; protected set; }
    public string? ConnectionString { get; protected set; }
    public Action<DbContextOptionsBuilder>? ProviderOptions { get; protected set; }
    public DataSeedingOptions? SeedingOptions { get; protected set; }

    public virtual IDatabaseAreaConfiguration EnableDataSeeding(Action<DataSeedingOptions> seedingConfiguration)
    {
        SeedingOptions = new DataSeedingOptions();
        seedingConfiguration?.Invoke(SeedingOptions);
        return this;
    }
}

/// <summary>
/// Configuration for a specific database area
/// </summary>
/// <typeparam name="TArea">The database area marker interface</typeparam>
public class DatabaseAreaConfiguration<TArea> : BaseDatabaseAreaConfiguration
    where TArea : class, IDatabaseArea
{
    public DatabaseAreaConfiguration(IServiceCollection services) : base(services)
    {
    }

    /// <summary>
    /// Configure this area to use SQLite database
    /// </summary>
    /// <param name="connectionString">SQLite connection string</param>
    /// <param name="sqliteOptions">SQLite-specific options</param>
    /// <returns>The configuration for chaining</returns>
    public DatabaseAreaConfiguration<TArea> UseSqlite(string connectionString, Action<DbContextOptionsBuilder>? sqliteOptions = null)
    {
        // Note: Implementation will be in a separate Sqlite package
        throw new NotImplementedException("SQLite provider requires FluentCMS.Repositories.Sqlite package");
    }

    /// <summary>
    /// Configure this area to use SQL Server database
    /// </summary>
    /// <param name="connectionString">SQL Server connection string</param>
    /// <param name="sqlServerOptions">SQL Server-specific options</param>
    /// <returns>The configuration for chaining</returns>
    public DatabaseAreaConfiguration<TArea> UseSqlServer(string connectionString, Action<DbContextOptionsBuilder>? sqlServerOptions = null)
    {
        // Note: Implementation will be in a separate SqlServer package
        throw new NotImplementedException("SQL Server provider requires FluentCMS.Repositories.SqlServer package");
    }

    /// <summary>
    /// Configure this area to use a custom database provider
    /// </summary>
    /// <param name="databaseProvider">The database provider implementation</param>
    /// <param name="connectionString">Connection string</param>
    /// <param name="providerOptions">Provider-specific options</param>
    /// <returns>The configuration for chaining</returns>
    public DatabaseAreaConfiguration<TArea> UseProvider(IDatabaseProvider databaseProvider, string connectionString, Action<DbContextOptionsBuilder>? providerOptions = null)
    {
        DatabaseProvider = databaseProvider;
        ConnectionString = connectionString;
        ProviderOptions = providerOptions;
        return this;
    }

    /// <summary>
    /// Enable data seeding for this area
    /// </summary>
    /// <param name="seedingConfiguration">Configuration for data seeding</param>
    /// <returns>The configuration for chaining</returns>
    public new DatabaseAreaConfiguration<TArea> EnableDataSeeding(Action<DataSeedingOptions> seedingConfiguration)
    {
        base.EnableDataSeeding(seedingConfiguration);
        return this;
    }
}

/// <summary>
/// Default database configuration for areas without explicit configuration
/// </summary>
public class DefaultDatabaseConfiguration : BaseDatabaseAreaConfiguration
{
    public DefaultDatabaseConfiguration(IServiceCollection services) : base(services)
    {
    }

    /// <summary>
    /// Configure default database to use SQLite
    /// </summary>
    /// <param name="connectionString">SQLite connection string</param>
    /// <param name="sqliteOptions">SQLite-specific options</param>
    /// <returns>The configuration for chaining</returns>
    public DefaultDatabaseConfiguration UseSqlite(string connectionString, Action<DbContextOptionsBuilder>? sqliteOptions = null)
    {
        // Note: Implementation will be in a separate Sqlite package
        throw new NotImplementedException("SQLite provider requires FluentCMS.Repositories.Sqlite package");
    }

    /// <summary>
    /// Configure default database to use SQL Server
    /// </summary>
    /// <param name="connectionString">SQL Server connection string</param>
    /// <param name="sqlServerOptions">SQL Server-specific options</param>
    /// <returns>The configuration for chaining</returns>
    public DefaultDatabaseConfiguration UseSqlServer(string connectionString, Action<DbContextOptionsBuilder>? sqlServerOptions = null)
    {
        // Note: Implementation will be in a separate SqlServer package
        throw new NotImplementedException("SQL Server provider requires FluentCMS.Repositories.SqlServer package");
    }

    /// <summary>
    /// Configure default database to use a custom provider
    /// </summary>
    /// <param name="databaseProvider">The database provider implementation</param>
    /// <param name="connectionString">Connection string</param>
    /// <param name="providerOptions">Provider-specific options</param>
    /// <returns>The configuration for chaining</returns>
    public DefaultDatabaseConfiguration UseProvider(IDatabaseProvider databaseProvider, string connectionString, Action<DbContextOptionsBuilder>? providerOptions = null)
    {
        DatabaseProvider = databaseProvider;
        ConnectionString = connectionString;
        ProviderOptions = providerOptions;
        return this;
    }

    /// <summary>
    /// Enable data seeding for the default configuration
    /// </summary>
    /// <param name="seedingConfiguration">Configuration for data seeding</param>
    /// <returns>The configuration for chaining</returns>
    public new DefaultDatabaseConfiguration EnableDataSeeding(Action<DataSeedingOptions> seedingConfiguration)
    {
        base.EnableDataSeeding(seedingConfiguration);
        return this;
    }
}
