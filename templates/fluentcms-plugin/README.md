# FluentCms-Plugin1

<!--#if (admin) -->
A FluentCMS plugin, its server half and its admin half, made with the `fluentcms-plugin` template of the `FluentCMS.Templates`
package:
<!--#else -->
The server half of a FluentCMS plugin, made with the `fluentcms-plugin` template of the `FluentCMS.Templates` package and
`--admin false`:
<!--#endif -->

```bash
dotnet new install FluentCMS.Templates
dotnet new fluentcms-plugin -n Acme.Reviews
```

The last segment of the name gives the plugin its classes and its name: `Acme.Reviews` makes `ReviewsPlugin` named `reviews`, with the tables `cms_reviews_*`, the settings area `reviews` and the routes under `/api/management/v1/reviews`. A plugin's name is lower kebab-case and at most 32 characters.
<!--#if (admin) -->

The admin half is `ReviewsAdminPlugin` in `Acme.Reviews.Admin`, with its admin pages under `/reviews`. Its name may not be a first segment the admin keeps for its own areas: `content`, `settings`, `account`, `users`, `roles`, `api-keys`, `members`, `sites`, `plugins`, `jobs`, `system` or `authentication`; the manifest test fails for one. `--admin false` makes the server half only:

```bash
dotnet new fluentcms-plugin -n Acme.Reviews --admin false
```
<!--#endif -->

## What is here

| File | Holds |
| --- | --- |
| `src/FluentCms-Plugin1/StarterPlugin.cs` | the plugin class with its manifest, `[FluentCmsPlugin]`, and what it adds in `ConfigureServices` and `Configure`; the assembly attribute lets a host discover it |
| `src/FluentCms-Plugin1/StarterContributions.cs` | a permission, `starter-plugin.notes.manage`, and the settings class of the area `starter-plugin` |
| `src/FluentCms-Plugin1/StarterModel.cs` | the plugin's table, `cms_starter_plugin_note`, mapped on `PluginDbContext<StarterPlugin>` |
| `src/FluentCms-Plugin1/StarterEndpoints.cs` | the Management endpoints `GET` and `POST /api/management/v1/starter-plugin/notes`, which check the permission and the settings; a post counts and adds under the plugin's `IDistributedLock`, so posts sent together cannot pass `MaxNotes` |
| `src/FluentCms-Plugin1/StarterHook.cs` | a hook that runs after an entry of any type is published |
| `src/FluentCms-Plugin1/Migrations/` | the plugin's migration stream, a set per provider: `Sqlite`, `PostgreSql` and `SqlServer` |
| `tests/FluentCms-Plugin1.Tests/` | tests on `FluentCMS.Testing` that boot the plugin in the test host, and the design-time factory of its migrations |
<!--#if (admin) -->
| `src/FluentCms-Plugin1.Admin/StarterAdminPlugin.cs` | the admin half's class, `[FluentCmsAdminPlugin]` with the server plugin's name, and the services it adds in `Configure` |
| `src/FluentCms-Plugin1.Admin/StarterPage.razor` | a page at `/starter-plugin` with its menu item, which lists the notes and needs `starter-plugin.notes.manage` |
| `src/FluentCms-Plugin1.Admin/StarterSettingsPage.razor` | the settings page of the area `starter-plugin`, in place of the generated one |
| `src/FluentCms-Plugin1.Admin/StarterWidget.razor` | a widget of the admin's home page, which counts the notes |
| `src/FluentCms-Plugin1.Admin/StarterApi.cs` | the half's client of the server half's endpoints, on the admin's `IManagementApi`, and its records |
| `src/FluentCms-Plugin1.Admin/StarterAdminResources.resx` | the half's texts, the labels of its menu item and its widget among them |
| `src/FluentCms-Plugin1.Admin/wwwroot/fluentcms-admin.json` | the half's manifest, written by hand: what the admin reads before it loads the half |
| `tests/FluentCms-Plugin1.Admin.Tests/` | the manifest test, and a bUnit test of the page |
<!--#endif -->

The plugin references `FluentCMS.Sdk` and `FluentCMS.Plugins.Abstractions` only, never `FluentCMS.Core`: the host brings core. The EF Core providers of PostgreSQL and SQL Server, the source generator and the analyzers are compiled against with `PrivateAssets="all"`, so the plugin's package depends on none of them. The SDK range of the manifest, `[1.0.0,2.0.0)`, is the versions of the SDK the plugin runs on.
<!--#if (admin) -->

