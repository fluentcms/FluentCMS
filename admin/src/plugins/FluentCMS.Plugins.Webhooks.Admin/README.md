# FluentCMS.Plugins.Webhooks.Admin

The admin half of the [FluentCMS](https://github.com/fluentcms/FluentCMS) webhooks plugin, for an admin app built on `FluentCMS.Admin`. The app references it and loads it lazily, the first time one of its contributions is used:

```xml
<ItemGroup>
  <PackageReference Include="FluentCMS.Plugins.Webhooks.Admin" />
  <BlazorWebAssemblyLazyLoad Include="FluentCMS.Plugins.Webhooks.Admin.wasm" />
</ItemGroup>
```

This preview holds the half's first page, `/webhooks`, with its heading only. Section 5.3 of the [admin technical specification](https://github.com/fluentcms/FluentCMS/blob/main/docs/planning/admin-technical-spec.md) has the design.
