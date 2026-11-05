using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Core.Storage;

// Everything a data provider package gives core. Its UseXxx method passes one to FluentCmsBuilder.UseDataProvider.
public interface IDataProvider
{
    string Name { get; }

    // The default of FluentCms:Database:MigrateOnStartup.
    bool MigrateOnStartup { get; }

    // Whether FluentCms:Database:SchemaSync may be Auto in Development.
    bool SupportsSchemaSync { get; }

    IStorageConventions Conventions { get; }

    IJsonSqlDialect JsonDialect { get; }

    IJobClaimDialect JobClaimDialect { get; }

    IDatabaseSchemaReader SchemaReader { get; }

    // Points the options of CmsDbContext at the database, with the host's content migrations and their history table.
    void ConfigureContentMigrations(DbContextOptionsBuilder options, Assembly migrationsAssembly);

    // The same, for SystemDbContext: adds the assembly that holds the system migrations and their history table.
    void ConfigureSystemMigrations(DbContextOptionsBuilder options);
}
