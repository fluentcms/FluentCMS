# Decisions log

All eleven categories are approved. Sixteen decisions are locked; 43 carry a recommendation and are treated as decided unless you change the Status here; the last two were decided on 31 Oct 2026. Summary across categories: 165 Must, 67 Should, 51 Could, 36 Won't.

## Locked

| # | Decision | Status | Resolution |
| --- | --- | --- | --- |
| 1 | Positioning | Decided | Human-first headless CMS for .NET; developers and content teams first, agents as first-class API consumers; humans publish. |
| 2 | Rendering | Decided | Headless-first; optional rendering plugin (Blazor components + Razor tag helpers) as Should, slug routing as Could; no templates, themes or page builder. |
| 3 | Delivery shape | Decided | NuGet packages plus a reference host image; no hosted SaaS. |
| 4 | Admin technology | Decided | Blazor WebAssembly, pure Management API client, RCL components talking only through `HttpClient`. |
| 5 | Admin distributions | Decided | Reference image (admin + API + first-party plugins), `fluentcms-admin` template, prebuilt static build with runtime `config.json`. |
| 6 | API surfaces | Decided | Delivery API (public, cacheable, read) and Management API (authenticated, complete); admin, CLI and MCP use Management only. |
| 7 | Authorization server | Decided | OpenIddict in the API host as the single issuer; Authorization Code + PKCE; external IdPs federate as SSO; direct external issuer is Could. |
| 8 | Storage engine | Decided | Relational via EF Core; PostgreSQL first, SQLite and SQL Server supported; no document store. |
| 9 | License | Decided | MIT, no paywalled features, no phone-home telemetry. |
| 10 | First-release scope additions | Decided | GraphQL with mutations, member authentication, multi-site, MCP server, opt-in AI assist all in the first release. |
| 11 | Plugin loading | Decided | Compile-time NuGet only; `AddPlugin<T>()` or assembly discovery; no runtime DLL loading. |
| 12 | Plugin admin contributions | Decided | Blazor components only, lazily loaded per plugin; no iframe or JS editor contract. |
| 13 | Core split | Decided | First-party features are plugins on the public SDK; core is schema, storage, REST, identity, versioning, admin shell, job runner, event bus. |
| 14 | Plugin isolation and contract | Decided | Trusted code in-process; `FluentCMS.Sdk` abstractions package with semver guarantees. |
| 15 | Plugin discovery | Decided | `fluentcms-plugin` NuGet tag and curated docs list; no marketplace. |
| 16 | Admin live updates | Decided | Polling in the first release; SignalR channel as Should. |

## Recommended (adopted unless changed)

