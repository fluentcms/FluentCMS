using FluentCMS.Repositories.Abstractions;
using FluentCMS.Repositories.EntityFramework;
using FluentCMS.Repositories.Sqlite;
using FluentCMS.Repositories.Tests.Integration.TestEntities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Repositories.Tests.Integration.TestFixtures;

/// <summary>
/// Test fixture that provides file-based SQLite database testing.
/// This tests real database file I/O, persistence, and concurrent access scenarios.
/// Each test gets its own physical database file that is cleaned up after the test.
/// </summary>
public class FileSqliteTestFixture : IDisposable
{
    private readonly string _databaseFileFullPath;
    private readonly string _testDatabaseDirectory;

    public FileSqliteTestFixture()
    {
        // Create a temporary directory for test databases
        _testDatabaseDirectory = Path.Combine(Path.GetTempPath(), "FluentCMS_Integration_Tests", Guid.NewGuid().ToString("N"));
        var databaseFileName = $"test_db_{Guid.NewGuid():N}.db";
        _databaseFileFullPath = Path.Combine(_testDatabaseDirectory, databaseFileName);
        Directory.CreateDirectory(_testDatabaseDirectory);
    }

    /// <summary>
    /// Creates a file-based test scope with its own physical database file.
    /// This enables testing of real database persistence, file locking, and I/O scenarios.
    /// </summary>
    public FileTestScope CreateFileScope()
    {
        return new FileTestScope(_databaseFileFullPath);
    }

    public void Dispose()
    {
        try
        {
            // Clean up test database directory
            if (Directory.Exists(_testDatabaseDirectory))
            {
                // Force delete all files including those that might be locked
                var files = Directory.GetFiles(_testDatabaseDirectory, "*", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    try
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                        File.Delete(file);
                    }
                    catch
                    {
                        // Ignore cleanup errors - files might be locked by other processes
                    }
                }

                try
                {
                    Directory.Delete(_testDatabaseDirectory, true);
                }
                catch
                {
                    // Ignore cleanup errors - directory might still be in use
                }
            }
        }
        catch
        {
            // Ignore disposal errors
        }
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Represents a file-based test scope with its own physical database file.
/// Implements IDisposable to ensure proper cleanup after each test.
/// </summary>
public class FileTestScope : IDisposable
{
    private readonly IServiceScope _scope;
    private readonly TestDbContext _context;
    private readonly string _databaseFilePath;

    public FileTestScope(string databaseFilePath)
    {
        _databaseFilePath = databaseFilePath;
        // Create isolated service collection for this test
        var services = new ServiceCollection();

        // Configure logging
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));

        // Configure file-based SQLite database for this test scope
        var connectionString = $"Data Source={_databaseFilePath};";
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
        _context.Database.EnsureCreated();
    }

    /// <summary>
    /// Gets the file path of the database being used for this test.
    /// </summary>
    public string DatabaseFilePath => _databaseFilePath;

    /// <summary>
    /// Gets a repository for the specified entity type within this test scope.
    /// </summary>
    public IRepository<TEntity> GetRepository<TEntity>() where TEntity : class, IEntity
    {
        return _scope.ServiceProvider.GetRequiredService<IRepository<TEntity>>();
    }

    /// <summary>
    /// Gets the database context for this test scope.
    /// </summary>
    public TestDbContext GetDbContext()
    {
        return _context;
    }

    /// <summary>
    /// Gets the service provider for this test scope.
    /// </summary>
    public IServiceProvider ServiceProvider => _scope.ServiceProvider;

    /// <summary>
    /// Verifies that the database file exists on disk.
    /// </summary>
    public bool DatabaseFileExists()
    {
        return File.Exists(_databaseFilePath);
    }

    /// <summary>
    /// Gets the size of the database file in bytes.
    /// </summary>
    public long GetDatabaseFileSize()
    {
        if (!File.Exists(_databaseFilePath))
            return 0;

        var fileInfo = new FileInfo(_databaseFilePath);
        return fileInfo.Length;
    }

    public void Dispose()
    {
        try
        {
            // Dispose context and scope first
            _context?.Dispose();
            _scope?.Dispose();
        }
        catch
        {
            // Ignore disposal errors
        }
        GC.SuppressFinalize(this);
    }
}
