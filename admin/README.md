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
