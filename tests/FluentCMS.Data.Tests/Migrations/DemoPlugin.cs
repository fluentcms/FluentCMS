using FluentCMS.Core.Storage;
using FluentCMS.Data.Tests.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Data.Tests.Migrations;

// A plugin stream through the seam the plugin host of M3 will use. Its model is empty, so that the one hand-written
// migration needs no snapshot.
internal sealed class DemoPlugin : IPluginMigrations
{
    public const string PluginName = "demo";
    public const string Table = "cms_demo_item";
    public const string HistoryTable = "cms_demo__migrations";
    public const string MigrationId = "20261102000000_InitialDemo";

    public string Name => PluginName;

    public static void Add(IServiceCollection services, TestDatabase database)
    {
        services.AddDbContext<DemoPluginDbContext>(options => database.UseDriver(options, typeof(DemoPluginDbContext).Assembly, HistoryTable));
        services.AddSingleton<IPluginMigrations, DemoPlugin>();
    }

    public DbContext ResolveContext(IServiceProvider scopedServices) => scopedServices.GetRequiredService<DemoPluginDbContext>();
}

internal sealed class DemoPluginDbContext(DbContextOptions<DemoPluginDbContext> options) : DbContext(options);

[DbContext(typeof(DemoPluginDbContext))]
[Migration(DemoPlugin.MigrationId)]
internal sealed class InitialDemo : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            name: DemoPlugin.Table,
            columns: table => new { id = table.Column<Guid>(nullable: false) },
            constraints: table => table.PrimaryKey("PK_cms_demo_item", item => item.id));

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(DemoPlugin.Table);
}
