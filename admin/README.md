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
| `tests/FluentCMS.Admin.Tests` | Component tests on bUnit, in the fast checks |
| `tests/FluentCMS.Admin.ApiTests` | The admin's client against `FluentCmsTestHost`, in full CI |
| `tests/FluentCMS.Admin.EndToEnd` | Browser tests against the reference host, in full CI |

A lane registers its services in `AddAdminServices` of `AdminHostBuilderExtensions`, which runs only with a valid `config.json` and hands it the `AdminConfiguration`. Localization and accessibility register before it, in `AddAdminStartup`, since the configuration error page needs them too.

The browser libraries are trimmable and the app publishes trimmed, so a trim warning in a FluentCMS assembly fails `dotnet build -warnaserror` (the trim analyzer) and the publish. In a FluentCMS component, the framework's `Router` (IL2111, and IL2110 with `NotFoundPage`), `LayoutView` (IL2110, IL2111) and `DynamicComponent` (IL2111) raise such warnings; `RouteView`, `CascadingValue`, `ErrorBoundary`, `NavLink`, `PageTitle` and `HeadOutlet` do not. The component that hosts one of the three carries `@attribute [UnconditionalSuppressMessage("Trimming", "IL2110", Justification = ...)]` and the same for `IL2111`, with the reason, and then both pass. The trimmer also drops a page of a trimmable library that nothing references, so `Router` does not find it at run time although the build passes: `AdminApp` keeps each page with a `DynamicDependency` on its constructor.

In `FluentCMS.Admin.Components`, a `.razor` component's public API is listed like any other type in `PublicAPI.Unshipped.txt`: the class, its constructor, its `~override BuildRenderTree` and each parameter's getter and setter, as RS0016 names them (`_Imports` included). RS0041, which only Razor's generated `BuildRenderTree` raises in this nullable project, is suppressed once for the namespace in `GlobalSuppressions.cs`.
