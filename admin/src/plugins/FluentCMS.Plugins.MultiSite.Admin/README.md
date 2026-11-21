# FluentCMS.Plugins.MultiSite.Admin

The admin half of the [FluentCMS](https://github.com/fluentcms/FluentCMS) multi-site plugin, for an admin app built on `FluentCMS.Admin`. The app references it and loads it lazily, the first time one of its contributions is used:

```xml
<ItemGroup>
  <PackageReference Include="FluentCMS.Plugins.MultiSite.Admin" />
  <BlazorWebAssemblyLazyLoad Include="FluentCMS.Plugins.MultiSite.Admin.wasm" />
</ItemGroup>
```

This preview holds the half's first page, `/multi-site/sites`, with its heading only. Section 5.6 of the [admin technical specification](https://github.com/fluentcms/FluentCMS/blob/main/docs/planning/admin-technical-spec.md) has the design.
