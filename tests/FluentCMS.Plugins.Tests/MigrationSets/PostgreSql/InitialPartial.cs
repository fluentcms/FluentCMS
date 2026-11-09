using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FluentCMS.Plugins.Tests.MigrationSets.PostgreSql;

[DbContext(typeof(PluginDbContext<PartialSetPlugin>))]
[Migration(PartialSetPlugin.PostgreSqlMigrationId)]
public sealed class InitialPartial() : ItemTableMigration("cms_partial_item");
