# Plugin migrations

A plugin with tables has a migration stream of its own: the migrations in its assembly that name `PluginDbContext<TPlugin>` in `[DbContext]`, with the history table `cms_{plugin}__migrations` ([ADR 0003](decisions/0003-plugin-contribution-contracts.md)).

## A set per provider

A migration set applies only to the provider it was scaffolded for, because it carries that provider's store types. So one plugin assembly holds a set per provider, each in a namespace that ends in the provider's set name:

| Provider | Set name | Folder | Snapshot |
| --- | --- | --- | --- |
| SQLite | `Sqlite` | `Migrations/Sqlite` | `SqliteModelSnapshot` |
| PostgreSQL | `PostgreSql` | `Migrations/PostgreSql` | `PostgreSqlModelSnapshot` |
| SQL Server | `SqlServer` | `Migrations/SqlServer` | `SqlServerModelSnapshot` |

Each data provider's `ConfigurePluginMigrations` calls `UsePluginMigrationSet` with its set name, and the plugin's context then sees only the migrations and the snapshot in that namespace. The host, the foreign-set check and `dotnet ef` all read the migrations through it. The sets share migration names and steps; their ids differ by the time each was scaffolded. The history table keeps the ids only, so a database never needs to know which namespace a migration came from.

EF finds a context's migrations by the context type, which every provider's set shares here. EF supports several providers with a migrations assembly or a context type per provider, but a plugin is one assembly with one context type, `PluginDbContext<TPlugin>`. The plugin's context therefore replaces EF's `IMigrationsAssembly` with one that keeps a single namespace. `dotnet ef migrations add --output-dir Migrations/PostgreSql` writes that namespace, so scaffolding needs nothing more than the provider argument below.

