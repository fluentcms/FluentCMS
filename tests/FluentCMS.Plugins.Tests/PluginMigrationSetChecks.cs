using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Plugins;
using FluentCMS.Core.Storage;
using FluentCMS.Data.PostgreSql;
using FluentCMS.Data.Sqlite;
using FluentCMS.Data.SqlServer;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Plugins.Tests;

// What every plugin's migration sets hold, checked without a database (#2711). The webhooks and media tests link this file.
internal static class PluginMigrationSetChecks
{
    public static TheoryData<string> Sets => [PluginMigrationSets.Sqlite, PluginMigrationSets.PostgreSql, PluginMigrationSets.SqlServer];

    // The services of a host on the provider of the set, which never open its database.
    public static ServiceProvider Host(string set, Action<FluentCmsBuilder> plugins)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddFluentCms(cms =>
        {
            if (set == PluginMigrationSets.Sqlite)
            {
                cms.UseSqlite("Data Source=:memory:");
            }
            else if (set == PluginMigrationSets.PostgreSql)
            {
                cms.UsePostgreSql("Host=localhost;Database=never_opened");
            }
            else
            {
                cms.UseSqlServer("Server=localhost;Database=never_opened");
            }

            plugins(cms);
        });

        return services.BuildServiceProvider();
    }

    // The context's migrations are its provider's set alone, which matches its model and which the runner takes.
    public static void IsTheSetOf(DbContext context, string set)
    {
        var migrations = context.GetService<IMigrationsAssembly>();
        var snapshot = migrations.ModelSnapshot.ShouldNotBeNull().GetType();

        migrations.Migrations.ShouldNotBeEmpty();
        migrations.Migrations.Values.ShouldAllBe(type => type.Namespace!.EndsWith("." + set, StringComparison.Ordinal));
        snapshot.Namespace.ShouldEndWith("." + set);
        snapshot.Name.ShouldBe(set + "ModelSnapshot");
        context.Database.HasPendingModelChanges().ShouldBeFalse();
        MigrationProviders.RefusalOf(context).ShouldBeNull();
    }

    // Each migration of the set makes the tables, columns, keys and indexes of the SQLite migration of the same name, so a
    // database of any provider goes through the same steps. Store types and index filters are the provider's own: SQL Server
    // adds IS NOT NULL to a unique index over a nullable column.
    public static void MatchesTheSqliteSet(DbContext context, DbContext sqlite) =>
        Operations(context).ShouldBe(Operations(sqlite));

    private static List<string> Operations(DbContext context)
    {
        var migrations = context.GetService<IMigrationsAssembly>();
        return [.. migrations.Migrations.SelectMany(migration => migrations.CreateMigration(migration.Value, context.Database.ProviderName!).UpOperations
            .Select(operation => $"{migration.Key[15..]}: {Describe(operation)}"))];
    }

    private static string Describe(MigrationOperation operation) => operation switch
    {
        CreateTableOperation table =>
            $"create table {table.Name} ({string.Join(", ", table.Columns.Select(Describe))}) key ({string.Join(", ", table.PrimaryKey!.Columns)})",
        AddColumnOperation column => $"add column {Describe(column)} to {column.Table}",
        CreateIndexOperation index => $"create {(index.IsUnique ? "unique " : "")}index {index.Name} on {index.Table} ({string.Join(", ", index.Columns)})",
        _ => operation.GetType().Name,
    };

    private static string Describe(AddColumnOperation column) =>
        $"{column.Name}{(column.MaxLength is { } length ? $"({length})" : "")}{(column.IsNullable ? " null" : "")}";
}
