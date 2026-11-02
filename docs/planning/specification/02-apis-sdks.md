# 2. APIs & SDKs — approved

Two HTTP surfaces over one in-process service: a public, cacheable Delivery API and an authenticated Management API that the admin, CLI and MCP server use exclusively. Everything is described by one generated OpenAPI document, and the C# and TypeScript SDKs are generated from it, so the API is never behind the admin.

| # | Feature | Tier | What it means |
| --- | --- | --- | --- |
| 1 | Delivery API (REST) | Must | Read-only, published content by default, browser-safe read key, designed for CDN caching. One endpoint family per content type plus media. |
| 2 | Management API (REST) | Must | Authenticated CRUD for content (drafts and published), media, schema introspection, users, roles, API keys, settings, jobs, webhooks, plugins. Complete enough to drive the admin. |
| 3 | In-process typed content service | Must | `IContentService<TArticle>` with the same query model, access rules and hooks as HTTP. Used by the host's own code and the rendering plugin; the HTTP layer is thin over it. |
| 4 | Query model: filtering | Must | Operators eq, ne, in, nin, lt/lte/gt/gte, between, contains, startsWith, null, and/or/not; filters on relation paths and block fields; same grammar in REST, GraphQL and the in-process API. |
| 5 | Query model: sort, pagination, selection, population | Must | Multi-field sort; offset and page pagination with totals; field selection; relation population by depth with a server-side cap. |
| 6 | Status and locale parameters | Must | \`status=published |
| 7 | Compact response shape | Must | Frontend-shaped JSON: no envelope bloat, relations inlined by depth, system fields in a predictable place, consistent casing. |
| 8 | Error model | Must | RFC 9457 problem details on every error; field-level validation errors with paths; stable error codes. |
| 9 | OpenAPI 3.1 document | Must | Generated per deployment from the live schema: one typed schema per content type, block unions as discriminated `oneOf`, security schemes, examples. Served with Scalar or Swagger UI. |
| 10 | Generated C# SDK | Must | Typed client and models from the OpenAPI document via CLI; strongly typed filters and population; works for Blazor, MAUI, services. |
| 11 | Generated TypeScript types and client | Must | `d.ts` for all types plus a small fetch-based client with typed query builder; framework-agnostic, works in Next.js, Astro, SvelteKit. |
| 12 | API keys and scopes | Must | Keys for delivery (read published), preview (read drafts), management (scoped by role). Expiry, rotation, last-used, per-key rate limit. |
| 13 | Bearer tokens from the authorization server | Must | OAuth 2.1 access tokens for the admin, CLI, MCP and user-facing apps; scopes map to RBAC. |
| 14 | Bulk operations | Must | Create many; update, publish, unpublish, delete by ID list or by filter; partial-failure response. |
| 15 | Optimistic concurrency | Must | ETag / `If-Match` on entry writes; 412 on conflict with the current version. |
| 16 | GraphQL | Must | Queries and mutations generated from the schema (via the GraphQL plugin); filters and pagination mirror REST; complexity and depth limits; introspection off in production; persisted queries optional. |
| 17 | Rate limiting | Must | Per-IP and per-key limits with standard headers; stricter default on auth endpoints. |
| 18 | CORS and security headers | Must | Allow-list configuration, no wildcard by default; security headers on API and admin. |
| 19 | Pagination of large sets | Must | Keyset (cursor) pagination for stable iteration alongside offset pagination. |
| 20 | HTTP caching | Must | `ETag`, `Cache-Control` and `Last-Modified` on Delivery responses; tag-based server cache with invalidation on publish; CDN purge hook. |
| 21 | Delivery API as a separate hostable surface | Should | Run Delivery alone on edge nodes with read replicas while Management stays central. |
| 22 | Aggregations | Should | Count, min, max, sum, avg and group-by on a type with filters, for dashboards. |
| 23 | Full-text search parameter | Should | `q=` across searchable fields using the configured search provider (Postgres FTS, SQLite FTS5, external). |
| 24 | Webhook-style change feed endpoint | Should | Paginated feed of changes since a cursor, for sync and static-site rebuilds. |
| 25 | SDK generation for more languages | Should | Python and Go clients via the same OpenAPI document. |
| 26 | Realtime subscriptions | Could | SSE or GraphQL subscriptions scoped by access rules. |
| 27 | Persisted and named queries | Could | Server-stored queries callable by name, usable as cache keys and MCP tools. |
| 28 | Query language endpoint | Could | GROQ-like projection language for complex read shapes in one request. |
| 29 | gRPC Delivery API | Could | For service-to-service consumers. |
| 30 | OData | Won't | Own query grammar instead. |
| 31 | JSON:API or HAL envelopes | Won't | Compact shape instead. |
| 32 | API versioning via URL path per content type | Won't | Single `/api/v1` version for the surface; schema changes are not API versions. |

## Decisions this category forces

- [ ] Filter grammar in URLs: Strapi-style `filters[price][$gt]=10`, Directus-style `filter[price][_gt]=10`, or a compact single parameter `filter=price>10 and status="x"`. Recommendation: bracket syntax for discoverability, with the compact grammar as an alternative later.
- [ ] Route shape: `/api/delivery/{type}` and `/api/management/{type}`, or one `/api/{type}` with behaviour decided by the key. Recommendation: separate prefixes, so caching, CORS and hosting rules can differ.
- [ ] Draft reads: a separate preview key class (Strapi, Umbraco) or a `draft` scope on management tokens. Recommendation: both; preview keys for external frontends, scope for admin and MCP.
- [ ] GraphQL library: HotChocolate (dominant in .NET, schema stitching, filtering middleware) or GraphQL.NET. Recommendation: HotChocolate.
- [ ] Population default depth: 1 (Payload 4) or 2 (Strapi). Recommendation: 1, with explicit `populate`.
