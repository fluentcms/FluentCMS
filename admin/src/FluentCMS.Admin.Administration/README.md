# FluentCMS.Admin.Administration

The administration pages of the [FluentCMS](https://github.com/fluentcms/FluentCMS) admin: users, roles, API keys, members, sites and locales, settings, plugins, jobs and diagnostics. An app built on `FluentCMS.Admin` references it and lists it as lazy, so that it is fetched on the first visit of one of these pages and not with the first download; the build stops when it is referenced and not lazy:

```xml
<ItemGroup>
  <PackageReference Include="FluentCMS.Admin" />
  <PackageReference Include="FluentCMS.Admin.Administration" />
  <BlazorWebAssemblyLazyLoad Include="FluentCMS.Admin.Administration.wasm" />
</ItemGroup>
```

The shell of `FluentCMS.Admin` knows the areas before this assembly is loaded: their sidebar entries, and the permission each needs, for the instance where every call of the area is instance-only. A route the user may not open answers the no-access state, and the assembly is not fetched for it.

This preview holds each page with its heading only, the read of the roles with a role select, and the frame of a list of records. Sections 2.6, 4.15 and 4.16 of the [admin technical specification](https://github.com/fluentcms/FluentCMS/blob/main/docs/planning/admin-technical-spec.md) have the design.

## Where an area's files go

Each area is a folder of its own: `Users/`, `Roles/`, `ApiKeys/`, `Members/`, `Sites/`, `Settings/`, `Plugins/`, `Jobs/` and `Diagnostics/`. Everything of an area stays in its folder, so that two areas share no file:

- its pages, each at the area's route or below it, such as `/users/{Id:guid}`: the shell gates a route by its area, and a page outside every area is not shown. The `h1` of the area's first page is `{Area}.Page.Heading` of its resources, which the end-to-end tests know the page by; a page below it names the last item of its breadcrumbs by placing `<PageCrumb Text="..." />` with what it read;
- its typed calls over `IManagementApi`, its records and their source-generated `JsonSerializerContext`;
- its resource file, `{Area}Resources.resx`, read through `IStringLocalizer<{Area}Resources>`, with keys `{Area}.{View}.{Text}`;
- a `_Imports.razor` of its own for the namespaces only it needs.

`Common/` holds what several areas use: `RoleReads` and `RoleSelect`, and `RecordList`, the frame of a list of records. A list with filters of its own names their query parameters in `Filters` (any but `q` and `page`), draws their controls itself, sets one with `SetFilter` and reads them from the `RecordQuery` its `Read` is given.

The area's sidebar entry, its permission and its breadcrumb are declared once, in `AdministrationAreas` of `FluentCMS.Admin`. Its component tests go in a folder of its own under `admin/tests/FluentCMS.Admin.Tests/Administration/`, on `AdministrationTestContext`; `{Area}LocalizationTests` there is the area's own check that no text of its pages is hard-coded, to which its issue adds each route with the answers the page reads (`LoadLocalized`).
