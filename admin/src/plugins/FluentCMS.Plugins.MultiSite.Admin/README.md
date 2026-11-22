# FluentCMS.Plugins.MultiSite.Admin

The admin half of the [FluentCMS](https://github.com/fluentcms/FluentCMS) multi-site plugin, for an admin app built on `FluentCMS.Admin`. The app references it and loads it lazily, the first time one of its contributions is used:

```xml
<ItemGroup>
  <PackageReference Include="FluentCMS.Plugins.MultiSite.Admin" />
  <BlazorWebAssemblyLazyLoad Include="FluentCMS.Plugins.MultiSite.Admin.wasm" />
</ItemGroup>
```

It adds two pages, each for a user who holds `sites.manage` for the instance, in a site where the multi-site plugin is enabled: `/multi-site/sites`, the sites of the instance, where a site is created and deleted, and `/multi-site/sites/{id}/hosts`, the host names of a site, where one is added, made primary and removed. A site's name and locales are changed on the admin's own sites page. Section 5.6 of the [admin technical specification](https://github.com/fluentcms/FluentCMS/blob/main/docs/planning/admin-technical-spec.md) has the design.
