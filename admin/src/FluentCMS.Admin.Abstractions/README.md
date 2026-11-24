# FluentCMS.Admin.Abstractions

The contract of an admin half of [FluentCMS](https://github.com/fluentcms/FluentCMS), a headless CMS for .NET: the manifest, the contribution attributes, the base classes of field editors, list cells and actions, and the services and records that an admin component may use. A plugin's admin half compiles against it and `FluentCMS.Admin.Components` only.

It follows semantic versioning from 1.0.0 on, tracked by the public API analyzer and package validation, as `FluentCMS.Sdk` is. Previews may break it between previews. Section 5.1 of the [admin technical specification](https://github.com/fluentcms/FluentCMS/blob/main/docs/planning/admin-technical-spec.md) has the design, and [writing an admin half](https://github.com/fluentcms/FluentCMS/blob/main/docs/admin-plugins.md) the guide, with each kind of contribution.

An admin half names its plugin with `[assembly: FluentCmsAdminPlugin(typeof(...))]`, declares its contributions with attributes (`[AdminPage]`, `[AdminMenuItem]`, `[SettingsPage]`, `[FieldEditor]`, `[ListCell]`, `[EntryAction]`, `[BulkAction]`, `[FieldAction]`, `[DashboardWidget]`, `[AdminService]`), and lists the same in `wwwroot/fluentcms-admin.json`, which the shell reads before it loads the half. A test of the half, whose project copies the file to its output, holds the two together:

```csharp
AdminPluginManifestCheck.VerifyFile(typeof(ReviewsAdminPlugin).Assembly, Path.Combine(AppContext.BaseDirectory, AdminPluginManifest.FileName));
```
