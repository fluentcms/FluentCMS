# FluentCMS.Plugins.Webhooks.Admin

The admin half of the [FluentCMS](https://github.com/fluentcms/FluentCMS) webhooks plugin, for an admin app built on `FluentCMS.Admin`. The app references it and loads it lazily, the first time one of its contributions is used:

```xml
<ItemGroup>
  <PackageReference Include="FluentCMS.Plugins.Webhooks.Admin" />
  <BlazorWebAssemblyLazyLoad Include="FluentCMS.Plugins.Webhooks.Admin.wasm" />
</ItemGroup>
```

It adds two pages, each for a user who holds `webhooks.manage` in a site where the webhooks plugin is enabled: `/webhooks`, the webhooks of the site, where one is created, changed, enabled or disabled and deleted, and its signing secret shown once, and `/webhooks/{id}`, the delivery log of a webhook, where a delivery is sent again and a ping is sent. Section 5.3 of the [admin technical specification](https://github.com/fluentcms/FluentCMS/blob/main/docs/planning/admin-technical-spec.md) has the design.
