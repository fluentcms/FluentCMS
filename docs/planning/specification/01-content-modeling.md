# 1. Content modeling — approved

The schema is C# code and the single source of truth; everything else (storage, admin, APIs, types) is generated from it. Every leading CMS converged on typed types, a block field, relations, validation and conditional fields; the Must list is that baseline plus the generation story no .NET CMS has.

| # | Feature | Tier | What it means |
| --- | --- | --- | --- |
| 1 | Code-first C# schema | Must | Attributes on classes or a fluent builder. Generates EF storage, admin forms, REST/GraphQL, OpenAPI and C#/TS types. No separate schema file to keep in sync. |
| 2 | Collection types and singletons | Must | Collections hold many entries; singletons hold one (site settings, homepage). Singletons get versions, access rules and hooks like collections. |
| 3 | Core scalar fields | Must | Text, long text, rich text, integer, decimal, boolean, date, date-time, email, URL, slug/UID (auto-generated from a source field, unique), select (single/multi), JSON. |
| 4 | Media and relation fields | Must | Single/multi media; relations 1:1, 1:N, M:N with ordering and an optional reverse side. Relation paths are filterable in the API. |
| 5 | Components | Must | Reusable, nestable field groups (SEO, Address, CTA), single or repeatable, shared across types. |
| 6 | Typed block field | Must | Ordered list of typed blocks for page-builder style content, with bounded nesting. Each block's schema is published in OpenAPI so clients get typed unions. |
| 7 | Validation | Must | Required, min/max, length, regex, unique, enum, plus custom C# validators with context (entry, siblings, user, operation). Drafts skip required; full validation runs at publish. |
| 8 | Conditional fields | Must | Show/hide and require-when as C# predicates, enforced in the admin and on the API. |
| 9 | Structured rich text | Must | Stored as editor-agnostic JSON with typed inline blocks and references to entries and media. The API returns JSON, HTML or Markdown on request. |
| 10 | Field metadata and admin hints | Must | Label, help text, placeholder, default value, read-only, hidden, width/group/tab, localizable, indexed/searchable. |
| 11 | System fields | Must | Id, createdAt/By, updatedAt/By, status, locale, version on every entry; all queryable. |
| 12 | Schema evolution | Must | EF Core migrations for structural changes; add, remove and rename fields without data loss; startup schema validation with actionable errors. |
| 13 | Schema introspection API | Must | Machine-readable schema endpoint consumed by the admin, the type generator, SDKs and the MCP server. |
| 14 | Polymorphic relations | Should | One field relating to several types (featured item → Article or Product). Adds query complexity; Payload and Directus ship it. |
| 15 | Computed and reverse-relation fields | Should | Read-only C# delegates (fullName) and reverse lookups (author.articles), optionally indexed. |
| 16 | Hierarchy and taxonomy | Should | Parent/child trees with ordering and path for any collection; tags/categories as a primitive shared by content and media. |
| 17 | Extra field types | Should | Geo point, color, time, duration, markdown. |
| 18 | Declarative indexes and constraints | Should | Per-field database indexes and composite unique constraints beyond the defaults. |
| 19 | Schema export | Should | Deterministic JSON snapshot of the schema for diffing between environments and for tooling. |
| 20 | Round-trip schema designer | Could | Dev-mode admin UI that writes the C# files, so code stays the source of truth (Statamic/Craft pattern). |
| 21 | Blocks as first-class entries | Could | Craft-style blocks with their own IDs, revisions and reuse across entries. |
| 22 | Database-first mapping | Could | Expose existing tables as content types (Directus pattern). Limited by composite keys and views. |
| 23 | Field-level encryption | Could | At-rest encryption for marked fields. |
| 24 | UI-only runtime schema builder | Won't | Schema lives in code; any designer must round-trip to C# (see 20). |
| 25 | Admin-chosen templates, themes, layouts, page builder | Won't | Headless-first. Rendering helpers and slug routing come as an optional plugin (see category 3); templates, themes and page builder stay out. |
| 26 | Document/NoSQL storage | Won't | Relational via EF Core only. |

## Decisions this category forces

- [ ] Entry ID type: GUID v7 (sortable, native in .NET 9+), ULID, or integer. Recommendation: GUID v7.
- [ ] Rich-text JSON format: ProseMirror/Tiptap JSON (mature editor, wrapped via JS interop in Blazor; Umbraco does the same) or Portable Text (MIT spec, .NET serializer, no mature editor). Recommendation: ProseMirror JSON, with a Portable Text exporter if demand appears.
- [ ] Storage mapping: fully relational (table per type, per array and per block, as Payload) or hybrid (relational columns for scalars and relations, JSON column for blocks and rich text). Recommendation: hybrid; it keeps migrations small and filters on scalars fast. SQL Server JSON support must be verified.
- [ ] Nesting depth limit for blocks and components. Recommendation: 3.
