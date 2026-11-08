using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FluentCMS.Plugins.Tests.MigrationSets.Sqlite;

[DbContext(typeof(PluginDbContext<PartialSetPlugin>))]
[Migration(PartialSetPlugin.SqliteMigrationId)]
public sealed class InitialPartial() : ItemTableMigration("cms_partial_item");
