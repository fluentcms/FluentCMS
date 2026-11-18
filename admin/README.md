# Admin

The Blazor WebAssembly admin of FluentCMS, a pure client of the Management API. The [admin technical specification](../docs/planning/admin-technical-spec.md) has the design.

| Path | Role |
| --- | --- |
| `src/FluentCMS.Admin.Abstractions` | The contract of an admin half, tracked by the public API analyzer |
| `src/FluentCMS.Admin.Components` | The wrapped component set and theme tokens, tracked by the public API analyzer |
| `src/FluentCMS.Admin` | The shell, sign-in, the API client and the core areas |
| `src/FluentCMS.Admin.RichText` | The rich text editor, loaded lazily |
| `src/FluentCMS.Admin.App` | The standalone app, published trimmed; not a package |
| `src/FluentCMS.Admin.Hosting` | Serves the app from an ASP.NET Core host |
| `src/plugins/FluentCMS.Plugins.Media.Admin` | The admin half of the media plugin, loaded lazily |
| `tests/FluentCMS.Admin.Tests` | Component tests on bUnit, in the fast checks |
| `tests/FluentCMS.Admin.ApiTests` | The admin's client against `FluentCmsTestHost`, in full CI |
| `tests/FluentCMS.Admin.EndToEnd` | Browser tests against the reference host, in full CI |
| `tests/FluentCMS.Plugins.Media.Admin.Tests` | The manifest test of the media half |

A lane registers its services in `AddAdminServices` of `AdminHostBuilderExtensions`, which runs only with a valid `config.json` and hands it the `AdminConfiguration`. The wrapped set registers there too, with the admin's localizer as its `ICmsTextSource`. Localization and accessibility register before it, in `AddAdminStartup`, since the configuration error page needs them too; that page uses no wrapped component, only the theme tokens of the `cms-root` class.

`AdminApp` renders one `CmsThemeRoot` around the router and the live regions. The router is inside `SignInGate`, which shows it only to a signed-in tab or on a route under `authentication/`, such as the callback `authentication/login-callback`. The sign-in is the admin's own code flow with PKCE (spec 2.4, decision record [0007](../docs/decisions/0007-admin-sign-in-flow.md)): `AdminSession`, which the shell uses through the internal `IAdminSession` for the signed-in user, sign-in and sign-out, is also the API client's token source, and `TokenRefreshHandler` refreshes it once on a 401. A form keeps its unsaved changes across the end of a session through `IFormDraftStore` of the Abstractions. Every component of the wrapped set is shown at `/system/components`, a page of the shell that ships with the admin and is linked from the start page. The admin halves the app references are loaded by `AdminPlugins` of `Plugins/`, from the manifests the build gathers into `fluentcms-admin.plugins.json` (spec 2.6); those it cannot load are listed at `/system/plugins`.

The shell is `Shell/AdminLayout`: with a signed-in user, `AdminContext` reads `/me`, `/me/sites` and, for the site, `/me/access`, `/schema` and `/plugins`, and the layout shows the page once they are read. `AdminContext` is the `IAdminContext` and `IAdminAccess` of the Abstractions, and `ShellSite` is the site and locale the API client sends. The sidebar lists the types the user may read, then the pages of `AdminMenu` its access and the plugins of the site allow; a page adds its entry there with the issue that writes it. Without a user, as on the callback, the layout shows the header and the page only, and calls no API.

The form of an entry is `Content/ContentForm`, at `content/{type}/new` and `content/{type}/{id}`, and for a singleton at `content/{type}` (spec 4.5). It lays the fields out from their `display`, edits the entry as its JSON document, and renders each field with the `FieldEditorBase` that `FieldEditorRegistry` of the Abstractions holds for its field type; a type without one shows its JSON read-only. `AddFieldEditors` registers the editors of the core field types, and the form keeps its draft through `IFormDraftStore`.

The browser libraries are trimmable and the app publishes trimmed, so a trim warning in a FluentCMS assembly fails `dotnet build -warnaserror` (the trim analyzer) and the publish. In a FluentCMS component, the framework's `Router` (IL2111, and IL2110 with `NotFoundPage`), `LayoutView` (IL2110, IL2111) and `DynamicComponent` (IL2111) raise such warnings; `RouteView`, `CascadingValue`, `ErrorBoundary`, `NavLink`, `PageTitle` and `HeadOutlet` do not. The component that hosts one of the three carries `@attribute [UnconditionalSuppressMessage("Trimming", "IL2110", Justification = ...)]` and the same for `IL2111`, with the reason, and then both pass. The trimmer also drops a page of a trimmable library that nothing references, so `Router` does not find it at run time although the build passes: `AdminApp` keeps each page with a `DynamicDependency` on its constructor.

The admin's rules of spec 3.3 are in `tests/FluentCMS.Architecture.Tests/AdminRulesTests.cs`. A type that uses JavaScript interop must be on its list of spec 3.1; a new admin project under `admin/src/` needs a reference from the architecture tests, which a test there asks for. Only `FluentCMS.Admin.Components` compiles against the component library. The `Pack` job of full CI fails when the compressed first download of the app grows past its budget (spec 7).

In `FluentCMS.Admin.Components`, a `.razor` component's public API is listed like any other type in `PublicAPI.Unshipped.txt`: the class, its constructor, its `~override BuildRenderTree` and each parameter's getter and setter, as RS0016 names them (`_Imports` included). RS0041, which only Razor's generated `BuildRenderTree` raises in this nullable project, is suppressed once for the namespace in `GlobalSuppressions.cs`.

The reference host serves the app at `/admin` through `FluentCMS.Admin.Hosting` once `FluentCms:Admin:Enabled` is true, with `config.json` built from `FluentCms:Admin` ([host configuration](../docs/host-configuration.md#admin)). Two loops run it locally against the reference host in Development, whose `appsettings.Development.json` turns the admin on and registers the client for both: `dotnet watch --project src/FluentCMS.Host` serves the referenced app at `https://localhost:5001/admin/`, and `dotnet watch --project admin/src/FluentCMS.Admin.App` serves the app alone at `https://localhost:5201/`, with the sample `wwwroot/config.json` pointing at the host run with `dotnet run --project src/FluentCMS.Host`.
