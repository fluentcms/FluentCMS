# FluentCMS.Plugins.Search.Admin

The admin half of the [FluentCMS](https://github.com/fluentcms/FluentCMS) search plugin, for an admin app built on `FluentCMS.Admin`. The app references it and loads it lazily, the first time one of its contributions is used:

```xml
<ItemGroup>
  <PackageReference Include="FluentCMS.Plugins.Search.Admin" />
  <BlazorWebAssemblyLazyLoad Include="FluentCMS.Plugins.Search.Admin.wasm" />
</ItemGroup>
```

It adds one page, `/search`, for a user who holds `search.manage` in a site where the search plugin is enabled: a rebuild of the site's search index, of every type with a searchable field or of one. The rebuild runs as a job; a user who holds `jobs.manage` for the instance is linked to it on the jobs page, which follows it. The plugins page links to it from the plugin's entry. Section 5.7 of the [admin technical specification](https://github.com/fluentcms/FluentCMS/blob/main/docs/planning/admin-technical-spec.md) has the design.
