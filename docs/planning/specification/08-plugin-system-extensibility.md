# 8. Plugin system & extensibility — approved

A plugin is a NuGet package: a Razor Class Library plus a registration against a public SDK. FluentCMS's own features are built as plugins on the same extension points, so the extension surface is proven before third parties use it. There is no runtime DLL loading, sandbox or marketplace in the first release; plugins are trusted code, like any dependency.

## Locked decisions (31 Oct 2026)

- Loading: compile-time NuGet only. Registered via `AddFluentCms().AddPlugin<T>()` or assembly-attribute discovery. No folder scan, no admin upload, no hot-load.
- Admin contributions: Blazor components only, compiled into the Blazor WebAssembly admin and loaded lazily per plugin. No iframe or JS-slot editor contract.
- Core split: dogfooded. Media, webhooks, GraphQL, MCP, AI assist, multi-site and similar ship as first-party plugins on the public extension points; the core is schema, storage, REST, identity and the admin shell.
- Isolation: trusted code in a shared process. Safety comes from the SDK contract, reviewed packages and semver, not sandboxing.
- Contract: a separate `FluentCMS.Sdk` abstractions package with semver guarantees. Breaking changes only in majors; `[Obsolete]` warnings and analyzers one release ahead.
- Discovery: a `fluentcms-plugin` NuGet tag and a curated list in the docs. No marketplace.

| # | Feature | Tier | What it means |
| --- | --- | --- | --- |
| 1 | Plugin package model | Must | A plugin has a server half (services, endpoints, hooks, migrations; referenced by the API host) and an admin half (Blazor WebAssembly RCL; referenced by the admin app). Shipped as one package with both, or two packages with a shared ID prefix. |
| 2 | Plugin manifest and compatibility range | Must | Name, version, author, supported FluentCMS SDK range, dependencies on other plugins. Startup fails fast with a clear error on mismatch or missing dependency. |
| 3 | Public SDK package | Must | `FluentCMS.Sdk`: interfaces, attributes, base classes and DTOs only. Core and plugins both depend on it; plugins never reference core internals. |
| 4 | Lifecycle hooks | Must | Before/after validate, create, update, delete, publish, unpublish, restore on any type or all types; async, ordered, cancellable, with shared context and origin (`Admin`, `Api`, `Mcp`, `Job`). |
| 5 | Custom field types | Must | A field type = storage mapping + validation + OpenAPI schema + Blazor editor + Blazor list cell. Registered once, available to all content types. |
| 6 | Custom API endpoints | Must | Minimal-API style endpoints mounted under the CMS route group, protected by CMS auth and RBAC by default. GraphQL schema contributions for the GraphQL plugin. |
| 7 | Admin pages, menu items and settings pages | Must | Full admin pages, sidebar sections, and a typed settings model that renders a settings page and persists per site. |
| 8 | Content list and entry actions | Must | Buttons and bulk actions in list and edit views, targeted by content type, with confirmation and progress. |
| 9 | Dashboard widgets | Must | Admin home widgets with size hints and per-role visibility. |
| 10 | Background jobs | Must | Plugin-owned queued and scheduled jobs on the core job runner, with retries and a visible job log. |
| 11 | Provider slots | Must | Replaceable providers behind core abstractions: file storage, email, search index, distributed cache, AI model (via Microsoft.Extensions.AI), image processing. |
| 12 | MCP tools, resources and prompts | Must | Plugins register agent tools, resources and prompts; the MCP plugin exposes them under the same RBAC and origin tagging as its built-in tools. |
| 13 | Plugin-owned EF migrations | Must | Each plugin ships its own tables and migrations in a prefixed schema or table prefix; run in dependency order at startup or via CLI. |
| 14 | Plugin permissions in RBAC | Must | Plugins declare permissions; they appear in role editing and are checked by the plugin's endpoints, pages and actions. |
| 15 | Enable/disable per site | Must | A referenced plugin can be switched off per site at runtime; disabled plugins contribute nothing to that site's admin, API or MCP surface. |
| 16 | Event bus | Must | In-process typed events (content, media, user, settings, publish) that plugins subscribe to; the same events feed webhooks and the activity log. |
| 17 | Plugin template and test harness | Must | `dotnet new fluentcms-plugin` and a test host that boots the CMS with SQLite for integration tests. |
| 18 | Plugin diagnostics | Should | Admin page listing loaded plugins, versions, health, registered contributions and startup warnings. |
| 19 | Plugin-contributed CLI commands | Should | Plugins add verbs to the FluentCMS CLI. |
| 20 | Content type packs | Should | Plugins ship content types, components and seed content (blog, docs site, FAQ) that a host can adopt. |
| 21 | Admin-side plugin catalog (read-only) | Could | Admin lists available plugins from a JSON feed with install instructions. Nothing installs from the UI. |
| 22 | Declared capabilities enforced at registration | Could | Manifest lists what the plugin touches; core refuses undeclared registrations. A guardrail, not a sandbox. |
| 23 | Plugin analyzers | Could | Roslyn analyzers that flag use of obsolete SDK members and common misuse at build time. |
| 24 | Runtime DLL loading (folder scan, admin upload, hot-load) | Won't | Compile-time only. |
| 25 | Iframe or JavaScript editor contract | Won't | Blazor components only. |
| 26 | Sandboxing or out-of-process plugins | Won't | Trusted code model. |
| 27 | Marketplace with payments or trust levels | Won't | Tag and curated list only. |

## Decisions this category forces

- [ ] Registration style: explicit `AddPlugin<T>()` in `Program.cs`, assembly-attribute discovery across referenced assemblies, or both with explicit winning. Recommendation: both; explicit for ordering and clarity, discovery for convenience.
- [ ] Core/plugin boundary: which first-party features are core and which are plugins. Proposed core: schema and storage, REST, identity and RBAC, versioning, admin shell, job runner, event bus. Proposed plugins: media, webhooks, GraphQL, MCP, AI assist, multi-site, search, SSO providers, activity log UI.
- [ ] Plugin data isolation: separate EF schema per plugin (Postgres and SQL Server) with table prefix fallback (SQLite), or table prefix everywhere. Recommendation: prefix everywhere for one code path.
- [ ] Hook ordering: numeric order per hook, or dependency-declared ordering in the manifest. Recommendation: numeric order with manifest dependencies as a tiebreak.
