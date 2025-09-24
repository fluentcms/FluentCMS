using FluentCMS.Repositories.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Repositories.Sqlite;

public class SqliteDatabaseConfiguration(string connectionString) : IDatabaseConfiguration
{
    public void ConfigureDbContext(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite(connectionString);
    }
}
