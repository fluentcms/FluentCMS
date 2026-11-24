# FluentCMS.Plugins.Abstractions

The plugin contracts of [FluentCMS](https://github.com/fluentcms/FluentCMS) that need EF Core or ASP.NET Core: `IPluginModelContributor` and `PluginDbContext<TPlugin>` for a plugin's own tables, `AddModel` and `AddMigrations` for its migration stream, and `MapGroup`, `MapManagement` and `MapDelivery` for its endpoints, with `Takes<T>` to describe in the OpenAPI document a body an endpoint reads by itself.

A plugin that adds rows of a site holds the site for them with `PluginDbContext<TPlugin>.HoldSiteAsync(site)`, in a transaction it began on the context and before the rows, so that a deletion of the site waits for the transaction and the rows go with the plugin's cleanup of `SiteDeletedEvent`; a site that is gone, also on an instance whose cache still has it, is refused with `FCMS2301` (`ValidationException`), and the transaction stays the plugin's to roll back. Without an open transaction the member throws `InvalidOperationException`, since a hold lasts to the end of the transaction. Hold several sites in ascending order of `SiteId.Value`, and never in a content hook: on SQL Server a hold there and a deletion of the same site queued meanwhile wait for each other until a timeout fails the content write. A plain save holds nothing.

```csharp
await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
await database.HoldSiteAsync(site, cancellationToken);
database.Add(row);
await database.SaveChangesAsync(cancellationToken);
await transaction.CommitAsync(cancellationToken);
```

A plugin references this package beside `FluentCMS.Sdk`. Like the SDK, it follows semantic versioning from 1.0.0 on. The `dotnet new fluentcms-plugin` template of `FluentCMS.Templates` starts a plugin with both, and [plugin migrations](https://github.com/fluentcms/FluentCMS/blob/main/docs/plugin-migrations.md) describes the migration stream.
