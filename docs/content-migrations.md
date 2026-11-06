# Content migrations

Three streams of migrations share one database, each with its own history table. The migration runner applies them in this order:

| Stream | Owner | History table |
| --- | --- | --- |
| System tables | the data provider package; see [system migrations](system-migrations.md) | `cms__migrations` |
| Plugin tables | each plugin, once the plugin host exists | `cms_{plugin}__migrations` |
| Content tables | the host project | `cms_content__migrations` |

## Adding a content migration

`fluentcms migrations add` reads the schema and the provider from a factory in the host project. It never opens the database named there.

```csharp
public sealed class CmsContextFactory : IDesignTimeDbContextFactory<CmsDbContext>
{
    public CmsDbContext CreateDbContext(string[] args) =>
        ContentMigrations.CreateContext(new SqliteDataProvider("Data Source=:memory:"), MySchema.Build(), typeof(CmsContextFactory).Assembly);
}
```

Then, in the host project:

```bash
fluentcms migrations add AddSubtitle
```

This builds the project and writes the migration to `Migrations/Content`. The runner looks for content migrations in the entry assembly; call `cms.UseContentMigrations(assembly)` inside `AddFluentCms` if they live elsewhere.

## Settings

| Setting | Values | Default |
| --- | --- | --- |
| `FluentCms:Database:MigrateOnStartup` | `true`, `false` | `true` on SQLite, `false` otherwise |
| `FluentCms:Database:SchemaSync` | `Auto`, `Off` | `Auto` in Development on SQLite, `Off` otherwise |

`Auto` anywhere other than Development on SQLite logs a warning and acts as `Off`, unless the host calls `cms.AllowSchemaSyncOutsideDevelopment()` inside `AddFluentCms`: then the default is `Auto` on SQLite in every environment, and `Off` still turns it off. It is for a host whose types have no compiled migrations, such as the [Docker image](docker.md) with a schema file; it cannot be set from configuration, a provider other than SQLite refuses it with `FCMS3154`, and the startup report lists it as `schema-sync-outside-development`. An invalid value stops startup with `FCMS1304`.

With `MigrateOnStartup` set to `false`, startup changes nothing in the database, including schema sync. `MigrationRunner.MigrateAsync` applies every stream whatever the setting, and `MigrationRunner.GetPendingMigrationsAsync` lists what is pending.

## Startup validation

With `MigrateOnStartup` set to `false`, startup first stops with `FCMS1305` if the system or a plugin stream has a pending migration, which is what an upgrade to a new FluentCMS version can bring:

```text
FCMS1305: stream system has not applied 20270101000000_AddSites; run 'fluentcms migrate'
```

Unless schema sync ran, startup then compares the content model with the database and stops with `FCMS1301` if a table or column is missing:

```text
FCMS1301: table cms_article is missing column subtitle; run 'fluentcms migrations add AddSubtitle'
```

It asks for `fluentcms migrate` instead if a content migration exists but is not applied. It checks tables and columns only, not indexes or column types.

## Schema sync

With `Auto`, startup skips the content stream and applies additive changes by comparing the model with the database, so a new field needs no migration while you develop. `MigrationRunner.SyncContentSchemaAsync` does the same on a running host for a schema you pass it.

| Change | Result |
| --- | --- |
| New table, new index | Applied |
| New nullable column, or a `NOT NULL` column with a default such as a list | Applied |
| New `NOT NULL` column with no default, such as a required text, `slug` or `richText` field | Reported |
| Check constraint of a new JSON column | Reported, because SQLite adds one only by rebuilding the table |
| Dropped table, column or index; changed column type, nullability or index | Reported |

Reported changes are logged as warnings and never applied. To get them, recreate the development database.

A database that schema sync has changed has no content history, so the first migration of the content stream will not apply to it. Switch to `Off` on a new database.
