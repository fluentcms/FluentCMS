using FluentCMS.Repositories.EntityFramework.Extensions;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Repositories.Sqlite;

public class SqliteDatabaseConfiguration(string connectionString) : IDatabaseConfiguration
{
    public void ConfigureDbContext(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite(connectionString);
    }
}
