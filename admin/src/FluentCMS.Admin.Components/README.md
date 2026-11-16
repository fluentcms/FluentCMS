# FluentCMS.Admin.Components

The wrapped component set of the [FluentCMS](https://github.com/fluentcms/FluentCMS) admin: buttons, inputs, dialogs, drawers, menus, a data grid, a tree, layout primitives, icons and the theme tokens, named for FluentCMS (`CmsButton`, `CmsDataGrid`). The admin and every plugin admin half use only this set, so the component library under it can be swapped.

Its component parameters follow semantic versioning from 1.0.0 on. Section 2.3 of the [admin technical specification](https://github.com/fluentcms/FluentCMS/blob/main/docs/planning/admin-technical-spec.md) has the design.

An app registers the services with `builder.Services.AddCmsComponents()`, wraps its markup in `CmsThemeRoot`, and links the set's stylesheet and the app's own bundle of scoped styles in `index.html`. The bundle, which the framework writes for every Blazor app, brings in the styles of the library under the set, so no host page names the library:

```html
<link rel="stylesheet" href="_content/FluentCMS.Admin.Components/cms.css" />
<link rel="stylesheet" href="MyApp.styles.css" />
```

The texts the set shows itself, such as the announcement of a grid's sort, come from its resources in the current UI culture. A host that localizes through its own `IStringLocalizer` registers an `ICmsTextSource` in place of the default one.

The theme tokens are CSS custom properties prefixed `--cms-` on `.cms-root`, with a light and a dark theme that follow `prefers-color-scheme` until `CmsThemeRoot.Mode` says otherwise. The accent is `--fluentcms-brand-accent` when the document sets it. The stylesheet uses logical properties only, so `dir="rtl"` on the document or a container mirrors the layout.
