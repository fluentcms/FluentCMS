# FluentCmsPlugin1

The server half of a FluentCMS plugin, made with the `fluentcms-plugin` template of the `FluentCMS.Templates` package:

```bash
dotnet new install FluentCMS.Templates
dotnet new fluentcms-plugin -n Acme.Reviews
```

The last segment of the name gives the plugin its classes and its name: `Acme.Reviews` makes `ReviewsPlugin` named `reviews`, with the tables `cms_reviews_*`, the settings area `reviews` and the routes under `/api/management/v1/reviews`. A plugin's name is lower kebab-case and at most 32 characters. The admin half of a plugin comes with the admin.

## What is here

| File | Holds |
| --- | --- |
| `src/FluentCmsPlugin1/StarterPlugin.cs` | the plugin class with its manifest, `[FluentCmsPlugin]`, and what it adds in `ConfigureServices` and `Configure`; the assembly attribute lets a host discover it |
| `src/FluentCmsPlugin1/StarterContributions.cs` | a permission, `starter-plugin.notes.manage`, and the settings class of the area `starter-plugin` |
| `src/FluentCmsPlugin1/StarterModel.cs` | the plugin's table, `cms_starter_plugin_note`, mapped on `PluginDbContext<StarterPlugin>` |
| `src/FluentCmsPlugin1/StarterEndpoints.cs` | the Management endpoints `GET` and `POST /api/management/v1/starter-plugin/notes`, which check the permission and the settings |
| `src/FluentCmsPlugin1/StarterHook.cs` | a hook that runs after an entry of any type is published |
| `src/FluentCmsPlugin1/Migrations/` | the plugin's migration stream, a set per provider: `Sqlite`, `PostgreSql` and `SqlServer` |
| `tests/FluentCmsPlugin1.Tests/` | tests on `FluentCMS.Testing` that boot the plugin in the test host, and the design-time factory of its migrations |

The plugin references `FluentCMS.Sdk` and `FluentCMS.Plugins.Abstractions` only, never `FluentCMS.Core`: the host brings core. The EF Core providers of PostgreSQL and SQL Server, the source generator and the analyzers are compiled against with `PrivateAssets="all"`, so the plugin's package depends on none of them. The SDK range of the manifest, `[1.0.0,2.0.0)`, is the versions of the SDK the plugin runs on.

## Build and test

```bash
dotnet build
dotnet test
```

The tests start a host on an in-memory SQLite database that discovers the plugin, applies its migrations and serves its endpoints, and check that each migration set matches the model on its provider.

## Use it in a host

Reference the project, or the package that `dotnet pack` makes, from a host such as one made with `dotnet new fluentcms`. The host discovers the plugin; `cms.AddPlugin<StarterPlugin>()` inside `AddFluentCms` adds it explicitly and fixes its place in the order. `fluentcms migrate`, or a start on SQLite, applies its migrations, in the history table `cms_starter_plugin__migrations`. A role needs `starter-plugin.notes.manage` to use the endpoints, and `settings.starter-plugin.manage` to change the settings at `/api/management/v1/settings/starter-plugin`.

## Change the table

You need the `dotnet-ef` tool (`dotnet tool install --global dotnet-ef`). Change `StarterModel`, then scaffold the migration once per set, with the test project as the startup project:

```bash
dotnet ef migrations add AddRating --project src/FluentCmsPlugin1 --startup-project tests/FluentCmsPlugin1.Tests --output-dir Migrations/Sqlite -- Sqlite
dotnet ef migrations add AddRating --project src/FluentCmsPlugin1 --startup-project tests/FluentCmsPlugin1.Tests --output-dir Migrations/PostgreSql -- PostgreSql
dotnet ef migrations add AddRating --project src/FluentCmsPlugin1 --startup-project tests/FluentCmsPlugin1.Tests --output-dir Migrations/SqlServer -- SqlServer
```

Run the tests: a set behind the model fails them. Do not edit a migration that has shipped. The [plugin migrations](https://github.com/fluentcms/FluentCMS/blob/main/docs/plugin-migrations.md) guide has the rest.
