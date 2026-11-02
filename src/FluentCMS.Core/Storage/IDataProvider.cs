using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Core.Storage;

// Everything a data provider package gives core. Its UseXxx method passes one to FluentCmsBuilder.UseDataProvider.
public interface IDataProvider
{
    string Name { get; }

    // The default of Database:MigrateOnStartup. The migration runner acts on it.
    bool MigrateOnStartup { get; }

    IStorageConventions Conventions { get; }

    IJsonSqlDialect JsonDialect { get; }

    IJobClaimDialect JobClaimDialect { get; }

    // Points the options at the database.
    void ConfigureDbContext(DbContextOptionsBuilder options);

    // The same, for SystemDbContext: adds the assembly that holds the system migrations and their history table.
    void ConfigureSystemMigrations(DbContextOptionsBuilder options);
}