| # | Category | Decision | Status | Recommendation |
| --- | --- | --- | --- | --- |
| 17 | Content modeling | Entry ID type | Recommended | GUID v7. |
| 18 | Content modeling | Rich-text JSON format | Recommended | ProseMirror/Tiptap JSON; Portable Text exporter if demanded. |
| 19 | Content modeling | Storage mapping | Recommended | Hybrid: relational columns for scalars and relations, JSON column for blocks and rich text; verify SQL Server JSON. |
| 20 | Content modeling | Nesting depth for blocks and components | Recommended | 3. |
| 21 | APIs | Filter grammar | Recommended | Bracket syntax (`filter[price][gt]=10`); compact grammar later. |
| 22 | APIs | Route shape | Recommended | Separate `/api/delivery` and `/api/management` prefixes. |
| 23 | APIs | Draft reads | Recommended | Preview key class for external frontends plus `draft` scope on management tokens. |
| 24 | APIs | GraphQL library | Recommended | HotChocolate. |
| 25 | APIs | Default population depth | Recommended | 1, explicit `populate` beyond. |
| 26 | Admin UI | Component library | Recommended | Fluent UI Blazor wrapped behind FluentCMS components. |
| 27 | Admin UI | Rich text editor engine | Recommended | Tiptap via JS interop (internal implementation, not an extension contract). |
| 28 | Admin UI | Preview protocol | Recommended | Per-type preview URL template with signed token; define the `postMessage` contract now. |
| 29 | Admin UI | Lazy loading granularity | Recommended | Per plugin assembly via `LazyAssemblyLoader`. |
| 30 | Identity | Identity foundation | Recommended | ASP.NET Core Identity with OpenIddict. |
| 31 | Identity | Permission model | Recommended | Role-based, additive policies, no deny rules. |
| 32 | Identity | Row-level rule authoring | Recommended | C# predicates in the first release; JSON rules as Should. |
| 33 | Identity | Member identity pool | Recommended | Separate tables and token audience from admins. |
| 34 | Media | Transform URL grammar | Recommended | Query string with signature parameter. |
| 35 | Media | Variant cache location | Recommended | Same storage provider as the original. |
| 36 | Media | Asset identity in URLs | Recommended | GUID plus filename, filename ignored on lookup. |
| 37 | Media | Local provider in production | Recommended | Allowed with a documented warning. |
| 38 | Workflow | Version storage | Recommended | Full snapshot per version; diff on demand. |
| 39 | Workflow | Draft storage | Recommended | Current-draft and current-published rows plus versions table. |
| 40 | Workflow | Job runner | Recommended | Own database-backed runner behind an interface; Hangfire swappable. |
| 41 | Workflow | Reference integrity at publish | Recommended | Warn by default, block per type when configured. |
| 42 | Localization | Site model placement | Recommended | Site ID in core with a default site; plugin adds UI and hostname routing. |
| 43 | Localization | Non-localized field storage | Recommended | Shared document row plus locale rows. |
| 44 | Localization | Fallback in list filters | Recommended | Requested locale only. |
| 45 | Localization | Locale code validation | Recommended | BCP 47 with per-site allow-list. |
| 46 | Plugins | Registration style | Recommended | Both explicit `AddPlugin<T>()` and discovery; explicit wins. |
| 47 | Plugins | Core/plugin boundary | Recommended | Core: schema, storage, REST, identity, versioning, admin shell, job runner, event bus. Plugins: media, webhooks, GraphQL, MCP, AI assist, multi-site, search, SSO providers, activity log UI, rendering. |
| 48 | Plugins | Plugin data isolation | Recommended | Table prefix on every provider. |
| 49 | Plugins | Hook ordering | Recommended | Numeric order with manifest dependencies as tiebreak. |
| 50 | AI & agents | Tool granularity | Recommended | Per-type tools for exposed types plus a `query` meta-tool; admins choose exposed types. |
| 51 | AI & agents | Transport | Recommended | Streamable HTTP plus `fluentcms mcp` stdio proxy for IDEs. |
| 52 | AI & agents | Vector storage | Recommended | pgvector plus in-process fallback; external store as provider slot. |
| 53 | AI & agents | AI usage accounting | Recommended | Log per action with token counts; no billing. |
| 54 | Infrastructure | Migration strategy | Recommended | One migration set per provider, generated by CI. |
| 55 | Infrastructure | Automatic migrations at startup | Recommended | On for SQLite, off otherwise, overridable. |
| 56 | Infrastructure | Output cache store | Recommended | ASP.NET Core output caching with tag eviction. |
| 57 | DX | Documentation stack | Recommended | Starlight (Astro), Markdown-first with `llms.txt`. |
| 58 | DX | Type generation source | Recommended | Source generator for C# in the host; OpenAPI for everything else. |
| 59 | DX | CLI distribution and repo layout | Recommended | `dotnet tool` plus standalone binaries; monorepo. |

## Formerly open (decided 31 Oct 2026)

| # | Decision | Status | Resolution |
| --- | --- | --- | --- |
| 60 | Minimum database versions | Decided | Decided 31 Oct 2026: PostgreSQL 15+, SQL Server 2019+ (2025 for the native JSON type), SQLite 3.45+. Re-check once the hybrid storage mapping is prototyped on SQL Server. |
| 61 | Project name and NuGet IDs | Decided | Decided 31 Oct 2026: the name stays FluentCMS. The `FluentCMS.*` package family on NuGet (38 packages, about 31k downloads) belongs to the [FluentCMS organization](https://www.nuget.org/profiles/FluentCMS), administered by Amir's `tajan` account. Only the bare `FluentCMS` ID belongs to FormCMS, and the product does not need it: it ships as a family (`FluentCMS.Sdk`, `FluentCMS.Core`, `FluentCMS.Admin`, `FluentCMS.Plugins.*`, `FluentCMS.Cli`). Actions: reserve the `FluentCMS.` ID prefix on NuGet; new packages that reuse an existing ID (for example `FluentCMS.Identity`, now at 0.0.5) start at 1.0.0; IDs that have no successor are unlisted and deprecated with a pointer to the replacement. |

**Sequencing note.** With GraphQL, member auth, multi-site, MCP and AI assist all in the first release plus 17 Must extension points, the first engineering milestone is the `FluentCMS.Sdk` contract and the core (schema, storage, REST, identity, versioning, job runner, event bus), because every first-party plugin depends on it. A realistic order: SDK + core, then media and admin shell, then MCP and GraphQL plugins, then multi-site, member auth and AI assist, then the rendering plugin and approval workflows in the first minors.
