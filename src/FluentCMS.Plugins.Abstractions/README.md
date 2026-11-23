# FluentCMS.Plugins.Abstractions

The plugin contracts of [FluentCMS](https://github.com/fluentcms/FluentCMS) that need EF Core or ASP.NET Core: `IPluginModelContributor` and `PluginDbContext<TPlugin>` for a plugin's own tables, `AddModel` and `AddMigrations` for its migration stream, and `MapGroup`, `MapManagement` and `MapDelivery` for its endpoints, with `Takes<T>` to describe in the OpenAPI document a body an endpoint reads by itself.

A save of `PluginDbContext<TPlugin>` that adds a row with a `site_id` column holds that site until it commits, so a site's deletion waits for it and the plugin's cleanup of `SiteDeletedEvent` finds the row; a save for a site that is gone is refused with `FCMS2301`. What that means for a plugin:

- A `Guid` column named `site_id` is always taken for a site of FluentCMS, with no opt-out: a value that is no site is refused. A site held in a value object, a member of a complex type or a column of another name is not guarded, and neither is a row written with `ExecuteSql` or `ExecuteUpdate`, nor one whose site is its parent row's.
- A modified row is held when its `site_id` is marked modified, and `Update(entity)` marks every property, so it takes the hold too.
- Such a save runs in a transaction: one of its own (a begin, the lock and a commit, up to three more round trips for a single insert), or the plugin's own, an ambient `TransactionScope` or an enlisted one, to whose end the site stays held. `IEventBus.PublishAsync` while it is open is refused, as inside any transaction, and a long transaction of the plugin keeps the site's deletion waiting.
- A data provider whose `LockSiteAsync` reads `context.Set<Site>()` must accept a context whose model has no `Site`, as a plugin's has none.

A plugin references this package beside `FluentCMS.Sdk`. Like the SDK, it follows semantic versioning from 1.0.0 on. The `dotnet new fluentcms-plugin` template of `FluentCMS.Templates` starts a plugin with both, and [plugin migrations](https://github.com/fluentcms/FluentCMS/blob/main/docs/plugin-migrations.md) describes the migration stream.
