using FluentCMS.Core.Storage;
using FluentCMS.Schema;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace FluentCMS.Data.Tests.Storage;

// One in-memory database, which lives as long as its connection stays open.
internal sealed class StorageDatabase : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly SchemaModel _schema;
    private readonly IStorageConventions? _conventions;

    public StorageDatabase(SchemaModel schema, IStorageConventions? conventions = null)
    {
        _schema = schema;
        _conventions = conventions;
        _connection.Open();
    }

    public CmsDbContext CreateContext() => new(
        new DbContextOptionsBuilder<CmsDbContext>()
            .UseSqlite(_connection)
            .UseCmsModel(_schema, _conventions)
            .Options);

    // EnsureCreated on CmsDbContext would skip the system tables, because they are excluded from its migrations.
    public CmsDbContext CreateCreatedContext()
    {
        using var system = new SystemDbContext(
            new DbContextOptionsBuilder<SystemDbContext>()
                .UseSqlite(_connection)
                .UseSystemModel(_conventions ?? new StorageConventions())
                .Options);

        var context = CreateContext();
        if (system.Database.EnsureCreated())
        {
            context.GetService<IRelationalDatabaseCreator>().CreateTables();
        }

        return context;
    }

    public void Dispose() => _connection.Dispose();
}
