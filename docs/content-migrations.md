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

This builds the project and writes the migration to `Migrations/Content`. `migrations add` and `migrations remove` run in the tool's own process, because they need EF Core's design-time package, which a host does not get: the tool loads the host beside its own FluentCMS.Core and EF Core, so a host built against another version stops with both versions named, such as `MyHost could not be used by this fluentcms, which has FluentCMS.Core 1.2.0; the host has 1.3.0, so use fluentcms 1.3.0`. The other commands run inside the host and have no such limit. The runner looks for content migrations in the entry assembly; call `cms.UseContentMigrations(assembly)` inside `AddFluentCms` if they live elsewhere.

`fluentcms migrations remove` deletes the newest content migration and writes the snapshot back as the migration before it left it. It first asks the host, with `fluentcms migrations list`, whether its database has the migration, and refuses with `FCMS3314` when it has: add a migration that undoes it instead. `--force` removes the files without asking, and leaves the database as it is.

The migrations are for the provider the factory names, and they carry its column types. Startup and `MigrationRunner.MigrateAsync` refuse a content or plugin set scaffolded for another provider with `FCMS1306`, before anything is applied:

```text
FCMS1306: stream content was scaffolded for SQLite; this host runs PostgreSQL, and a migration set applies only to the provider it was scaffolded for
```

Moving an existing database to another provider is not supported. For another provider, scaffold a new content set with a factory for that provider and start from a new database. Schema sync skips the content stream, so a host can sync on SQLite in Development while its content migrations are for its server provider.

## Settings

| Setting | Values | Default |
| --- | --- | --- |
| `FluentCms:Database:MigrateOnStartup` | `true`, `false` | `true` on SQLite, `false` otherwise |
| `FluentCms:Database:SchemaSync` | `Auto`, `Off` | `Auto` in Development on SQLite, `Off` otherwise |

`Auto` anywhere other than Development on SQLite logs a warning and acts as `Off`, unless the host calls `cms.AllowSchemaSyncOutsideDevelopment()` inside `AddFluentCms`: then the default is `Auto` on SQLite in every environment, and `Off` still turns it off. It is for a host whose types have no compiled migrations, such as the [Docker image](docker.md) with a schema file; it cannot be set from configuration, a provider other than SQLite refuses it with `FCMS3154`, and the startup report lists it as `schema-sync-outside-development`. An invalid value stops startup with `FCMS1304`.

With `MigrateOnStartup` set to `false`, startup changes nothing in the database, including schema sync. Apply what is pending with `fluentcms migrate`, below, or start one instance with `MigrateOnStartup` set to `true`.

## Applying migrations

The commands below run inside the host, so they use its configuration, its provider and its plugins. In the project directory, the tool builds the project and starts it with `fluentcms <command>`; `--project` names another directory, `--no-build` runs the last build, `--assembly` names a built host and `--environment` the environment it runs in. The host must call the command entry point in `Program.cs`, between `Build()` and `Run()`:

```csharp
var app = builder.Build();
if (await app.RunFluentCmsCommandAsync(args) is { } exitCode)
{
    return exitCode;
}

app.UseFluentCms();
app.MapFluentCms();
await app.RunAsync();
return 0;
```

A command runs before any hosted service starts, so nothing migrates on start, listens, runs jobs or seeds, and the process ends with the command's exit code: 0, 1 for a failure, 2 for bad usage. A host started with `fluentcms` arguments that does not call the entry point stops before it changes anything, with `FCMS3301` and the line to add. A built host runs the same commands without the tool, as `dotnet MyHost.dll fluentcms migrate`, which is how the [Docker image](docker.md#migrate-before-the-host-starts) runs them in an init container.

| Command | Does |
| --- | --- |
| `fluentcms migrate` | Applies the pending migrations of every stream in the order system, plugins, content, lists them under their stream, and records the schema in the schema history. Instances that migrate one database at once apply each migration once; on SQL Server, a run that waits while another turns `READ_COMMITTED_SNAPSHOT` on is disconnected and succeeds when run again ([ADR 0005](decisions/0005-sql-server-read-committed-snapshot.md)). |
| `fluentcms migrate --check` | Lists the pending migrations of every stream and exits with 1 when any is pending (`FCMS3303`), and with 0 when none is. |
| `fluentcms migrations list [--json]` | Every stream with its applied and pending migrations. |
| `fluentcms migrations script [--stream <name>] [--from <id>] [--to <id>] [--idempotent] [--output <file>]` | The SQL of the host's provider, for a database that only an administrator may change. Without `--stream`, every stream in order; `--from` and `--to` need `--stream`, and `0` is before the first migration. `--idempotent` checks each migration against the history, on PostgreSQL and SQL Server only (`FCMS3306` on SQLite). It opens no database, and records no schema history, which the next `fluentcms migrate` or a start with `MigrateOnStartup` does. |

The tool sends the host's console logs to standard error, so that standard output holds only what the command writes. Neither `migrate` nor the other commands need `MigrateOnStartup`.

## Startup validation

With `MigrateOnStartup` set to `false`, startup first stops with `FCMS1305` if the system or a plugin stream has a pending migration, which is what an upgrade to a new FluentCMS version can bring:

```text
FCMS1305: stream system has not applied 20270101000000_AddSites; run 'fluentcms migrate' to apply the pending migrations, or set FluentCms:Database:MigrateOnStartup to true
```

Unless schema sync ran, startup then compares the content model with the database and stops with `FCMS1301` if a table or column is missing:

```text
FCMS1301: table cms_article is missing column subtitle; run 'fluentcms migrations add AddSubtitle'
```

If a content migration exists but is not applied, it asks to run `fluentcms migrate` instead, with the startup option as the alternative. It checks tables and columns only, not indexes or column types.

## Schema sync

With `Auto`, startup skips the content stream and applies additive changes by comparing the model with the database, so a new field needs no migration while you develop. `MigrationRunner.SyncContentSchemaAsync` does the same on a running host for a schema you pass it.

| Change | Result |
| --- | --- |
| New table, new index | Applied |
| New nullable column, or a `NOT NULL` column with a default such as a list | Applied |
| New `NOT NULL` column with no default, such as a required text, `slug` or `richText` field | Reported |
| Check constraint of a new JSON column | Reported, because SQLite adds one only by rebuilding the table |
| Dropped table, column or index; changed column type, nullability or index | Reported |

Reported changes are logged as warnings and never applied. To get them, recreate the development database. Instances that start together on one database apply each change once: an instance with something to apply takes the `fluentcms:schema-sync` lock and compares again before it applies. A table under a plugin's prefix (`cms_{plugin}_`) belongs to that plugin's stream, so schema sync never reports it as a dropped table.

A database that schema sync has changed has no content history, so the first migration of the content stream will not apply to it. Switch to `Off` on a new database.
