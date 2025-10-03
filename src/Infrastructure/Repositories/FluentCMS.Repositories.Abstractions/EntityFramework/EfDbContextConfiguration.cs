using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Repositories.EntityFramework;

public static class EfDbContextConfiguration
{
    // Create DbContextOptions for a specific EF provider and connection string
    public static DbContextOptions CreateDbContextOptions(string providerName, string connectionString)
    {
        var builder = new DbContextOptionsBuilder();

        switch (providerName)
        {
            case "Sqlite":
                builder.UseSqlite(connectionString); // Assumes FluentCMS.Repositories.Sqlite includes the Microsoft.EntityFrameworkCore.Sqlite package
                break;
            case "SqlServer":
                builder.UseSqlServer(connectionString); // Assumes FluentCMS.Repositories.SqlServer includes the Microsoft.EntityFrameworkCore.SqlServer package
                break;
            default:
                throw new InvalidOperationException($"Unknown EF provider: {providerName}");
        }

        return builder.Options;
    }
}
