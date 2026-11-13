# FluentCMS.Plugins.Abstractions

The plugin contracts of [FluentCMS](https://github.com/fluentcms/FluentCMS) that need EF Core or ASP.NET Core: `IPluginModelContributor` and `PluginDbContext<TPlugin>` for a plugin's own tables, `AddModel` and `AddMigrations` for its migration stream, and `MapGroup`, `MapManagement` and `MapDelivery` for its endpoints.

A plugin references this package beside `FluentCMS.Sdk`. Like the SDK, it follows semantic versioning from 1.0.0 on. The `dotnet new fluentcms-plugin` template of `FluentCMS.Templates` starts a plugin with both, and [plugin migrations](https://github.com/fluentcms/FluentCMS/blob/main/docs/plugin-migrations.md) describes the migration stream.