An assembly with no set under any of these names holds one set, as every plugin did before [#2711](https://github.com/fluentcms/FluentCMS/issues/2711). Every provider takes it. A host on another provider than the one it was scaffolded for stops at startup with `FCMS1306`, before anything is applied, and the message names the set the plugin lacks:

```text
FCMS1306: stream acme was scaffolded for SQLite and has no set in a namespace ending in .PostgreSql; this host runs PostgreSQL, and a migration set applies only to the provider it was scaffolded for
```

Once an assembly has a set under any provider's name, a migration outside those namespaces belongs to no set and never runs, so a migration that every provider needs goes into each set. A plugin that has sets per provider but none for the host's provider, or a set with a snapshot and no migration, stops with `stream acme has no set in a namespace ending in .SqlServer`. A set in a provider's namespace that was scaffolded for another provider is still refused, as `stream acme was scaffolded for SQLite`. A hand-written migration with no target model names no provider and is never refused. Moving an existing database to another provider is not supported.

The PostgreSQL and SQL Server sets call their provider's model builder extensions, such as `UseIdentityByDefaultColumns`. The plugin references `Npgsql.EntityFrameworkCore.PostgreSQL` and `Microsoft.EntityFrameworkCore.SqlServer` with `PrivateAssets="all"`, so it compiles them while its package depends on neither provider. A host loads a set, and the provider assembly it calls, only when it runs on that provider. The plugin also references `System.Security.Cryptography.Pkcs` the same way, as `FluentCMS.Data.SqlServer` does, because `Microsoft.Data.SqlClient` brings a vulnerable version.

On SQL Server, the set is the `nvarchar(max)` form that core's system migrations use, on 2019 and 2025 alike: the webhooks and media plugins keep their JSON in text columns.

## The design-time factory

`PluginDbContext<TPlugin>` is generic, and a plugin references neither Core nor a data provider, so `dotnet ef` cannot build the context from the plugin's project. A startup project that references the plugin and the three providers holds a design-time factory for the closed type, which builds the context as a host does with `PluginDbContextDesignTime.Create`. The argument after `--` names the set to scaffold into. For the webhooks, media, MCP and multi-site plugins, that is the plugin's test project, and for the sample plugin of the tests, `FluentCMS.Plugins.Tests`:

```csharp
public sealed class WebhooksDbContextFactory : IDesignTimeDbContextFactory<PluginDbContext<WebhooksPlugin>>
{
    public PluginDbContext<WebhooksPlugin> CreateDbContext(string[] args) =>
        PluginDbContextDesignTime.Create<WebhooksPlugin>(
            args,
            [
                new SqliteDataProvider("Data Source=:memory:"),
                new PostgreSqlDataProvider("Host=localhost;Database=never_opened"),
                new SqlServerDataProvider("Server=localhost;Database=never_opened"),
            ],
            new WebhookModel());
}
```

The factory names every model contributor the plugin adds. It never opens a database. The startup project references `Microsoft.EntityFrameworkCore.Design` with `PrivateAssets` set to `all`. A missing or unknown set name stops the command with the names it takes.

## Adding a migration

You need the `dotnet-ef` tool (`dotnet tool install --global dotnet-ef`).

1. Change the plugin's model contributor.

2. Scaffold the migration once per set, with the same name, into the folder of that set, with the factory's project as the startup project. `dotnet ef` starts a fresh context each time.

   ```bash
   dotnet ef migrations add AddWebhookFilters --project src/plugins/FluentCMS.Plugins.Webhooks --startup-project tests/FluentCMS.Plugins.Webhooks.Tests --output-dir Migrations/Sqlite -- Sqlite
   ```

   ```bash
   dotnet ef migrations add AddWebhookFilters --project src/plugins/FluentCMS.Plugins.Webhooks --startup-project tests/FluentCMS.Plugins.Webhooks.Tests --output-dir Migrations/PostgreSql -- PostgreSql
   ```

   ```bash
   dotnet ef migrations add AddWebhookFilters --project src/plugins/FluentCMS.Plugins.Webhooks --startup-project tests/FluentCMS.Plugins.Webhooks.Tests --output-dir Migrations/SqlServer -- SqlServer
   ```

   Where the startup project sees more than one plugin context, as `FluentCMS.Plugins.Tests` does, add `--context` with the full name of the closed type, as `dotnet ef dbcontext list` prints it.

3. Add `// <auto-generated />` as the first line of each new migration file, as for the [system migrations](system-migrations.md).

4. Run the plugin's tests. A model ahead of its migrations stops the host with EF's pending model changes error, and the tests of the webhooks, media, MCP, multi-site and sample plugins check that each set matches the model on its provider and makes the same tables, columns and indexes as the SQLite set.

The first migration of a new set needs one more step: rename its snapshot, `PluginDbContextModelSnapshot`, class and file, to the set's name, such as `PostgreSqlModelSnapshot`. `dotnet ef` looks for an existing snapshot file by its name anywhere in the project, so two sets whose snapshots share a name would write over each other. A plugin whose single set is still in `Migrations` moves it to `Migrations/Sqlite`, with its namespace and snapshot renamed, before it scaffolds a set for another provider: until then that set is every provider's, and the new set would be scaffolded on top of it. The history keeps the ids, so nothing changes for a database.

Do not edit a migration that has shipped. The PostgreSQL and SQL Server sets are checked against the model offline, by the plugin's own tests.

## Applying the sets on every provider

The data tests apply the stream of every first-party plugin on the provider of the run (`FLUENTCMS_TEST_PROVIDER`), so the PostgreSQL and SQL Server sets are applied to a server in CI. `PluginStreamTests` in `tests/FluentCMS.Data.Tests/Migrations` migrates a host that adds the plugins, checks that each stream wrote its history table, and reads every table of each plugin through its model, so a table or a column that the model has and the applied set lacks fails on that provider.

A plugin that gets a migration stream is added in two places:

1. a `ProjectReference` to the plugin in `tests/FluentCMS.Data.Tests/FluentCMS.Data.Tests.csproj`, beside the others;
2. a line in `PluginStreamTests.Streams`: `PluginStream.Of<AiAssistPlugin>()`.

A test of that class fails when a project under `src/plugins` has a `Migrations` folder and no line there.

The other data tests start hosts of their own, which must not load these plugins. So the project turns plugin discovery off with the `AppContext` switch `FluentCMS.Plugins.DisableDiscovery`, set in its project file, and the hosts of `PluginStreamTests` add the plugins with `AddPlugin`.
