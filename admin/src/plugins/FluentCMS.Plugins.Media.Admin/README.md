# FluentCMS.Plugins.Media.Admin

The admin half of the [FluentCMS](https://github.com/fluentcms/FluentCMS) media plugin, for an admin app built on `FluentCMS.Admin`. The app references it and loads it lazily, the first time one of its contributions is used:

```xml
<ItemGroup>
  <PackageReference Include="FluentCMS.Plugins.Media.Admin" />
  <BlazorWebAssemblyLazyLoad Include="FluentCMS.Plugins.Media.Admin.wasm" />
</ItemGroup>
```

This preview holds the media library, the `/media` page and its menu item: folders, search, filters, grid and list views, and upload by button, drop and paste; and the latest uploads widget of the home page. Sections 4.14 and 5.2 of the [admin technical specification](https://github.com/fluentcms/FluentCMS/blob/main/docs/planning/admin-technical-spec.md) have the design.
