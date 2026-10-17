using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FluentCMS.Infrastructure.Persistence;

// Used by `dotnet ef` to build the model for migrations; no database is opened.
internal sealed class SqliteDesignTimeFactory : IDesignTimeDbContextFactory<SqliteFluentCmsDbContext>
{
    public SqliteFluentCmsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SqliteFluentCmsDbContext>()
            .UseSqlite("Data Source=design-time.db")
            .Options;

        return new SqliteFluentCmsDbContext(options);
    }
}

internal sealed class PostgreSqlDesignTimeFactory : IDesignTimeDbContextFactory<PostgreSqlFluentCmsDbContext>
{
    public PostgreSqlFluentCmsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PostgreSqlFluentCmsDbContext>()
            .UseNpgsql("Host=localhost;Database=design-time")
            .Options;

        return new PostgreSqlFluentCmsDbContext(options);
    }
}
