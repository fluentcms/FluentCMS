# FluentCMS.Plugins.Search.Admin

The admin half of the [FluentCMS](https://github.com/fluentcms/FluentCMS) search plugin, for an admin app built on `FluentCMS.Admin`. The app references it and loads it lazily, the first time one of its contributions is used:

```xml
<ItemGroup>
  <PackageReference Include="FluentCMS.Plugins.Search.Admin" />
  <BlazorWebAssemblyLazyLoad Include="FluentCMS.Plugins.Search.Admin.wasm" />
</ItemGroup>
```

This preview holds the half's first page, `/search`, with its heading only. Section 5.7 of the [admin technical specification](https://github.com/fluentcms/FluentCMS/blob/main/docs/planning/admin-technical-spec.md) has the design.
