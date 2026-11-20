using FluentCMS.Core.Storage;
using FluentCMS.Data.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FluentCMS.Data.Tests.Providers;

// PostgreSQL copies a database far quicker than it applies the system migrations to one, so a test that applies them first
// gets a copy of a database that already holds them. The template is migrated once, when the first such test asks for it.
// PostgreSQL refuses to copy a database that has a session, so the template is only ever reached without pooling.
// migrate: the migration of the template, from its connection string; the system migrations unless a test of this class sets it.
internal sealed class PostgreSqlTemplate(string admin, string name, Action<string>? migrate = null)
{
    private readonly Lock _lock = new();
    private bool _migrated;
    private Exception? _failure;

    public string Name => name;

    // How many times the template was migrated, which a run does once.
    public int Migrations { get; private set; }

    public string Copy()
    {
        EnsureMigrated();
        var copy = $"t_{Guid.NewGuid():N}";
        DatabaseServer.Run(new NpgsqlConnection(admin), $"CREATE DATABASE \"{copy}\" TEMPLATE \"{name}\"");
        return copy;
    }

    public static SystemDbContext SystemContext(string connectionString)
    {
        var provider = new PostgreSqlDataProvider(connectionString);
        var options = new DbContextOptionsBuilder<SystemDbContext>();
        provider.ConfigureSystemMigrations(options);
        options.UseSystemModel(provider.Conventions);
        return new SystemDbContext(options.Options);
    }

    private static void MigrateSystemTables(string connectionString)
    {
        using var context = SystemContext(connectionString);
        context.Database.Migrate();
    }

    private void EnsureMigrated()
    {
        lock (_lock)
        {
            if (_migrated)
            {
                return;
            }

            // A migration that failed once fails every test that asks after it at once, instead of each trying it again in turn.
            if (_failure is not null)
            {
                throw new InvalidOperationException($"The template database {name} failed to migrate earlier in this run.", _failure);
            }

            try
            {
                DatabaseServer.Run(new NpgsqlConnection(admin), $"CREATE DATABASE \"{name}\"");
                (migrate ?? MigrateSystemTables)(new NpgsqlConnectionStringBuilder(admin) { Database = name, Pooling = false }.ToString());
            }
            catch (Exception exception)
            {
                _failure = exception;
                DropAfterFailure();
                throw;
            }

            Migrations++;
            _migrated = true;
        }
    }

    // A failure to drop the half-migrated template must not hide the migration's own.
    private void DropAfterFailure()
    {
        try
        {
            DatabaseServer.Run(new NpgsqlConnection(admin), $"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)");
        }
        catch (NpgsqlException)
        {
        }
    }
}
