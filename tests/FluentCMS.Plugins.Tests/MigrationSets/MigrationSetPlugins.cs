using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FluentCMS.Plugins.Tests.MigrationSets;

// A plugin whose one set is in a namespace that names no provider, as every plugin's was before sets per provider, and was
// scaffolded for SQLite.
[FluentCmsPlugin("legacy", SdkRange = "[1.0.0,2.0.0)")]
public sealed class LegacySetPlugin : IPlugin
{
    public const string MigrationId = "20261108000000_InitialLegacy";

    public void ConfigureServices(IPluginServiceBuilder plugin) => plugin.AddMigrations(typeof(LegacySetPlugin).Assembly);

    public void Configure(IPluginAppBuilder app)
    {
    }
}

// A plugin with a SQLite set, a PostgreSql set that was scaffolded for SQLite too, and no SQL Server set.
[FluentCmsPlugin("partial", SdkRange = "[1.0.0,2.0.0)")]
public sealed class PartialSetPlugin : IPlugin
{
    public const string SqliteMigrationId = "20261108000000_InitialPartial";
    public const string PostgreSqlMigrationId = "20261108000001_InitialPartial";

    public void ConfigureServices(IPluginServiceBuilder plugin) => plugin.AddMigrations(typeof(PartialSetPlugin).Assembly);

    public void Configure(IPluginAppBuilder app)
    {
    }
}

// Makes one table, with the target model that SQLite scaffolds for it: a table and no annotation of another provider. The
// plugins map no model, so they need no snapshot.
public abstract class ItemTableMigration(string table) : Migration
{
    protected override void BuildTargetModel(ModelBuilder modelBuilder) =>
        modelBuilder.Entity("Item", item =>
        {
            item.Property<Guid>("Id").HasColumnName("id");
            item.HasKey("Id");
            item.ToTable(table);
        });

    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            name: table,
            columns: columns => new { id = columns.Column<Guid>(nullable: false) },
            constraints: constraints => constraints.PrimaryKey($"PK_{table}", item => item.id));

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(table);
}

[DbContext(typeof(PluginDbContext<LegacySetPlugin>))]
[Migration(LegacySetPlugin.MigrationId)]
public sealed class InitialLegacy() : ItemTableMigration("cms_legacy_item");
