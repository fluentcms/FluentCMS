# FluentCMS.Plugins.Mcp.Admin

The admin half of the [FluentCMS](https://github.com/fluentcms/FluentCMS) MCP plugin, for an admin app built on `FluentCMS.Admin`. The app references it and loads it lazily, the first time one of its contributions is used:

```xml
<ItemGroup>
  <PackageReference Include="FluentCMS.Plugins.Mcp.Admin" />
  <BlazorWebAssemblyLazyLoad Include="FluentCMS.Plugins.Mcp.Admin.wasm" />
</ItemGroup>
```

This preview holds the half's first page, `/mcp/clients`, with its heading only. Section 5.4 of the [admin technical specification](https://github.com/fluentcms/FluentCMS/blob/main/docs/planning/admin-technical-spec.md) has the design.
