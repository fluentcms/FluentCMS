using FluentCMS.Repositories.Abstractions;
using FluentCMS.Repositories.EntityFramework;
using FluentCMS.Repositories.Sqlite;
using FluentCMS.Repositories.Tests.Integration.TestEntities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Repositories.Tests.Integration.TestFixtures;

public class SqliteTestFixture : IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly TestDbContext _context;

    public SqliteTestFixture()
    {
        var services = new ServiceCollection();

        // Configure logging
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));

        // Configure SQLite in-memory with shared cache
        var connectionString = $"Data Source=:memory:;Cache=Shared;";
        services.AddSqliteDatabase(connectionString);

        services.AddScoped<IApplicationExecutionContext, SystemExecutionContext>();
        services.AddEfDbContext<TestDbContext>(options => { }, ServiceLifetime.Singleton);
        services.AddGenericRepository<TestUser, TestDbContext>();
        services.AddGenericRepository<TestProduct, TestDbContext>();
        services.AddGenericRepository<TestCategory, TestDbContext>();
        // Register repositories
        //services.AddScoped<IRepository<TestUser>, Repository<TestUser, TestDbContext>>();
        //services.AddScoped<IRepository<TestProduct>, Repository<TestProduct, TestDbContext>>();
        //services.AddScoped<IRepository<TestCategory>, Repository<TestCategory, TestDbContext>>();

        _serviceProvider = services.BuildServiceProvider();

        // Create and ensure database is created
        _context = _serviceProvider.GetRequiredService<TestDbContext>();
        _context.Database.OpenConnection(); // Keep connection open for in-memory SQLite
        _context.Database.EnsureCreated();
    }

    public IServiceScope CreateScope()
    {
        return _serviceProvider.CreateScope();
    }

    public TestDbContext CreateDbContext()
    {
        return _serviceProvider.GetRequiredService<TestDbContext>();
    }

    public IRepository<TEntity> GetRepository<TEntity>() where TEntity : class, IEntity
    {
        return _serviceProvider.GetRequiredService<IRepository<TEntity>>();
    }

    public async Task CleanDatabase()
    {
        try
        {
            // More robust approach - recreate the database to ensure clean state
            await _context.Database.EnsureDeletedAsync();
            await _context.Database.EnsureCreatedAsync();
        }
        catch (Exception)
        {
            // Fallback: Try to delete data directly using SQL
            try
            {
                await _context.Database.ExecuteSqlRawAsync("DELETE FROM TestUsers");
                await _context.Database.ExecuteSqlRawAsync("DELETE FROM TestProducts");
                await _context.Database.ExecuteSqlRawAsync("DELETE FROM TestCategories");
            }
            catch
            {
                // Last resort: detach all tracked entities and remove what exists
                _context.ChangeTracker.Clear();
                var users = await _context.TestUsers.ToListAsync();
                var products = await _context.TestProducts.ToListAsync();
                var categories = await _context.TestCategories.ToListAsync();

                if (users.Any()) _context.TestUsers.RemoveRange(users);
                if (products.Any()) _context.TestProducts.RemoveRange(products);
                if (categories.Any()) _context.TestCategories.RemoveRange(categories);

                await _context.SaveChangesAsync();
            }
        }
    }

    public void Dispose()
    {
        _context?.Dispose();
        if (_serviceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }
        GC.SuppressFinalize(this);
    }
}

public class SqliteTestFixtureFactory
{
    public static SqliteTestFixture Create() => new();

    public static IServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();

        // Configure logging for tests
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));

        // Create unique database name for each test run
        var dbName = Guid.NewGuid().ToString();
        var connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared;";

        services.AddScoped<IApplicationExecutionContext, SystemExecutionContext>();

        services.AddEfDbContext<TestDbContext>(options => { }, ServiceLifetime.Singleton);

        // Register repositories without interceptors for clean testing
        //services.AddScoped<IRepository<TestUser>, Repository<TestUser, TestDbContext>>();
        //services.AddScoped<IRepository<TestProduct>, Repository<TestProduct, TestDbContext>>();
        //services.AddScoped<IRepository<TestCategory>, Repository<TestCategory, TestDbContext>>();

        var serviceProvider = services.BuildServiceProvider();

        // Ensure database is created
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        context.Database.OpenConnection();
        context.Database.EnsureCreated();

        return serviceProvider;
    }
}
