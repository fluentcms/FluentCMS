using FluentCMS.Repositories.Abstractions;
using FluentCMS.Repositories.EntityFramework;
using FluentCMS.Repositories.EntityFramework.Extensions;
using FluentCMS.Repositories.Sqlite;
using FluentCMS.Repositories.Tests.Integration.TestEntities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Repositories.Tests.Integration.TestFixtures;

/// <summary>
/// Test fixture that provides isolated database instances per test to ensure proper test isolation.
/// Each test gets its own in-memory database that is automatically cleaned up after the test.
/// </summary>
public class IsolatedSqliteTestFixture : IDisposable
{
    private readonly IServiceProvider _serviceProvider;

    public IsolatedSqliteTestFixture()
    {
        var services = new ServiceCollection();
        _serviceProvider = services.BuildServiceProvider();
    }

    /// <summary>
    /// Creates an isolated test scope with its own database instance.
    /// This ensures complete test isolation - each test gets a fresh database.
    /// </summary>
    public IsolatedTestScope CreateIsolatedScope()
    {
        return new IsolatedTestScope();
    }

    public void Dispose()
    {
        if (_serviceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Represents an isolated test scope with its own database and repositories.
/// Implements IDisposable to ensure proper cleanup after each test.
/// </summary>
public class IsolatedTestScope : IDisposable
{
    private readonly IServiceScope _scope;
    private readonly TestDbContext _context;
    private readonly string _databaseId;

    public IsolatedTestScope()
    {
        // Generate unique database identifier for this test
        _databaseId = Guid.NewGuid().ToString("N");

        // Create isolated service collection for this test
        var services = new ServiceCollection();

        // Copy base services from the parent service provider
        var baseServices = new ServiceCollection();
        baseServices.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        baseServices.AddScoped<IApplicationExecutionContext, SystemExecutionContext>();

        // Configure logging
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));

        // Configure unique in-memory SQLite database for this test scope
        var connectionString = $"Data Source=test_db_{_databaseId};Mode=Memory;Cache=Shared;";
        services.AddSqliteDatabase(connectionString);

        // Register application services
        services.AddScoped<IApplicationExecutionContext, SystemExecutionContext>();
        services.AddEfDbContext<TestDbContext>();
        services.AddGenericRepository<TestUser, TestDbContext>();
        services.AddGenericRepository<TestProduct, TestDbContext>();
        services.AddGenericRepository<TestCategory, TestDbContext>();

        var serviceProvider = services.BuildServiceProvider();
        _scope = serviceProvider.CreateScope();

        // Create and ensure database is created
        _context = _scope.ServiceProvider.GetRequiredService<TestDbContext>();
        _context.Database.OpenConnection(); // Keep connection open for in-memory SQLite
        _context.Database.EnsureCreated();
    }

    /// <summary>
    /// Gets a repository for the specified entity type within this isolated test scope.
    /// </summary>
    public IRepository<TEntity> GetRepository<TEntity>() where TEntity : class, IEntity
    {
        return _scope.ServiceProvider.GetRequiredService<IRepository<TEntity>>();
    }

    /// <summary>
    /// Gets the database context for this isolated test scope.
    /// </summary>
    public TestDbContext GetDbContext()
    {
        return _context;
    }

    /// <summary>
    /// Gets the service provider for this isolated test scope.
    /// </summary>
    public IServiceProvider ServiceProvider => _scope.ServiceProvider;

    public void Dispose()
    {
        try
        {
            // Close connection and dispose context
            _context?.Database.CloseConnection();
            _context?.Dispose();
        }
        finally
        {
            _scope?.Dispose();
        }
        GC.SuppressFinalize(this);
    }
}
