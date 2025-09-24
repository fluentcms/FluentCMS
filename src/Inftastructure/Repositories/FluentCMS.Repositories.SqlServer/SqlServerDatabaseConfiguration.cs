using FluentCMS.Repositories.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Repositories.SqlServer;

public class SqlServerDatabaseConfiguration(string connectionString) : IDatabaseConfiguration
{
    public void ConfigureDbContext(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlServer(connectionString);
    }
}
