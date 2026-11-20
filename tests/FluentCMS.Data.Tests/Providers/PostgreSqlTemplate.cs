using FluentCMS.Core.Storage;
using FluentCMS.Data.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FluentCMS.Data.Tests.Providers;

// PostgreSQL copies a database far quicker than it applies the system migrations to one, so a test that applies them first
// gets a copy of a database that already holds them. The template is migrated once, when the first such test asks for it.
// PostgreSQL refuses to copy a database that has a session, so the template is only ever reached without pooling.
internal sealed class PostgreSqlTemplate(string admin, string name)
{
    private readonly Lock _lock = new();
    private bool _migrated;

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

    private void EnsureMigrated()
    {
        lock (_lock)
        {
            if (_migrated)
            {
                return;
            }

            try
            {
                DatabaseServer.Run(new NpgsqlConnection(admin), $"CREATE DATABASE \"{name}\"");
                using (var context = SystemContext(new NpgsqlConnectionStringBuilder(admin) { Database = name, Pooling = false }.ToString()))
                {
                    context.Database.Migrate();
                }
            }
            catch
            {
                // The next test that asks migrates it again instead of copying half a migration.
                DatabaseServer.Run(new NpgsqlConnection(admin), $"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)");
                throw;
            }

            Migrations++;
            _migrated = true;
        }
    }
}
