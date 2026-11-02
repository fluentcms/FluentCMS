# Scope, tiers & locked decisions

FluentCMS is an open source, human-first headless CMS for .NET: NuGet packages you add to an ASP.NET Core host, a thin reference host for Docker, and a Blazor admin. Developers get code-first schema with generated types and can render with Razor/Blazor in the same app or go fully headless; content teams get workflow, RBAC, SSO and audit without a paid tier; AI agents get a first-class MCP server, with humans as publishers. This specification is written fresh; earlier FluentCMS versions do not constrain it.

## Locked decisions

- Headless-first. The content API is the only read path. An optional first-party rendering plugin (Should) ships Blazor components and Razor tag helpers: rich text to HTML, image URLs and `srcset`, SEO head, draft preview middleware, sitemap and robots. Slug routing and redirects come later (Could). No templates, themes, layouts or page builder chosen in the admin.
- Delivery: NuGet packages (`AddFluentCms()`) plus a reference host image. No hosted SaaS.
- Admin: Blazor WebAssembly, a pure HTTP client of the Management API. Components live in a Razor Class Library and talk to the API only through `HttpClient`, never through injected server services. Three ways to get it: the reference host image (admin and API built together, first-party plugins included); a `dotnet new fluentcms-admin` template for teams hosting the admin separately or adding their own plugins; a prebuilt static-files build for first-party-only setups on any static host or CDN with a CORS allow-list. Live updates (locks, job progress) are polled in the first release; a SignalR channel is Should.
- Two API surfaces: Delivery API (public, cacheable, read-only, browser-safe key) and Management API (authenticated, full CRUD, schema introspection, media, users, roles, settings, jobs, plugins). Admin, CLI and MCP use the Management API only, so the admin is proof the API is complete.
- Auth: the API host is the OAuth 2.1 / OpenID Connect authorization server (OpenIddict), the single token issuer in the first release. Admin uses Authorization Code + PKCE with refresh tokens; MCP clients use the same server; external identity providers (Entra ID, Okta, Keycloak) federate through it as SSO. Accepting tokens from an external issuer directly is Could.
- Plugins: compile-time NuGet, server half plus Blazor admin half; FluentCMS's own features are plugins (see category 8). The admin must be built with the plugins it shows; the reference host builds admin and API together.
- Storage: relational via EF Core; PostgreSQL first, SQLite and SQL Server supported.
- License: MIT, with no paywalled enterprise features.
- In the first release: REST and GraphQL, end-user/member authentication, multi-site, MCP server, opt-in AI assist in the editor.

## Tiers (MoSCoW)

| Tier | Meaning |
| --- | --- |
| Must | In the first public release. The product is not usable for its target users without it. |
| Should | Planned for the following minor releases. The first-release architecture must not block it. |
| Could | Desirable; built when time or community contribution allows. |
| Won't | Deliberately out of scope for now, stated so contributors do not propose it. |

Each category below lists its features with a Tier dropdown you can change inline, followed by the decisions that category forces. Categories are approved one at a time; an approved category is marked in its heading.
