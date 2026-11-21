# FluentCMS.Admin.Components

The wrapped component set of the [FluentCMS](https://github.com/fluentcms/FluentCMS) admin: buttons, inputs, dialogs, drawers, menus, a data grid, a tree, layout primitives, icons and the theme tokens, named for FluentCMS (`CmsButton`, `CmsDataGrid`). The admin and every plugin admin half use only this set, so the component library under it can be swapped.

Its component parameters follow semantic versioning from 1.0.0 on. Section 2.3 of the [admin technical specification](https://github.com/fluentcms/FluentCMS/blob/main/docs/planning/admin-technical-spec.md) has the design.

An app registers the services with `builder.Services.AddCmsComponents()`, wraps its markup in `CmsThemeRoot`, and links the set's stylesheet and the app's own bundle of scoped styles in `index.html`. The bundle, which the framework writes for every Blazor app, brings in the styles of the library under the set, so no host page names the library:

```html
<link rel="stylesheet" href="_content/FluentCMS.Admin.Components/cms.css" />
<link rel="stylesheet" href="MyApp.styles.css" />
```

The texts the set shows itself, such as the announcement of a grid's sort, come from its resources in the current UI culture. A host that localizes through its own `IStringLocalizer` registers an `ICmsTextSource` in place of the default one. It is asked for the names of the set's resources and for every key of the library under the set; for a name the resources do not hold it must return `null`, not the name as `IStringLocalizer` does, so that the library's own text is shown.

A secret that a page shows once, such as a new API key, a webhook's signing secret or a registered client's, goes in `CmsSecretDialog`. It is a modal dialog, open while `Secret` is not null. It shows the set's text that the secret is shown only now, the page's `ChildContent`, and the secret under `Label`, hidden until the user presses Show. Copy writes it to the clipboard and says whether the browser allowed it. Done is the only way to close it: Escape and a click outside do not, since a secret closed by mistake cannot be read again. Done raises `SecretChanged` with null, so bind it and keep the secret in that one field only. `SecretChanged` is required: a secret passed one way would give a dialog that Done never closes, so the dialog throws `InvalidOperationException` when it renders without it, with a secret or without. Each new value of `Secret`, the next secret included, starts hidden. While the dialog is open, leaving the page asks first. The browser asks before a reload, a close or another site. The dialog asks before a route change, Back included, with Stay and Leave; Escape or a click outside answers Stay. Leave raises `SecretChanged` with null before the route changes, so a page that outlives the change does not show the secret on the next route. `@bind-Secret:after` therefore runs after Leave as well as after Done, and a step that follows Done alone, such as opening the new key's page, goes in `OnClosed`. `OnClosed` is raised once the dialog has closed after Done and given focus back, and not after Leave or when the page drops the secret itself:

```razor
<CmsSecretDialog Title="Copy the new key" Label="Secret" @bind-Secret="secret" OnClosed="OpenKey">
    <p>A client sends it in the X-Api-Key header.</p>
</CmsSecretDialog>
```

The dialog keeps no copy of its own and writes the secret to no storage, log or address. It gives the secret to the clipboard only when the user asks, through the browser's `navigator.clipboard.writeText`.

The theme tokens are CSS custom properties prefixed `--cms-` on `.cms-root`, with a light and a dark theme that follow `prefers-color-scheme` until `CmsThemeRoot.Mode` says otherwise. The accent is `--fluentcms-brand-accent` when the document sets it. The stylesheet uses logical properties only, so `dir="rtl"` on the document or a container mirrors the layout.
