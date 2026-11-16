# FluentCMS.Admin.Components

The wrapped component set of the [FluentCMS](https://github.com/fluentcms/FluentCMS) admin: buttons, inputs, dialogs, drawers, menus, a data grid, a tree, layout primitives, icons and the theme tokens, named for FluentCMS (`CmsButton`, `CmsDataGrid`). The admin and every plugin admin half use only this set, so the component library under it can be swapped.

Its component parameters follow semantic versioning from 1.0.0 on. Section 2.3 of the [admin technical specification](https://github.com/fluentcms/FluentCMS/blob/main/docs/planning/admin-technical-spec.md) has the design.

An app registers the services with `builder.Services.AddCmsComponents()`, wraps its markup in `CmsThemeRoot`, and links two stylesheets in `index.html`:

```html
<link rel="stylesheet" href="_content/Microsoft.FluentUI.AspNetCore.Components/Microsoft.FluentUI.AspNetCore.Components.bundle.scp.css" />
<link rel="stylesheet" href="_content/FluentCMS.Admin.Components/cms.css" />
```

The theme tokens are CSS custom properties prefixed `--cms-` on `.cms-root`, with a light and a dark theme that follow `prefers-color-scheme` until `CmsThemeRoot.Mode` says otherwise. The stylesheet uses logical properties only, so `dir="rtl"` on the document or a container mirrors the layout.
