# FluentCMS.Plugins.Mcp.Admin

The admin half of the [FluentCMS](https://github.com/fluentcms/FluentCMS) MCP plugin, for an admin app built on `FluentCMS.Admin`. The app references it and loads it lazily, the first time one of its contributions is used:

```xml
<ItemGroup>
  <PackageReference Include="FluentCMS.Plugins.Mcp.Admin" />
  <BlazorWebAssemblyLazyLoad Include="FluentCMS.Plugins.Mcp.Admin.wasm" />
</ItemGroup>
```

It adds four pages in a site where the MCP plugin is enabled: `/mcp/clients`, for a user who holds `mcp.manage` for the instance, where the MCP clients are approved with a role, rejected, given another role, revoked and registered; `/mcp/instructions` and `/mcp/prompts`, for `mcp.manage` in the site, the guidelines and the instructions per content type that agents are told, and the stored prompts; and `/mcp/activity`, for `activity.read` in the site, what agents wrote, which the other pages link to. The settings of `mcp` are the admin's generated settings page. Section 5.4 of the [admin technical specification](https://github.com/fluentcms/FluentCMS/blob/main/docs/planning/admin-technical-spec.md) has the design.
