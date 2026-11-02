using FluentCMS.Core.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FluentCMS.Data.SqlServer;

// Used only by `dotnet ef migrations add`, which never opens this database. See docs/system-migrations.md.
public sealed class SystemDbContextFactory : IDesignTimeDbContextFactory<SystemDbContext>
{
    public SystemDbContext CreateDbContext(string[] args)
    {
        var provider = new SqlServerDataProvider("Server=localhost");
        var options = new DbContextOptionsBuilder<SystemDbContext>();
        provider.ConfigureSystemMigrations(options);
        options.UseSystemModel(provider.Conventions);
        return new SystemDbContext(options.Options);
    }
}