The admin half references `FluentCMS.Admin.Abstractions` and `FluentCMS.Admin.Components` only among the FluentCMS packages, and never the server half: it runs in the browser, and reads the server half through the Management API with records of its own.
<!--#endif -->

## Build and test

```bash
dotnet build
dotnet test
```

The tests start a host on an in-memory SQLite database that discovers the plugin, applies its migrations and serves its endpoints, and check that each migration set matches the model on its provider.
<!--#if (admin) -->
The admin half's tests render its page, and check that `fluentcms-admin.json` and the attributes of the half's assembly agree.

## Change the admin half

The admin reads `wwwroot/fluentcms-admin.json` before it loads the half's assembly, the first time one of its contributions is used, so the file lists every page, menu item, settings page and widget with the full name of its class. When you add, change or remove a contribution attribute, change the file with it: the manifest test names each difference. A page carries its route twice, `@page` for the router and `[AdminPage]` for the manifest, under `/starter-plugin`. A menu item's `labels` in the file are its texts by culture name, which the sidebar shows before the half is loaded, in the interface culture, else its parents', else `en`; `en` is required, and the manifest test fails without it. The admin shows the half's contributions only in a site where the server plugin is enabled, and each only to a user who holds the permission it declares.
<!--#endif -->

## Use it in a host

Reference the project, or the package that `dotnet pack` makes, from a host such as one made with `dotnet new fluentcms`. The host discovers the plugin; `cms.AddPlugin<StarterPlugin>()` inside `AddFluentCms` adds it explicitly and fixes its place in the order. `fluentcms migrate`, or a start on SQLite, applies its migrations, in the history table `cms_starter_plugin__migrations`. A role needs `starter-plugin.notes.manage` to use the endpoints, and `settings.starter-plugin.manage` to change the settings at `/api/management/v1/settings/starter-plugin`.

## Change the table

You need the `dotnet-ef` tool (`dotnet tool install --global dotnet-ef`). Change `StarterModel`, then scaffold the migration once per set, with the test project as the startup project:

```bash
dotnet ef migrations add AddRating --project src/FluentCms-Plugin1 --startup-project tests/FluentCms-Plugin1.Tests --output-dir Migrations/Sqlite -- Sqlite
dotnet ef migrations add AddRating --project src/FluentCms-Plugin1 --startup-project tests/FluentCms-Plugin1.Tests --output-dir Migrations/PostgreSql -- PostgreSql
dotnet ef migrations add AddRating --project src/FluentCms-Plugin1 --startup-project tests/FluentCms-Plugin1.Tests --output-dir Migrations/SqlServer -- SqlServer
```

Run the tests: a set behind the model fails them. Do not edit a migration that has shipped. The [plugin migrations](https://github.com/fluentcms/FluentCMS/blob/main/docs/plugin-migrations.md) guide has the rest.
<!--#if (admin) -->

## Use the admin half in an admin app

The prebuilt admin that `FluentCMS.Admin.Hosting` serves holds the first-party halves only, so a plugin's own half runs in an admin app of your own, which `dotnet new fluentcms-admin` makes with the first-party halves:

```bash
dotnet new fluentcms-admin -n Acme.AdminApp
```

Reference the half's project, or the package that `dotnet pack` makes, from that app, and list its assembly as lazy, so that it is downloaded the first time it is used:

```xml
<ItemGroup>
  <PackageReference Include="FluentCms-Plugin1.Admin" Version="1.0.0" />
  <BlazorWebAssemblyLazyLoad Include="FluentCms-Plugin1.Admin.wasm" />
</ItemGroup>
```

The app's build writes the `fluentcms-admin.json` of every half it references into `fluentcms-admin.plugins.json`, and stops when a half is not listed as lazy. A host with `FluentCMS.Admin.Hosting`, such as one of `dotnet new fluentcms`, serves the app at `/admin` once it references the app's project, and the app's README says how to host it as static files instead. The admin needs no change to its code for the half: in a site where the plugin is enabled, the sidebar shows the menu item to a user who holds the page's permission, and the page loads the half the first time it is opened. A half the admin cannot use, such as one whose admin range does not hold the running admin, is left out and listed on `/system/plugins`.
<!--#endif -->
