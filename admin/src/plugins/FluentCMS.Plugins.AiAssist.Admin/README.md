# FluentCMS.Plugins.AiAssist.Admin

The admin half of the [FluentCMS](https://github.com/fluentcms/FluentCMS) AI assist plugin, for an admin app built on `FluentCMS.Admin`. The app references it and loads it lazily, the first time one of its contributions is used:

```xml
<ItemGroup>
  <PackageReference Include="FluentCMS.Plugins.AiAssist.Admin" />
  <BlazorWebAssemblyLazyLoad Include="FluentCMS.Plugins.AiAssist.Admin.wasm" />
</ItemGroup>
```

This preview holds the half's first page, `/ai/usage`, with its heading only. Section 5.5 of the [admin technical specification](https://github.com/fluentcms/FluentCMS/blob/main/docs/planning/admin-technical-spec.md) has the design.
