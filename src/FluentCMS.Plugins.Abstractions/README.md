# FluentCMS.Plugins.Abstractions

The plugin contracts of [FluentCMS](https://github.com/fluentcms/FluentCMS) that need EF Core or ASP.NET Core: `IPluginModelContributor` and `PluginDbContext<TPlugin>` for a plugin's own tables, `AddModel` and `AddMigrations` for its migration stream, and `MapGroup`, `MapManagement` and `MapDelivery` for its endpoints, with `Takes<T>` to describe in the OpenAPI document a body an endpoint reads by itself.

A save of `PluginDbContext<TPlugin>` that adds a row with a `site_id` column holds that site until it commits, so a site's deletion waits for it and the plugin's cleanup of `SiteDeletedEvent` finds the row; a save for a site that is gone is refused with `FCMS2301`. A row written another way, or whose site is its parent row's, is not held.

A plugin references this package beside `FluentCMS.Sdk`. Like the SDK, it follows semantic versioning from 1.0.0 on. The `dotnet new fluentcms-plugin` template of `FluentCMS.Templates` starts a plugin with both, and [plugin migrations](https://github.com/fluentcms/FluentCMS/blob/main/docs/plugin-migrations.md) describes the migration stream.
