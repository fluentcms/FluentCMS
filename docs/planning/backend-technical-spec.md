# FluentCMS backend: technical specification (draft for review)

Version 1, 1 Nov 2026. Status: **approved**. The eighteen questions in section 10.1 were decided on 1 Nov 2026 and the text reflects those decisions. GitHub issues for milestones M0 and M1 were created from section 9 the same day; later milestones get their issues when they are planned.

This document takes the approved [feature specification](specification/README.md) and keeps only what runs on the server: the NuGet packages a host adds, the reference host, the server halves of the first-party plugins, and the developer tooling that talks to them. The Blazor admin, the admin halves of plugins, frontend starter kits and the documentation site are out of scope here and get their own documents later. Once this document is approved, GitHub issues are created from section 9.

How to read it: sections 1 to 3 set scope and architecture; sections 4 to 6 describe each backend area with its design, key types, storage and endpoints; section 7 maps every specification row to a backend area and a milestone; section 8 sequences the work; section 9 is the proposed issue list; section 10 records the decisions taken on the open questions and the suggestions that shaped the design. Everything marked *proposed* is still open to change during review.

## 1. Scope

### 1.1 What counts as backend

| In scope | Out of scope (separate documents) |
| --- | --- |
| `FluentCMS.Sdk` public contract | Blazor WebAssembly admin shell and generated views |
| Core engine: schema, storage, content service, versioning, publishing, localization, sites, identity, authorization, activity log, events, hooks, job runner, settings, plugin host | Admin halves of plugins (Razor Class Libraries, field editors, pages, widgets) |
| Delivery API and Management API (REST), error model, OpenAPI document, caching, rate limiting, CORS | Admin distributions (`fluentcms-admin` template, static build) |
| Authorization server (OpenIddict), including the server-rendered login, consent, two-factor, invitation and reset pages it needs | Preview iframe and `postMessage` protocol (the backend ships only preview keys and draft reads) |
| First-party plugins, server side: media, webhooks, GraphQL, MCP, AI assist, multi-site, search | Frontend starter kits (Next.js, Astro, Blazor) |
| Infrastructure: configuration, health, observability, multi-instance safety, export and import, startup diagnostics, reference host image | Documentation site, `llms.txt`, agent skills and repository instructions |
| Tooling that targets the backend: CLI, `fluentcms` and `fluentcms-plugin` templates (server half), test host, source generator, analyzers | Generated TypeScript client (a consumer of the backend's OpenAPI document; tracked with the frontend work) |

The generated C# and TypeScript clients are produced *from* the backend's OpenAPI document. The CLI command that produces them is in scope; the client packages themselves are tracked with the frontend work.

### 1.2 Decisions this document is bound by

From [scope and locked decisions](specification/00-scope-tiers-locked-decisions.md) and the [decisions log](specification/12-decisions-log.md). Numbers are decision numbers in the log.

| # | Decision | Effect on the backend |
| --- | --- | --- |
| 1, 2 | Human-first, headless-first | The content API is the only read path; no rendering in core. |
| 3 | NuGet packages plus a reference host image | `AddFluentCms()` on any ASP.NET Core host; one Docker image for standalone use. |
| 6, 22 | Two API surfaces with separate prefixes | `/api/delivery/...` public and cacheable; `/api/management/...` authenticated and complete. |
| 7, 30 | OpenIddict in the API host on ASP.NET Core Identity | The host is the single token issuer; external identity providers federate through it. |
| 8, 19, 60 | EF Core, hybrid storage mapping, PostgreSQL 15+ first, SQLite 3.45+, SQL Server 2019+ | Relational columns for scalars and relations, JSON for blocks and rich text. |
| 11, 13, 14, 46, 47, 48 | Compile-time plugins on a public SDK, dogfooded core split, table prefix | Core is schema, storage, REST, identity, versioning, job runner, event bus; the rest are plugins. |
| 17, 18, 20 | GUID v7 ids, ProseMirror JSON, nesting depth 3 | Fixed in the SDK types. |
| 21, 23, 25 | Bracket filter grammar, preview key plus `draft` scope, populate depth 1 | Fixed in the query model. |
| 24, 31, 32, 33 | HotChocolate, additive RBAC, C# row rules, separate member pool | Fixed in the GraphQL plugin and the identity design. |
| 38, 39, 40, 41 | Snapshot versions, draft and published rows, own job runner, warn on unpublished references | Fixed in the publishing design. |
| 42, 43, 44, 45 | Site id in core, shared row plus locale rows, requested-locale filtering, BCP 47 | Fixed in the storage and localization design. |
| 50, 51, 52, 53 | Per-type MCP tools plus `query`, Streamable HTTP, pgvector with fallback, usage logging | Fixed in the MCP and AI assist plugins. |
| 54, 55, 56 | One migration set per provider, auto-migrate only on SQLite by default, ASP.NET Core output caching | Fixed in the data provider packages and caching. |
| 58, 59 | Source generator for C# in the host, monorepo, `dotnet tool` CLI | Fixed in the repository layout and tooling. |
| 61 | `FluentCMS.*` package family, new IDs start at 1.0.0 | Package names in section 2.4. |

### 1.3 Platform targets

| Target | Version | Note |
| --- | --- | --- |
| .NET | 10 LTS (`global.json` pins SDK 10.0.100, roll forward latest feature) | C# 14, file-scoped namespaces and primary constructors per `.editorconfig`. |
| EF Core | 10 | Property-bag entity types, JSON columns, compiled model where useful. |
| PostgreSQL | 15+ | Primary. `jsonb`, `FOR UPDATE SKIP LOCKED`, full-text search, pgvector optional. |
| SQLite | 3.45+ | Development, tests, small sites. JSON functions, FTS5, polling job runner. |
| SQL Server | 2019+ (2025 for the native `json` type) | `nvarchar(max)` JSON with `JSON_VALUE` and `OPENJSON` below 2025; `READPAST` job runner. |
| Containers | Linux and Windows, multi-arch image | Reference host. |

## 2. Architecture

### 2.1 Layering

```text
ASP.NET Core host (your app, or FluentCMS.Host)
|
+-- HTTP surfaces
|   Delivery API, Management API, OAuth/OIDC endpoints and hosted pages,
|   GraphQL (plugin), /mcp (plugin), /media (plugin), /health
|   -> authenticate, resolve site and locale, parse the query, map errors, cache
|
+-- In-process services: the only place rules are enforced
|   IContentService<T>, IPublishingService, IMediaService, IAuthorizationEvaluator,
|   IActivityLog, IEventBus, IJobScheduler
|   -> permissions, row rules, field filtering, validation, hooks, versions
|
+-- Schema and storage
|   SchemaModel (built from C# types) -> EF Core model (property bags) -> migrations
|
+-- EF Core provider: FluentCMS.Data.PostgreSql, .Sqlite or .SqlServer
```

Rules that follow from the layering:

- HTTP, GraphQL and MCP layers carry identity and intent only. They never touch the `DbContext` and never evaluate permissions themselves. An architecture test enforces this (3.4).
- The in-process service is the public API for host code and, later, for the rendering plugin. Whatever REST can do, `IContentService<T>` can do, with the same access checks. There is no "override access" flag; a `System` principal exists for jobs, seeding and the CLI.
- Every operation runs inside an `OperationContext` (2.2). Versions, the activity log, hooks, webhooks and MCP origin tagging all read from it, so it exists from the first commit.

### 2.2 Operation context

One ambient, immutable record flows through every call:

```csharp
public sealed record OperationContext(
    Principal Principal,      // admin user, member, API key, service account, or System
    Origin Origin,            // Admin, Api, Mcp, Job, Cli, Seed
    SiteId Site,
    string Locale,            // resolved request locale, BCP 47
    string? ClientId,         // OAuth client id or API key id
    string CorrelationId,
    DateTimeOffset Timestamp);
```

The entry point sets the origin: Delivery and Management endpoints set `Api`, requests from the admin's OAuth client map to `Admin`, the MCP plugin sets `Mcp`, the job runner sets `Job`. Origin, principal and client id are persisted on every version and activity row.

### 2.3 One enforcement point

`IAuthorizationEvaluator` answers three questions, and nothing else reads permission data:

1. May this principal perform operation *X* on type *T*, optionally on this entry, in this site and locale?
2. Which fields of *T* may it read or write? Hidden fields are omitted from responses, not blanked.
3. Which row-rule predicate must be appended to list queries for *T*?

`IContentService` asks on every operation. The schema introspection endpoint and the MCP tool list ask to filter what they expose. The admin reads the same answers through the Management API.

### 2.4 Package map

Proposed NuGet packages, all `FluentCMS.*`, first release 1.0.0. A host adds two packages (`FluentCMS.Core` and one data provider) for a working API. Heavy optional dependencies stay out of core.

| Package | Contents | Depends on |
| --- | --- | --- |
| `FluentCMS.Sdk` | Interfaces, attributes, base classes, DTOs, events, permission names. No implementation. Semver-guaranteed, public API tracked by analyzers. | `Microsoft.Extensions.*` abstractions only |
| `FluentCMS.Core` | The engine and the HTTP surfaces: schema, storage model, content service, query model, versioning, publishing, localization, sites, identity and authorization server, RBAC, activity log, events, hooks, job runner, settings, plugin host, Delivery and Management APIs, OpenAPI, caching, health, diagnostics, export and import, rich text renderers. | `FluentCMS.Sdk`, EF Core, ASP.NET Core Identity, OpenIddict |
| `FluentCMS.Data.PostgreSql` | Npgsql wiring, provider-specific SQL (JSON paths, `SKIP LOCKED`, full-text search), the PostgreSQL system migration set. | `FluentCMS.Core`, Npgsql.EntityFrameworkCore.PostgreSQL |
| `FluentCMS.Data.Sqlite` | Same for SQLite (polling runner, JSON1, FTS5, auto-migrate on by default). | `FluentCMS.Core`, Microsoft.EntityFrameworkCore.Sqlite |
| `FluentCMS.Data.SqlServer` | Same for SQL Server (`READPAST`, `JSON_VALUE`, native `json` on 2025). | `FluentCMS.Core`, Microsoft.EntityFrameworkCore.SqlServer |
| `FluentCMS.Plugins.Media` | Assets, folders, local storage provider, transforms, signed URLs, media Delivery API, the `media` field type. | `FluentCMS.Sdk`, SixLabors.ImageSharp |
| `FluentCMS.Plugins.Media.S3`, `.AzureBlob` | Storage providers. | `FluentCMS.Plugins.Media`, AWSSDK.S3 or Azure.Storage.Blobs |
| `FluentCMS.Plugins.Webhooks` | Subscriptions, HMAC signing, delivery with retries, delivery log. | `FluentCMS.Sdk` |
| `FluentCMS.Plugins.GraphQL` | Schema generated from the content schema, queries and mutations, limits. | `FluentCMS.Sdk`, HotChocolate |
| `FluentCMS.Plugins.Mcp` | Streamable HTTP MCP server, tools, resources, prompts, client management, agent instructions. | `FluentCMS.Sdk`, ModelContextProtocol |
| `FluentCMS.Plugins.AiAssist` | Provider-agnostic AI actions and usage log. | `FluentCMS.Sdk`, Microsoft.Extensions.AI |
| `FluentCMS.Plugins.MultiSite` | Hostname resolution, per-site hosts, cross-site operations. | `FluentCMS.Sdk` |
| `FluentCMS.Plugins.Search` (Should) | Full-text `q=` parameter on PostgreSQL FTS and SQLite FTS5, external index slot. | `FluentCMS.Sdk` |
| `FluentCMS.Caching.Redis` (Should) | Distributed cache, output cache and distributed lock on one connection string. | `FluentCMS.Sdk` |
| `FluentCMS.Testing` | `FluentCmsTestHost`, fixtures for users, keys and content; SQLite by default. | `FluentCMS.Core`, `FluentCMS.Data.Sqlite` |
| `FluentCMS.Cli` | The `fluentcms` dotnet tool. | `FluentCMS.Core` design-time services |
| `FluentCMS.Generators` | Roslyn source generator: typed filter builders and mappers for the host's content types. | Roslyn |
| `FluentCMS.Analyzers` | Roslyn analyzers for schema attributes and SDK misuse. | Roslyn |
| `FluentCMS.Templates` | `dotnet new` templates (`fluentcms`, `fluentcms-plugin`). | none |

Not a package: `FluentCMS.Host`, the reference host application and Docker image.

A host's `Program.cs`:

```csharp
builder.Services.AddFluentCms(cms =>
{
    cms.UsePostgreSql(builder.Configuration.GetConnectionString("cms"));
    cms.AddContentTypesFromAssembly(typeof(Program).Assembly);
    cms.AddPlugin<MediaPlugin>(media => media.UseS3(builder.Configuration.GetSection("S3")));
    cms.AddPlugin<WebhooksPlugin>();
    cms.AddPlugin<McpPlugin>();
});

var app = builder.Build();
app.MapFluentCms(); // delivery, management, oauth, health, plugin endpoints
app.Run();
```

A split into separate `FluentCMS.Identity` and `FluentCMS.Api` packages was considered and rejected (Q1).

## 3. Repository layout

Monorepo (decision 59). The admin, starters and docs-site folders are reserved and not covered here.

```text
FluentCMS/
+-- .github/                      workflows: build, test matrix, pack, publish; templates
+-- docs/
|   +-- planning/                 this document and specification/
|   +-- decisions/                ADRs for choices made during implementation (Q12)
+-- src/
|   +-- FluentCMS.Sdk/
|   +-- FluentCMS.Core/
|   +-- FluentCMS.Data.PostgreSql/
|   +-- FluentCMS.Data.Sqlite/
|   +-- FluentCMS.Data.SqlServer/
|   +-- FluentCMS.Testing/
|   +-- FluentCMS.Cli/
|   +-- FluentCMS.Generators/
|   +-- FluentCMS.Analyzers/
|   +-- FluentCMS.Host/           reference host: app, Dockerfile, compose
|   +-- plugins/
|       +-- FluentCMS.Plugins.Media/
|       +-- FluentCMS.Plugins.Media.S3/
|       +-- FluentCMS.Plugins.Media.AzureBlob/
|       +-- FluentCMS.Plugins.Webhooks/
|       +-- FluentCMS.Plugins.GraphQL/
|       +-- FluentCMS.Plugins.Mcp/
|       +-- FluentCMS.Plugins.AiAssist/
|       +-- FluentCMS.Plugins.MultiSite/
|       +-- FluentCMS.Plugins.Search/
+-- tests/
|   +-- FluentCMS.Sdk.Tests/
|   +-- FluentCMS.Core.Tests/              unit tests on SQLite
|   +-- FluentCMS.Core.IntegrationTests/   HTTP tests through FluentCMS.Testing
|   +-- FluentCMS.Data.Tests/              provider matrix: PostgreSQL, SQLite, SQL Server
|   +-- FluentCMS.Architecture.Tests/      layering rules
|   +-- FluentCMS.Plugins.*.Tests/
+-- templates/
|   +-- fluentcms/                         API host template
|   +-- fluentcms-plugin/                  plugin template, server half (admin half later)
+-- samples/
|   +-- Blog/                              sample host used by docs and tests
+-- admin/                                 reserved: Blazor admin
+-- starters/                              reserved: frontend starters
+-- Directory.Build.props                  TFM, nullable, warnings as errors, analyzers, package metadata
+-- Directory.Packages.props               central package management
+-- FluentCMS.slnx
+-- global.json
```

### 3.1 Core internal structure

`FluentCMS.Core` is organised by feature area, one folder per area, each with its own registration method called from `AddFluentCms()`:

```text
src/FluentCMS.Core/
+-- DependencyInjection/   AddFluentCms, FluentCmsBuilder, MapFluentCms, options validation
+-- Schema/                attribute reader, fluent builder, SchemaModel, field type registry,
|                          schema validation, introspection DTOs, snapshot and hash
+-- Storage/               CmsDbContext, model builder from SchemaModel, conventions, JSON
|                          mapping, system entities, migration runner, SQL dialect abstraction
+-- Content/               IContentService, ContentDocument, query model, filter compiler,
|                          projection, population, validation pipeline, hook pipeline, bulk
+-- Publishing/            state machine, versions, publish, unpublish, discard, restore,
|                          scheduling, trash and purge, reference integrity checks
+-- Localization/          locale registry per site, fallback resolution, request locale
+-- Sites/                 Site entity, default site, site resolution by header or parameter
+-- Identity/              Identity stores, OpenIddict server, hosted pages (Razor Pages),
|                          users, invitations, 2FA, SSO federation, API keys, members
+-- Authorization/         permission catalogue, role permissions, evaluator, field filter,
|                          row rule registry, public access
+-- ActivityLog/           writer, query, retention job
+-- Api/
|   +-- Delivery/          per-type read endpoints, status and locale handling, cache headers
|   +-- Management/        content, schema, users, roles, keys, sites, locales, settings,
|   |                      jobs, activity, plugins, transfer, system, me
|   +-- Common/            problem details, filter and sort parsing, pagination, ETag,
|   |                      rate limiting policies, CORS, API key authentication handler
|   +-- OpenApi/           document transformers: per-type schemas, block unions, security
+-- Events/                in-process event bus, event types, outbox for reliable fan-out
+-- Jobs/                  job store, runner, scheduler, retries, dead letter, leases
+-- Plugins/               plugin host, manifest validation, registration order, per-site
|                          enablement, provider slots
+-- Settings/              typed settings model, per-site persistence, change events
+-- Transfer/              export package writer, import with upsert and dry run
+-- Caching/               output cache tags and eviction, database-backed IDistributedLock
+-- Diagnostics/           health checks, startup report, OpenTelemetry instrumentation
+-- RichText/              ProseMirror document model, HTML and Markdown renderers,
                           link and media reference extraction
```

### 3.2 Build and quality conventions

- `Directory.Build.props`: `net10.0`, nullable enabled, warnings as errors, .NET analyzers at `latest-recommended`, deterministic builds, SourceLink, package icon and license expression.
- `Directory.Packages.props`: central package versions. A new dependency is named in the issue before it is added, so it is visible in review.
- `FluentCMS.Sdk` uses `Microsoft.CodeAnalysis.PublicApiAnalyzers` (`PublicAPI.Shipped.txt`, `PublicAPI.Unshipped.txt`) and `EnablePackageValidation` against the last released version, so the semver promise in decision 14 is enforced by the build.
- Tests: xUnit v3 with Shouldly (Q13). SQLite is the only database used locally and in CI during implementation; Testcontainers for PostgreSQL and SQL Server join the data matrix with the deferred provider work (Q19).
- Public types in `FluentCMS.Sdk` carry XML docs because they ship in the package. Implementation code does not.

### 3.3 Naming conventions

| Thing | Convention | Example |
| --- | --- | --- |
| Content type name | lower kebab-case, from the attribute or the class name | `article`, `site-settings` |
| Field name in the API | camelCase of the C# property | `publishedOn` |
| Table | `{prefix}{type}`, prefix default `cms_`; system tables `cms_*`; plugin tables `cms_{plugin}_*` | `cms_article`, `cms_media_asset` |
| Permission | dotted, lower case | `content.article.publish` |
| Route | the type name as declared, no pluralisation | `/api/delivery/v1/article` |
| Event | past tense, suffix `Event` | `EntryPublishedEvent` |
| Hook | `IBefore{Operation}Hook<T>` and `IAfter{Operation}Hook<T>` | `IBeforePublishHook<Article>` |
| Error code | `FCMS` plus four digits, grouped by area | `FCMS1001` |

### 3.4 Architecture tests

`FluentCMS.Architecture.Tests` asserts: `Api/*` never references `Storage/*` or `CmsDbContext`; plugins reference only `FluentCMS.Sdk`; `FluentCMS.Sdk` references no implementation package; nothing outside `Authorization/*` reads role permission tables.

## 4. Core areas

Each area lists the specification rows it implements (category.row, for example 1.3 is content modeling row 3), the design, key types, storage, endpoints and open points.

### 4.1 SDK contract

Rows 8.3, 8.11, 8.14, 8.16 and every attribute and interface named below.

`FluentCMS.Sdk` is the only package plugins and host code compile against. Namespaces:

| Namespace | Holds |
| --- | --- |
| `FluentCMS.Schema` | content type and field attributes, `ContentItem` base class, `RichText`, `BlockList`, `Ref<T>`, `RefList<T>`, `IFieldType`, fluent `ISchemaBuilder` |
| `FluentCMS.Content` | `IContentService`, `IContentService<T>`, `ContentDocument`, `ContentQuery`, filter AST, `PagedResult<T>`, `OperationContext`, `Origin`, `Principal` |
| `FluentCMS.Publishing` | `EntryStatus`, `VersionInfo`, `ScheduleRequest`, `IPublishingService` |
| `FluentCMS.Hooks` | hook interfaces and `HookContext` |
| `FluentCMS.Events` | `IEventBus`, `IEventHandler<T>`, event records |
| `FluentCMS.Jobs` | `IJob<TPayload>`, `IJobScheduler`, `JobOptions` |
| `FluentCMS.Authorization` | `Permission`, `IPermissionProvider`, `IRowRule<T>`, `IAuthorizationEvaluator` |
| `FluentCMS.Plugins` | `IPlugin`, `FluentCmsPluginAttribute`, `IPluginServiceBuilder`, `IPluginAppBuilder`, `IPluginModelContributor`, `IPluginState` |
| `FluentCMS.Providers` | `IFileStorage`, `IEmailSender`, `ISearchIndex`, `IDistributedLock`, `IImageProcessor` |
| `FluentCMS.Media` | `MediaRef`, `MediaRefList`, `IMediaService`, `IMediaResolver` (contract only; implemented by the media plugin) |
| `FluentCMS.Sites`, `FluentCMS.Localization` | `SiteId`, `ISiteResolver`, `ILocaleResolver`, `FallbackChain` |
| `FluentCMS.Settings` | `SettingsAttribute`, `ISettings<T>` |
| `FluentCMS.Diagnostics` | `StartupReport`, `FluentCmsHealthCheckTags` |
| `FluentCMS.Agents` | `IMcpToolProvider`, `IMcpResourceProvider`, `IMcpPromptProvider` (contracts; implemented by the MCP plugin) |

The SDK has no dependency on EF Core or ASP.NET Core beyond `Microsoft.Extensions.*` abstractions, so plugin test projects stay light.

### 4.2 Schema

Rows 1.1 to 1.13, 1.19 (snapshot), 1.14 to 1.18 as Should.

**Definition.** Attributes on plain classes are the primary form; the fluent builder is the second form and is what the attribute reader produces internally, so both yield the same `SchemaModel`. The builder also lets a host define types without a class, which the reference host uses to load a schema file (Q2).

```csharp
[Collection("article", Localized = true)]
public sealed class Article : ContentItem
{
    [Text(MaxLength = 200), Required, Localizable, Searchable]
    public string Title { get; set; } = "";

    [Slug(Source = nameof(Title)), Localizable, Unique]
    public string Slug { get; set; } = "";

    [RichText, Localizable]
    public RichText Body { get; set; } = RichText.Empty;

    [Relation(Reverse = nameof(Author.Articles))]
    public Ref<Author> Author { get; set; }

    [Relation(Ordered = true)]
    public RefList<Tag> Tags { get; set; } = [];

    [Media]
    public MediaRef? Hero { get; set; }

    [Blocks(typeof(HeroBlock), typeof(QuoteBlock), typeof(GalleryBlock))]
    public BlockList Sections { get; set; } = [];

    [Component]
    public Seo Seo { get; set; } = new();

    [Select, Indexed]
    public ArticleCategory Category { get; set; }

    [DateTime, Indexed]
    public DateTimeOffset? PublishedOn { get; set; }

    [VisibleWhen(nameof(IsVideo)), RequiredWhen(nameof(IsVideo))]
    [Url]
    public string? VideoUrl { get; set; }

    public static Condition IsVideo => Condition.Field(nameof(Category)).Equals(ArticleCategory.Video);
}

[Singleton("site-settings")]
public sealed class SiteSettings : ContentItem { /* ... */ }

[Component]
public sealed class Seo { [Text(MaxLength = 70)] public string? MetaTitle { get; set; } /* ... */ }

[Block("hero")]
public sealed class HeroBlock { [Text] public string Heading { get; set; } = ""; [Media] public MediaRef? Image { get; set; } }
```

`ContentItem` carries the system fields (`Id`, `Status`, `Locale`, `Version`, `CreatedAt`, `CreatedBy`, `UpdatedAt`, `UpdatedBy`, `PublishedAt`) with setters the engine uses. Their names are reserved; a field with a reserved name fails schema validation.

**Field types.** Each field type is an `IFieldType` descriptor: CLR type(s), storage kind (`Column`, `Json`, `Reference`), default validation, OpenAPI schema fragment, supported filter operators, whether it can be sorted or indexed. Core registers text, longText, richText, integer, decimal, boolean, date, dateTime, email, url, slug, select (single and multi), json, relation, component and blocks. The media plugin registers `media`, which is the proof of the custom field type extension point (8.5): core without the media plugin reports `FCMS1203: field type 'media' used by article.hero is not registered`.

**Components.** `[Component]` classes are reusable field groups. A single component flattens to prefixed columns (`seo_metaTitle`) so its fields are filterable and indexable; a repeatable component (`List<Address>`) is stored as JSON (Q6b).

**Blocks.** `[Block("name")]` classes form the union of a `BlockList` field. Blocks may contain components and nested block lists up to depth 3 (decision 20), validated at startup. Every block schema is emitted in OpenAPI as a `oneOf` with a `type` discriminator.

**Validation.** Declarative: `Required`, `Length`, `Range`, `RegularExpression`, `Unique`, enum membership. Custom: `IFieldValidator<TField>` and `IEntryValidator<T>` receive a `ValidationContext` (entry, sibling values, principal, operation, locale). Draft saves skip `Required` and `RequiredWhen`; publish runs everything.

**Conditions.** `VisibleWhen` and `RequiredWhen` reference a static `Condition` expressed with a small fluent API (`Field(...).Equals/In/IsEmpty/And/Or/Not`). Being data rather than arbitrary C#, a condition is serialised into the introspection document so the admin and agents can evaluate it, and the server enforces `RequiredWhen` at publish. Arbitrary C# predicates stay available through `IEntryValidator<T>` for server-only rules.

**Metadata.** `Display(Label, Help, Placeholder, Group, Tab, Width)`, `DefaultValue`, `ReadOnly`, `Hidden`, `Localizable`, `Indexed`, `Searchable`. All of it appears in introspection; none of it affects storage except `Indexed`, `Unique` and `Localizable`.

**Validation at startup.** Reserved names, nesting depth, relation targets exist, slug sources exist, `Localizable` only on localized types, field types registered, duplicate type names, duplicate reverse relation names. Errors carry `FCMS12xx` codes and stop the host.

**Introspection.** `GET /api/management/v1/schema` and `GET /api/management/v1/schema/types/{type}` return the model filtered by the caller's permissions (4.17 in the identity category): types, fields, field types, validation, conditions, metadata, relations, block unions, localized flags. The same DTOs feed the MCP schema resources and the CLI's schema diff.

**Snapshot and evolution.** `SchemaModel` serialises to a deterministic JSON snapshot; its SHA-256 is the schema hash. The hash is stored in `cms_schema_history` whenever content migrations are applied, so startup can report drift. Field renames are declared (`[Field(FormerNames = ["subtitle"])]`) so the migration generator emits a rename instead of drop and add.

Related decisions: Q2 (schema file for the reference host), Q6b (component storage).

### 4.3 Storage and migrations

Rows 1.12, 8.13, 10.1, 10.4, 10.10; decisions 8, 19, 39, 43, 48, 54, 55, 60.

**Model building.** The EF Core model is generated from `SchemaModel` using shared-type (property bag) entity types, so there is no CLR entity class per content type. This supports attribute-defined and builder-defined types alike, lets one typed `Article` project out of two tables (shared row plus locale row), and keeps the user's class a plain projection. Source-generated row classes were the alternative (Q5); the M1 storage spike (issue B-10) validates the choice on all three providers before the content tables are built.

**Tables per content type.** For `article` (localized, prefix `cms_`):

| Table | Key | Holds |
| --- | --- | --- |
| `cms_document` | `(document_id, locale)` unique; `id` v7 PK | One row per document and locale, any type: `type`, `site_id`, `status`, `draft_version`, `published_version`, `published_at`, `first_published_at`, `deleted_at`, `deleted_by`, `created_*`, `updated_*`, `schedule_publish_at`, `schedule_unpublish_at`. The type-independent state table used by lists across types, trash, scheduling and the MCP `query` tool. |
| `cms_article` | `(document_id, state)` unique | Non-localized fields, one row per document and state (0 draft, 1 published). `site_id` denormalised for partial unique indexes and future partitioning. |
| `cms_article_i18n` | `(document_id, locale, state)` unique | Localized fields, one row per document, locale and state. Absent for non-localized types, whose fields all live in `cms_article`. |
| `cms_reference` | `(source_type, source_document_id, state, locale, field, position)` unique; index on `(target_type, target_id)` | Every relation and media reference in one polymorphic table. Gives "what references X" for media usage tracking, reference integrity at publish and delete warnings in one query. Polymorphic relations (1.14, Should) need no new table. |
| `cms_version` | `(document_id, locale, number)` unique | Full JSON snapshot per save plus `status_after`, `author_id`, `origin`, `client_id`, `message`, `created_at`. |

**Column mapping.** text `varchar(n)`; longText `text`; richText, json, blocks, multi-select and repeatable components `jsonb` (PostgreSQL), `nvarchar(max)` or `json` (SQL Server), `text` with JSON1 (SQLite); integer `int` or `bigint`; decimal `numeric(p,s)`; boolean; date `date`; dateTime `timestamptz` or `datetimeoffset`; email, url, slug `varchar`; single select the enum name as `varchar`; relations and media as `cms_reference` rows only, no denormalised id column.

**Indexes.** Primary and unique keys above; `[Indexed]` fields; `[Unique]` fields as a partial unique index `WHERE state = 1` per `(site_id, locale, field)` with the draft side checked in the validation pipeline; GIN indexes on `jsonb` columns on PostgreSQL when `[Indexed]` is set on a JSON field.

**JSON dialect.** Filters on block and JSON paths compile through `IJsonSqlDialect`, implemented in each data provider package (`jsonb` operators, `JSON_VALUE`, `json_extract`). The supported set in the first release is: block list contains a block of type *X*, and equality or containment on a scalar path one level inside blocks or JSON, identical on all providers. Deeper paths are deferred and will be PostgreSQL-only when they come (Q7).

**Migration streams.** Three kinds of EF migration contexts share one database, each with its own history table:

| Stream | Owner | Generated by | History table |
| --- | --- | --- | --- |
| System tables (`cms_document`, `cms_version`, `cms_reference`, identity, jobs, settings, activity, sites, locales, plugins, locks, outbox) | `FluentCMS.Data.{Provider}` | CI, one set per provider | `cms__migrations` |
| Plugin tables (`cms_media_*`, `cms_webhook_*`, ...) | each plugin package, per provider folder | plugin CI | `cms_{plugin}__migrations` |
| Content type tables (`cms_article`, `cms_article_i18n`, ...) | the host project | `fluentcms migrations add <name>` using the host's `SchemaModel` and provider | `cms_content__migrations` |

At runtime one `CmsDbContext` holds the union model for querying, with the system and plugin tables marked `ExcludeFromMigrations`. The migration runner applies the streams in order system, plugins (dependency order), content, at startup when `Database:MigrateOnStartup` is true (default true on SQLite, false otherwise) or through `fluentcms migrate`. `fluentcms migrate --check` lists pending migrations and exits non-zero.

**Development loop.** With `Database:SchemaSync = Auto` (default in Development on SQLite only) the content stream is skipped and additive changes (new tables, new nullable columns, new indexes) are applied by diffing the model against the database at startup. Destructive changes are reported, never applied. This gives "schema changes apply without restart" for new fields and keeps production on explicit migrations.

**Startup validation.** The runner compares the content model with the database and fails with actionable errors (`FCMS1301: table cms_article is missing column subtitle; run 'fluentcms migrations add AddSubtitle'`).

Related decisions: Q5 (property bags, validated by the spike), Q7 (JSON filtering depth).

### 4.4 Content service and query model

Rows 2.3 to 2.8, 2.14, 2.15, 2.19, 1.7, 1.8.

**Service.** `IContentService` works on `ContentDocument` (an ordered dictionary with typed accessors, used by the Management API, MCP and import) and `IContentService<T>` wraps it with a generated or reflection-based mapper. Both expose:

| Operation | Notes |
| --- | --- |
| `GetAsync(id, GetOptions)` | status, locale, fallback, select, populate |
| `QueryAsync(ContentQuery)` | returns `PagedResult<T>` with items, `total` (optional), `nextCursor` |
| `CreateAsync(entry)` | creates the draft; returns the entry with system fields |
| `UpdateAsync(id, entry, expectedVersion)` | full or partial (JSON merge patch) update of the draft; conflict throws `VersionConflictException` with the current version |
| `DeleteAsync(id)` | trash |
| `RestoreAsync(id)` | from trash |
| `PublishAsync(id, locale?)`, `UnpublishAsync`, `DiscardDraftAsync` | publishing transitions (4.5) |
| `ScheduleAsync(id, ScheduleRequest)`, `CancelScheduleAsync` | per locale |
| `GetVersionsAsync(id, locale)`, `RestoreVersionAsync(id, number)`, `DiffAsync(id, a, b)` | versions |
| `BulkAsync(BulkRequest)` | create many; update, publish, unpublish, delete by id list or by filter with a required cap; per-item result |
| `CountAsync(filter)` | |

**Query model.** `ContentQuery { Filter, Sort[], Page, Cursor, Select[], Populate[], Status, Locale, Fallback, Site, RichTextFormat }`. The filter is an AST (`FieldFilter(path, op, value)`, `And`, `Or`, `Not`) with operators `eq ne in nin lt lte gt gte between contains startsWith null notNull`. The same AST is produced by the REST bracket parser, the GraphQL input types, the MCP `query` tool and the typed builder from the source generator.

REST grammar (decision 21):

```text
GET /api/delivery/v1/article
    ?filter[category][eq]=news
    &filter[author.name][contains]=amir
    &filter[or][0][title][startsWith]=A&filter[or][1][title][startsWith]=B
    &filter[sections][hasBlock]=hero
    &sort=-publishedOn,title
    &page=2&pageSize=20            (or offset=20&limit=20, or cursor=...)
    &fields=title,slug,author.name
    &populate=author,tags          (depth 1 by default; populate=*&depth=2 up to the cap)
    &status=published              (draft requires a preview key or the draft scope)
    &locale=fr-CA&fallback=none
    &richText=html                 (json | html | markdown)
    &withTotal=false
```

**Compilation.** The AST compiles to LINQ over the property-bag entities. Relation paths join through `cms_reference` to the target type's row in the same state and locale (decision 44: no fallback-aware filtering). Block and JSON paths go through `IJsonSqlDialect`. Row rules from the evaluator are appended as an `And` node before compilation, so authorization and filtering are one query.

**Population.** Default depth 1, cap configurable (default 3). Beyond the depth a relation is returned as `{ "id": "...", "type": "author" }`. Cycles stop at the cap. Field selection applies to populated entries with dotted paths.

**Pagination.** Offset and page with `total` (skip the count with `withTotal=false`). Keyset cursor: base64 of the sort key values plus the document id, which is always appended as the final sort key for stability.

**Concurrency.** Each `(document, locale)` has an integer `version`. Writes carry `expectedVersion` (`If-Match: "7"` on REST). A mismatch is rejected with 412 and the current entry in the problem details `current` extension.

**Validation pipeline.** Field type coercion and declarative rules, custom field validators, entry validators, uniqueness (both states, per site and locale), then, on publish, `Required`, `RequiredWhen` and reference integrity.

**Hook pipeline.** `Before` hooks run inside the transaction in `[HookOrder]` order and may mutate the entry or cancel (`HookRejectedException`, mapped to 422). `After` hooks run inside the transaction after the write so plugin tables stay consistent. Events (4.11) are dispatched after commit.

**Response shape.** Flat JSON, system fields at the top level under reserved names, relations inlined by depth:

```json
{
  "id": "0192f2a4-7b1c-7d2e-9f10-2a3b4c5d6e7f",
  "status": "published",
  "locale": "fr-CA",
  "version": 7,
  "createdAt": "2026-11-01T09:12:00Z",
  "updatedAt": "2026-11-01T10:40:00Z",
  "publishedAt": "2026-11-01T10:41:00Z",
  "title": "Bonjour",
  "slug": "bonjour",
  "author": { "id": "0192f2a4-...", "type": "author", "name": "Amir" },
  "tags": [{ "id": "0192f2a4-...", "type": "tag" }],
  "sections": [{ "type": "hero", "heading": "..." }],
  "fallbacks": { "subtitle": "fr" }
}
```

Lists return `{ "items": [...], "total": 120, "page": 2, "pageSize": 20, "nextCursor": "..." }`. A nested `sys` object for system fields was considered and rejected (Q14).

### 4.5 Versioning, publishing, scheduling and trash

Rows 6.1 to 6.11; decisions 38, 39, 40, 41.

**States.** Per `(document, locale)`: `Draft` (never published), `Published` (draft equals published), `Modified` (draft newer than published), `Unpublished` (was published, now withdrawn). Status is stored on `cms_document` and recomputed on every transition.

**Transitions.**

| Operation | Effect | Validation | Event |
| --- | --- | --- | --- |
| Save draft | upsert state 0 rows, `draft_version++`, write `cms_version` | draft rules | `EntryDraftSavedEvent` |
| Publish | copy state 0 rows to state 1, set `published_version`, `published_at` | full rules plus reference integrity | `EntryPublishedEvent` |
| Unpublish | delete state 1 rows, status `Unpublished` | permission | `EntryUnpublishedEvent` |
| Discard draft | copy state 1 rows to state 0 | permission | `EntryDraftDiscardedEvent` |
| Restore version | new draft from snapshot | draft rules | `EntryVersionRestoredEvent` |
| Trash | set `deleted_at`; excluded by the global query filter | permission | `EntryTrashedEvent` |
| Restore from trash | clear `deleted_at` | permission | `EntryRestoredEvent` |
| Purge | delete all rows; separate permission `content.{type}.purge` | | `EntryPurgedEvent` |
| Schedule | row in `cms_schedule`, job enqueued | | `EntryScheduledEvent` |

**Versions.** Every save writes an immutable snapshot (decision 38) with author, origin, client id and an optional message. A per-type cap (default 50) prunes oldest drafts; versions that were published are never pruned. Diff is computed on demand as a field-level structural diff (blocks and rich text compared as trees) and returned as a list of `{ path, kind, before, after }`.

**Reference integrity.** On publish, references to unpublished or trashed entries produce warnings in the response (`warnings[]`) by default; `[PublishPolicy(UnpublishedReferences = Block)]` on a type turns them into a 422.

**Scheduling.** `cms_schedule (id, document_id, locale, action, run_at_utc, time_zone, status, job_id, created_by)`. The job runner executes `ScheduledPublishJob` and `ScheduledUnpublishJob`; `cms_document` mirrors the next scheduled times for list views. Cancel and reschedule update the row and the job.

**Agent-safe publishing.** Nothing in core treats `Origin.Mcp` differently: publish needs `content.{type}.publish` for everyone. The safety comes from the MCP plugin exposing publish as a separate tool and from roles given to agent principals. Origin is recorded on every version and activity row, so agent writes are filterable.

### 4.6 Localization and sites in core

Rows 7.1 to 7.6, 7.8, 7.10 to 7.12 (model and API parts); decisions 42 to 45.

**Tables.** `cms_site (id, name, slug, default_locale, is_default, created_at)`, `cms_site_locale (site_id, code, position, fallbacks json, enabled)`. The first system migration seeds the default site with locale `en`.

**Locale resolution.** `locale` parameter or `X-Cms-Locale` header, else the site's default. `locale=all` returns every locale of an entry as a map. No `Accept-Language` negotiation, to keep Delivery responses cacheable by URL.

**Fallback.** Per site an ordered chain (`fr-CA` to `fr` to `en`), applied per field on read; `fallbacks` in the response names the locale that served each fallen-back field; `fallback=none` disables. Lists filter on the requested locale only (decision 44).

**Sites.** `site_id` is on `cms_document`, `cms_*` content tables, roles, API keys, settings and activity rows. Resolution order: API key binding, `X-Cms-Site` header, `site` parameter, hostname (multi-site plugin), default site. Content types are site-scoped by default; `[Collection("tag", Scope = ContentScope.Global)]` makes one entry set shared by all sites (`site_id` null) and allowed as a relation target from any site.

**Management endpoints.** Sites and locales CRUD live in core (`/api/management/v1/sites`, `/api/management/v1/sites/{id}/locales`) because the data model is core; the multi-site plugin adds hostnames and anything beyond the default site (Q6a).

### 4.7 Identity and the authorization server

Rows 4.1, 4.2, 4.9 to 4.11, 4.13, 4.15, 4.16, 2.12, 2.13; decisions 7, 30, 33.

**Foundation.** ASP.NET Core Identity with `CmsUser` and `CmsRole` (GUID keys) in `cms_user*` and `cms_role*` tables, and OpenIddict server with its stores in `cms_oidc_*`.

**Endpoints.** OpenIddict defaults: `/connect/authorize`, `/connect/token`, `/connect/userinfo`, `/connect/introspect`, `/connect/revoke`, `/connect/logout`, `/.well-known/openid-configuration`. Flows: Authorization Code with PKCE for public clients (admin, MCP clients, CLI), rotating refresh tokens, client credentials for service accounts. Access tokens are JWTs signed with keys from a configured certificate or, when none is configured, a generated key persisted in `cms_signing_key` through Data Protection. Audiences: `fluentcms:management` for admins and keys, `fluentcms:members` for members.

**Hosted pages.** The Authorization Code flow needs server-rendered pages: login, logout, consent (third-party MCP clients), two-factor challenge and recovery codes, forgot and reset password, accept invitation, external login callback, error. They ship as Razor Pages in `FluentCMS.Core` with minimal CSS, brandable through settings (logo, colours, name) and overridable by the host. This is the one piece of UI in the backend.

**Admin users.** Invitation by email (`cms_invitation` with hashed token and expiry), password policy from options, lockout, forced reset flag, deactivate, session list from OpenIddict authorizations with revoke. Password hashing uses Identity's PBKDF2 hasher behind the replaceable `IPasswordHasher<CmsUser>`; Argon2id was considered and deferred to an optional package if demand appears (Q3, which amends row 4.2 of the specification).

**Two-factor.** TOTP through Identity's authenticator support, recovery codes, enforcement per role: a role flagged `RequireTwoFactor` makes the authorize endpoint refuse a session without an `mfa` authentication method reference.

**Admin SSO.** External OIDC providers (Entra ID, Okta, Keycloak and generic OIDC are the same handler with presets) configured in the host configuration and shown read-only in the admin in the first release (runtime editing is a Should). Federation happens on the hosted login page: auto-provisioning, default role, group claim to role mapping, optional local-login disable.

**API keys.** `cms_api_key (id, name, class, key_prefix, key_hash, role_id, site_ids json, type_names json, expires_at, last_used_at, rate_limit, revoked_at, created_by)`. Classes: `delivery` (read published), `preview` (read drafts too), `management` (whatever the bound role allows). Format `fcms_dk_...`, `fcms_pk_...`, `fcms_mk_...` with 32 random bytes, shown once, stored as SHA-256. Sent as `Authorization: ApiKey <key>` or `X-Api-Key`. A dedicated authentication handler produces a principal with the role's claims and the key class as scope.

**Members.** A separate pool (decision 33): `cms_member*` tables with their own `UserManager<CmsMember>`, `cms_member_group` and membership table. REST endpoints under `/api/members/v1`: `register`, `verify-email`, `resend-verification`, `forgot-password`, `reset-password`, `me`. Tokens come from OpenIddict with the members audience; login runs through the hosted pages (code with PKCE) and, when `Members:AllowDirectLogin` is on, through a custom `member_password` grant for first-party apps (Q4). External providers for members: Google and Microsoft (in-box handlers), GitHub and Apple (Q10), generic OIDC. Member groups appear in row rules and public access.

**Security defaults.** Closed by default: no anonymous Delivery access until the Public role grants it; CORS allow-list with no wildcard; secure cookie flags; strict rate limit on `/connect/*` and `/api/members/v1/*`; secrets only from configuration providers; Production refuses to start with a generated signing key unless `Security:AllowGeneratedSigningKey` is set.

### 4.8 Authorization

Rows 4.3 to 4.8, 4.12, 4.17, 8.14; decisions 31, 32.

**Permission catalogue.** Core permissions (`users.manage`, `roles.manage`, `apikeys.manage`, `sites.manage`, `settings.{area}.manage`, `jobs.manage`, `activity.read`, `schema.read`, `plugins.manage`, `transfer.manage`), generated per type (`content.{type}.create|read|update|delete|publish|unpublish|restore|purge`), and plugin-declared ones through `IPermissionProvider`. All stored in `cms_role_permission (role_id, permission, constraints json)`.

**Constraints.** Additive only, no deny rules. A grant may be constrained to sites, locales, a field allow-list (`fields: ["title", "slug"]`, null means all) and a row rule name. A principal's effective permission set is the union of its roles' grants.

**Roles.** `cms_role` has an optional `site_id` (null is instance-wide). Built-ins seeded by the first migration: Super Admin (everything), Editor, Author (own entries row rule), Public (the anonymous principal per site, which is how public access configuration is stored), Member (default for the member pool).

**Row rules.** Registered in code with a name and used by roles as data:

```csharp
cms.Authorization.AddRowRule<Article>("own-entries",
    ctx => article => article.CreatedBy == ctx.Principal.Id);
cms.Authorization.AddRowRule<Article>("region",
    ctx => article => article.Region == ctx.Principal.Claim("region"));
```

The evaluator compiles the predicate into the list query and checks it on single-entry operations. JSON-authored rules are a Should (decision 32).

**Evaluator.** `IAuthorizationEvaluator` caches the effective permission set per principal for the request; a `permissions_version` stamp on roles invalidates it across instances. Scopes narrow but never widen: a token with `draft` may read drafts if its roles allow; `management` tokens are bounded by roles.

**Introspection and MCP.** Both call the evaluator to drop types and fields the principal cannot read, so generated admin forms and agent tools match permissions (4.17).

### 4.9 Activity log

Row 4.14; the backend half of 3.26 and the data for 4.20 (Should).

`cms_activity (id v7, site_id, occurred_at, actor_type, actor_id, actor_name, origin, client_id, action, target_type, target_id, target_label, locale, details json, correlation_id, ip)`. Written by event subscribers, so plugins get logged for free when they raise events. Retention job, default 30 days, configurable per site. `GET /api/management/v1/activity` with filters on actor, origin, action, target, date range; permission `activity.read`.

### 4.10 REST APIs

Rows 2.1, 2.2, 2.6 to 2.9, 2.12 to 2.20, 10.8, 10.12.

**Delivery API.** Read-only, published by default, under `/api/delivery/v1`:

| Route | Notes |
| --- | --- |
| `GET /{type}` | list with the query grammar; singletons return the single entry |
| `GET /{type}/{id}` | by id |
| `GET /{type}/by/{field}/{value}` | by a unique field such as slug |
| `GET /media/{id}` | asset metadata and URLs (media plugin) |
| `GET /openapi/v1.json`, `GET /scalar` | document and UI (host-level, not versioned) |

`status=draft` requires a preview key or a bearer token with the `draft` scope. Responses carry `ETag` (hash of version, locale and query), `Last-Modified` and a configurable `Cache-Control` (default `public, max-age=60, stale-while-revalidate=300`). Output caching (decision 56) tags responses with `site:{id}`, `type:{site}:{type}` and `entry:{id}`; publish, unpublish and media events evict by tag and raise `CacheInvalidatedEvent` for webhook-driven CDN purges.

**Management API.** Under `/api/management/v1`, bearer or management key:

| Group | Routes |
| --- | --- |
| content | `/content/{type}` list and create; `/content/{type}/{id}` get, patch, delete; `/publish`, `/unpublish`, `/discard`, `/schedule`; `/versions`, `/versions/{n}/restore`, `/versions/{a}/diff/{b}`; `/content/{type}/bulk`; `/content/{type}/trash` list, `/trash/{id}/restore`, `/trash/{id}` purge |
| schema | `/schema`, `/schema/types/{type}`, `/schema/snapshot` |
| identity | `/users`, `/users/{id}/sessions`, `/invitations`, `/roles`, `/permissions` (catalogue), `/api-keys`, `/members`, `/member-groups` |
| sites | `/sites`, `/sites/{id}/locales` |
| settings | `/settings/{area}` get and put, with the JSON schema of the settings type |
| jobs | `/jobs`, `/jobs/{id}`, `/jobs/{id}/retry`, `/jobs/{id}/cancel`, `/jobs/dead-letter` |
| activity | `/activity` |
| plugins | `/plugins`, `/plugins/{name}/sites/{site}` enable and disable |
| transfer | `/transfer/export` (job, then download), `/transfer/import` (upload, dry run, apply) |
| system | `/system/diagnostics`, `/system/health` (detailed) |
| me | `/me`, `/me/sessions`, `/me/two-factor` |

**Error model.** RFC 9457 `application/problem+json` on every error: `type` (`urn:fluentcms:error:{code}` until a docs domain exists), `title`, `status`, `detail`, `code` (`FCMS...`), `errors[]` of `{ path, code, message }` for validation, `current` on 412, `traceId`.

**OpenAPI 3.1.** `Microsoft.AspNetCore.OpenApi` with document transformers that add one schema per content type and per block, block unions as `oneOf` with discriminator, the filter grammar as documented parameters, security schemes (`oauth2` authorization code, `apiKey`), examples from the sample pack. Served per deployment at `/openapi/v1.json`; Scalar UI at `/scalar` (dependency approved in Q9).

**Rate limiting.** ASP.NET Core rate limiting with policies `delivery` (partition by key or IP), `management` (by principal), `auth` (strict, by IP). `RateLimit-*` headers and `Retry-After`. Per-key overrides from `cms_api_key.rate_limit`.

**CORS and headers.** Separate allow-lists for Delivery and Management from configuration and settings; no wildcard by default; security headers middleware (`X-Content-Type-Options`, `Referrer-Policy`, `Content-Security-Policy` with `frame-ancestors` for the hosted pages).

**Request limits.** Body size, page size cap (default 100), populate depth cap, filter node cap, upload caps (media plugin).

### 4.11 Events and hooks

Rows 8.4, 8.16, 6.10.

**Event bus.** `IEventBus.PublishAsync<TEvent>` with `IEventHandler<TEvent>` subscribers, in-process. Events raised during a transaction are buffered and dispatched after commit, in `[HookOrder]` order; a failing handler is logged and does not affect the write. Handlers that must not be lost (webhooks, activity log, cache invalidation) subscribe through the outbox: the event is also written to `cms_outbox` in the same transaction and a runner job drains it, so a crash between commit and dispatch loses nothing.

**Catalogue.** Content: `EntryDraftSavedEvent`, `EntryPublishedEvent`, `EntryUnpublishedEvent`, `EntryDraftDiscardedEvent`, `EntryVersionRestoredEvent`, `EntryTrashedEvent`, `EntryRestoredEvent`, `EntryPurgedEvent`, `EntryScheduledEvent`, `EntryScheduleCancelledEvent`. Media (plugin): asset uploaded, updated, replaced, moved, deleted. Identity: user created, updated, deactivated, role changed, key created, key revoked, login succeeded, login failed, member registered. Settings: `SettingsChangedEvent`. Sites: created, updated. Plugins: enabled, disabled. Infrastructure: `CacheInvalidatedEvent`, `JobFailedEvent`, `JobDeadLetteredEvent`.

**Hooks.** `IBeforeValidateHook<T>`, `IBeforeCreateHook<T>`, `IAfterCreateHook<T>`, the same pairs for Update, Delete, Publish, Unpublish, Restore; `T` is a content class or `ContentDocument` for all types. `HookContext` carries the `OperationContext`, the entry, the previous state, the changed field paths and `Cancel(code, message)`. Ordering: `[HookOrder(int)]` with plugin dependency order as the tiebreak (decision 49).

### 4.12 Job runner

Rows 6.6, 8.10, 10.7 (locks), 10.20 (Should); decision 40.

**Tables.** `cms_job (id, type, payload json, queue, site_id, run_at, priority, status, attempts, max_attempts, lease_until, lease_owner, last_error, created_at, created_by, origin, completed_at)`, `cms_job_attempt (job_id, attempt, started_at, finished_at, outcome, error)`, `cms_recurring_job (name, cron, payload json, queue, next_run_at, enabled)`.

**API.** `IJob<TPayload>.ExecuteAsync(payload, JobContext)`; `IJobScheduler.EnqueueAsync`, `ScheduleAsync(runAt)`, `AddRecurring(name, cron)`. Jobs run under a `System` principal with `Origin.Job` unless the payload carries an actor to impersonate for audit (scheduled publish records who scheduled it).

**Runner.** A hosted service with a configurable worker count per queue. Claiming uses the provider dialect: `FOR UPDATE SKIP LOCKED` (PostgreSQL), `UPDLOCK, READPAST` (SQL Server), optimistic `UPDATE ... WHERE lease_until IS NULL` (SQLite). Leases with heartbeat; retries with exponential backoff and jitter; dead-letter after `max_attempts`; graceful shutdown; multi-instance safe without extra infrastructure. `IJobRunner` is an interface so a Hangfire adapter can replace it.

**Built-in jobs.** Scheduled publish and unpublish, trash purge, version pruning, activity retention, outbox dispatch, webhook delivery (plugin), media variant cleanup (plugin).

**Management.** `/jobs` endpoints (4.10), permission `jobs.manage`. Cron parsing uses Cronos (MIT), approved in Q9.

### 4.13 Plugin host, server side

Rows 8.1 to 8.4, 8.6, 8.10 to 8.16; decisions 11, 13, 14, 46 to 49.

**Shape.**

```csharp
[FluentCmsPlugin("media", Version = "1.0.0", SdkRange = "[1.0.0,2.0.0)")]
public sealed class MediaPlugin : IPlugin
{
    public void ConfigureServices(IPluginServiceBuilder plugin)
    {
        plugin.Services.AddScoped<IMediaService, MediaService>();
        plugin.AddFieldType<MediaFieldType>();
        plugin.AddPermissions<MediaPermissions>();
        plugin.AddSettings<MediaSettings>();
        plugin.AddModel<MediaModelContributor>();      // entities with prefix cms_media_
        plugin.AddMigrations(typeof(MediaPlugin).Assembly);
        plugin.AddJob<VariantCleanupJob>();
        plugin.AddEventHandler<EntryPurgedEvent, ReleaseReferencesHandler>();
    }

    public void Configure(IPluginAppBuilder app)
    {
        app.MapGroup("/media").MapMediaEndpoints();   // under the CMS route group, CMS auth by default
        app.MapManagement("/media").MapMediaManagementEndpoints();
    }
}
```

**Registration.** Explicit `AddPlugin<T>()` wins and fixes order; `[assembly: FluentCmsPlugin(typeof(MediaPlugin))]` on referenced assemblies is discovered through the compile-time dependency context. No folder scan, no runtime loading.

**Validation at startup.** SDK range against the running SDK version, dependencies present, duplicate names; failure stops the host with `FCMS14xx` errors.

**Per-site enablement.** `cms_plugin_site (plugin, site_id, enabled)` read through `IPluginState`. A disabled plugin's endpoints return 404 for that site, its hooks, events, jobs and MCP contributions are skipped, and the Management plugin list reports it disabled so the admin hides its UI.

**Provider slots.** Defined in the SDK, defaulted in core, replaceable by plugins or the host: `IFileStorage` (media plugin, local default), `IEmailSender` (core; SMTP through MailKit, plus a log-only sender for Development; Q9), `ISearchIndex` (search plugin), `IDistributedCache` (BCL memory default), `IDistributedLock` (core, `cms_lock` table default), `IImageProcessor` (media plugin), `IChatClient` and `IEmbeddingGenerator` from Microsoft.Extensions.AI (AI assist plugin).

**Settings.** `[Settings("media")] sealed class MediaSettings` is persisted per site in `cms_setting (site_id, key, json, updated_at, updated_by)` with instance defaults and per-site overrides. `ISettings<T>.GetAsync(site)` reads it; `SettingsChangedEvent` follows writes; the Management API exposes get and put per area with the JSON schema of the type so the admin can render it.

**Manifest exposure.** `GET /api/management/v1/plugins` lists name, version, SDK range, dependencies, contributions (field types, permissions, endpoints, jobs, tools) and enablement per site.

### 4.14 Transfer and schema snapshot

Rows 10.9, 10.10, 1.19.

**Export package.** A zip: `manifest.json` (FluentCMS version, schema hash, site, created at, options), `schema.json`, `content/{type}.jsonl` (both states, versions optional), `references.jsonl`, `media.jsonl` (metadata and storage keys; binaries optional), `settings.json`, `roles.json` (optional; users never). Produced by a job; downloaded from `/transfer/export/{jobId}`.

**Import.** Schema hash check (strict, or compatible when only additive differences), upsert by id, per-type selection, site remap, dry run with a report of creates, updates, skips and conflicts, then apply as a job with progress.

**Schema tools.** `fluentcms schema snapshot` from code; `fluentcms schema diff --url` against a running instance's introspection; non-zero exit on drift so CI can gate deploys.

### 4.15 Infrastructure and operations

Rows 10.3, 10.5 to 10.8, 10.11 to 10.15.

**Configuration.** Everything under the `FluentCms` section (`FluentCms:Database:Provider`, `FluentCms:Database:MigrateOnStartup`, `FluentCms:Security:*`, `FluentCms:Delivery:Cache:*`, `FluentCms:Jobs:*`), bound to options with data annotations and `ValidateOnStart`. Environment variables map through the standard `__` separator, which is how the reference host is configured. Secret providers (user secrets, Key Vault, AWS Secrets Manager) are the host's configuration builders; core takes no dependency on them.

**Health.** `/health/live` (process) and `/health/ready` (database, storage provider, job runner heartbeat, plugins) through `Microsoft.Extensions.Diagnostics.HealthChecks`; detailed JSON at `/api/management/v1/system/health` behind a permission.

**Observability.** One `ActivitySource` (`FluentCMS`) with spans for content operations, hooks, jobs, webhooks and MCP calls; one `Meter` with request counts by surface, query duration, job latency, cache hit ratio, webhook outcomes; structured logs with event ids and `FCMS` codes. Exporters are the host's choice. Nothing is sent to the project.

**Multi-instance.** Stateless API; Data Protection and signing keys persisted to the database by default; jobs lease through the database; `IDistributedLock` defaults to a `cms_lock` table and gets a Redis provider in `FluentCMS.Caching.Redis`; output cache in memory by default or Redis; media on shared storage.

**Reverse proxy.** Forwarded headers, `PathBase` option for sub-path hosting, HTTPS redirection and HSTS options; documented for Nginx, Caddy, YARP, Azure Front Door and Cloudflare.

**Startup diagnostics.** A `StartupReport` (providers, plugins and versions, pending migrations, schema drift, insecure defaults, environment) logged once at start and served at `/api/management/v1/system/diagnostics`.

**Backups.** Documented per database and storage provider; `fluentcms backup` bundles an export package and the media files into one archive for small installs.

**Supported platforms.** Linux and Windows containers, Azure App Service, AWS ECS, Kubernetes, bare `dotnet run`. .NET 10 LTS baseline.

### 4.16 Reference host

Rows 10.2, 11.5 (sample data); decision 3.

`src/FluentCMS.Host`: an ASP.NET Core app referencing `FluentCMS.Core`, all three data providers (chosen by configuration), every first-party plugin and, later, the admin at `/admin`. Content types come from a schema file loaded through the fluent builder (`FLUENTCMS_SCHEMA=/config/schema.json`) or from the built-in sample pack (`FLUENTCMS_SAMPLE=blog`) for evaluation; see Q2. SQLite with zero configuration by default. Multi-stage Dockerfile on the chiseled `aspnet:10.0` image, non-root, multi-arch; a compose file with PostgreSQL and MinIO; health probes; migrations on start for SQLite and an init container pattern (`fluentcms migrate`) documented for the others.

## 5. First-party plugins, server halves

### 5.1 Media plugin

Rows 5.1 to 5.12; 5.13 to 5.18 as Should; decisions 34 to 37.

**Tables.** `cms_media_asset (id, site_id, folder_id, file_name, storage_provider, storage_key, mime_type, size, checksum, width, height, duration, title, alt json per locale, caption json per locale, credit, tags json, focal_x, focal_y, is_private, created_*, updated_*, deleted_at)`, `cms_media_folder (id, site_id, parent_id, name, path)`, `cms_media_variant (asset_id, params_hash, storage_key, size, created_at)`, `cms_media_preset (site_id, name, params json)`.

**Storage.** `IFileStorage` (stream open, write, delete, exists, metadata) with per-site provider selection; providers: local disk (allowed in production with a documented warning), S3-compatible, Azure Blob.

**Upload.** `POST /api/management/v1/media` multipart, multi-file; size limits; MIME allow-list with magic-byte sniffing; executables rejected; SVG sanitised or rejected per setting. Upload from URL is a Should.

**Transforms.** `GET /media/{id}/{filename}?w=&h=&fit=cover|contain|fill&q=&f=auto|webp|avif|jpeg|png&fx=&fy=&preset=hero&sig=` where the filename is ignored on lookup (decision 36). `f=auto` negotiates by `Accept` with `Vary: Accept`. Results are cached as variants in the same storage. Limits on dimensions and operation count. Signatures are HMAC over the canonical parameter string with a key from Data Protection; `Media:RequireSignedUrls=false` allows unsigned URLs for trusted setups. `IImageProcessor` has a SixLabors.ImageSharp implementation by default (Q8). ImageSharp is under the Six Labors Split License, free for open source projects and companies below its revenue threshold; the documentation carries that note and the slot stays replaceable.

**Media Delivery API.** `GET /api/delivery/v1/media/{id}` returns metadata and URL templates with the same key classes and cache rules as content.

**Usage and integrity.** `cms_reference` rows with `target_type = media` answer `GET /media/{id}/usage`; delete is refused while referenced unless `force=true`, which nulls the references; replace keeps the id and invalidates variants.

**Private assets.** `is_private` assets are never served from `/media/{id}`; `POST /media/{id}/download-url` returns a short-lived signed URL after a folder permission check (`media.folder.{id}.read`).

**Field type.** Registers `media` with `MediaRef` and `MediaRefList`, OpenAPI schema, filters by id, and an `IMediaResolver` used by population so `populate=hero` returns metadata and URLs. Rich text and block media references use the same resolver.

### 5.2 Webhooks plugin

Webhooks appear in rows 2.2, 2.20, 6.10, 8.16 and 10.8, and decision 47 names them a first-party plugin.

**Tables.** `cms_webhook (id, site_id, name, url, secret (Data Protection encrypted), events json, types json, headers json, enabled, created_*)`, `cms_webhook_delivery (id, webhook_id, event_id, attempt, state, status_code, duration_ms, request_excerpt, response_excerpt, error, next_retry_at, created_at)`.

**Delivery.** Subscribes through the outbox; each delivery is a job. Payload `{ id, event, occurredAt, site, origin, actor, data: { type, documentId, locale, status, version }, links }`. Headers `X-FluentCMS-Event`, `X-FluentCMS-Delivery`, `X-FluentCMS-Signature: t=<unix>,v1=<hex HMAC-SHA256(secret, t + "." + body)>`. Retries at 1m, 5m, 30m, 2h, 24h by default, then dead; manual redeliver; test ping; log retention 30 days.

**Endpoints.** `/api/management/v1/webhooks` CRUD, `/{id}/deliveries`, `/{id}/deliveries/{d}/redeliver`, `/{id}/ping`.

### 5.3 GraphQL plugin

Row 2.16; decision 24.

HotChocolate schema built from `SchemaModel` at startup: one object type per content type and block, input types for create and update, filter inputs mirroring the REST operators, sort inputs, offset and cursor pagination, population through selection. Mutations per type: create, update, delete, publish, unpublish. One endpoint `/graphql` authenticated like REST; a delivery key sees queries over published content only, a management principal sees mutations its roles allow. Depth and complexity limits, introspection off in Production for unauthenticated callers, optional persisted queries. `IGraphQLSchemaContributor` lets plugins add types.

### 5.4 MCP plugin

Rows 9.1 to 9.13; 9.16 to 9.21 as Should; decisions 50 to 52.

**Server.** Streamable HTTP at `/mcp` through `ModelContextProtocol.AspNetCore`, enabled per site, in-process. Authentication: bearer tokens from the authorization server or API keys. RFC 9728 protected resource metadata at `/.well-known/oauth-protected-resource`; the authorization server metadata is OpenIddict's discovery document; dynamic client registration (RFC 7591) at `/connect/register` is implemented over OpenIddict's application manager. A registration creates a pending client that an administrator approves and assigns a role before tokens are issued; an open-with-rate-limit mode is a setting for trusted setups (Q11).

**Principal.** Every call runs as the authenticated user or key, with the same `IAuthorizationEvaluator`, site and locale scoping. The tool list is filtered to what the principal may do.

**Tools.** For each exposed type (`Mcp:ExposedTypes`, default all readable types, capped): `{type}_list`, `{type}_get`, `{type}_search`, `{type}_create_draft`, `{type}_update_draft`, `{type}_discard_draft`, `{type}_delete`. Shared: `publish`, `unpublish`, `schedule` (require the publish permission), `query` (type plus filter JSON for the long tail), `media_list`, `media_search`, `media_get`, `media_upload` (URL or base64), `media_update`. Input schemas are generated from `SchemaModel` filtered by field permissions.

**Resources and prompts.** `fluentcms://schema`, `fluentcms://schema/{type}`, `fluentcms://sites/{site}/guidelines`, `fluentcms://types/{type}/instructions`. Prompts from `IMcpPromptProvider` implementations and from editors, stored in `cms_mcp_prompt`.

**Safety.** `dryRun` defaults to true on delete and bulk tools and returns the plan with a `confirmationToken` (HMAC of the plan, five-minute validity); execution requires the token; bulk operations need an explicit cap; per-client call limits, payload caps, result truncation with paging.

**Origin tagging.** `Origin.Mcp`, client id and principal on every write.

**Instructions and clients.** `cms_mcp_instruction (site_id, type nullable, text, updated_*)` feeds resources and tool descriptions. `/api/management/v1/mcp/clients` lists OpenIddict applications flagged as MCP clients with last activity and revoke; `/mcp/prompts` and `/mcp/instructions` manage the rest.

### 5.5 AI assist plugin

Rows 9.14, 9.15; 9.16 to 9.20 as Should; decision 53.

Microsoft.Extensions.AI `IChatClient` with providers for OpenAI, Azure OpenAI, Anthropic, Ollama and OpenAI-compatible endpoints, configured per site with keys from configuration or Data Protection encrypted settings. Actions: rewrite, shorten, expand, fixGrammar, title, summary, seo, translate, altText. Endpoint `POST /api/management/v1/ai/actions/{action}` with `{ type, field, locale, text or documentId, options }` returning `{ suggestion, usage }`, optionally streamed over SSE. Results are suggestions; nothing is written to content by the plugin. `cms_ai_usage (site_id, user_id, action, provider, model, prompt_tokens, completion_tokens, duration_ms, succeeded, created_at)`. Permission `ai.use`; per-type and per-field opt-in in settings.

### 5.6 Multi-site plugin

Rows 7.10 to 7.13 (beyond the core model), 7.15 and 7.18 as Should.

`cms_site_host (site_id, hostname, is_primary)` and a middleware resolving the site from the request host before the core resolver's header and parameter rules. Site creation beyond the default, per-site storage provider selection (Should), cross-site copy with relation remapping (Should). Q6a fixes what is core and what is plugin.

### 5.7 Search, SSO providers, rendering

Search (2.23, 8.11): `ISearchIndex` slot in the SDK from the start; the plugin provides PostgreSQL FTS and SQLite FTS5 and the `q=` parameter (Should). SSO provider presets beyond generic OIDC and SAML (4.18) are Should. The rendering plugin (Blazor components and Razor tag helpers) is Should and mostly not backend; its server pieces (rich text to HTML, draft preview middleware, sitemap) reuse `RichText/` renderers from core.

## 6. Tooling that targets the backend

Rows 11.1 to 11.6, 11.12, 11.13.

### 6.1 CLI

`fluentcms` dotnet tool, also published as standalone binaries later (decision 59). Works against the host project (`--project`, design-time services) or a running instance (`--url`, `--token`).

| Command | Does |
| --- | --- |
| `new host`, `new plugin` | wraps `dotnet new fluentcms` and `fluentcms-plugin` |
| `migrations add <name>`, `migrations list`, `migrations script`, `migrations remove` | content migration stream for the host's provider |
| `migrate [--check]` | applies all streams in order; `--check` lists pending and exits non-zero |
| `schema snapshot`, `schema diff --url`, `schema validate` | snapshot from code, drift against an instance, validation errors |
| `generate csharp`, `generate typescript`, `generate openapi` | from a running instance or from code |
| `export`, `import --dry-run` | transfer packages |
| `apikey create`, `apikey list`, `apikey revoke` | |
| `user create`, `user invite`, `user reset-password`, `user add-role` | |
| `jobs list`, `jobs run <name>`, `jobs retry <id>` | |
| `mcp` | stdio to Streamable HTTP proxy for IDE clients |
| `backup` | export package plus media archive |
| `doctor` | the startup report without starting the host |

### 6.2 Templates

`fluentcms`: an API host with `Program.cs`, `appsettings.json`, a sample `Article` type, a `Migrations/` folder, a Dockerfile and a README; options for provider and sample content. `fluentcms-plugin`: a server project with manifest, settings, a hook, an endpoint, a migration and a test project on `FluentCMS.Testing`; the admin half is added when the admin exists.

### 6.3 Test host

`FluentCmsTestHost` on `WebApplicationFactory`: `UseSqlite(inMemory or file)`, `WithContentTypes<...>()`, `WithPlugin<T>()`, fixtures `CreateUserAsync(role)`, `CreateApiKeyAsync(class)`, `AsUser(user)` and `AsKey(key)` HTTP clients, `Content<T>()` for in-process calls, `Seed(...)`. The project's own integration tests use it from milestone M1, which is why it ships early.

### 6.4 Source generator and analyzers

`FluentCMS.Generators` emits, per content type in the host: a typed filter and sort builder (`ArticleFields.Title.Contains("x")`), a reflection-free `ContentDocument` mapper, and block union helpers. `FluentCMS.Analyzers` reports `FCMS0xxx` diagnostics: invalid attribute combinations, reserved names, slug source missing, relation to a non-content type, `Localizable` on a non-localized type, a filtered field without `Indexed` (info), obsolete SDK members. Client generation from OpenAPI follows Q15: an own TypeScript emitter for the `d.ts` and the typed query builder, and the C# generator (Kiota or own) decided in issue B-93 once the OpenAPI document exists.

## 7. Backend feature inventory

Every Must row of the specification that has backend work, with its area and milestone. Should rows are listed per category; Could and Won't rows are omitted.

### 7.1 Content modeling (category 1)

| Row | Feature | Backend part | Area | Milestone |
| --- | --- | --- | --- | --- |
| 1.1 | Code-first C# schema | attributes, builder, `SchemaModel` | 4.2 | M1 |
| 1.2 | Collections and singletons | kinds, singleton semantics | 4.2 | M1 |
| 1.3 | Core scalar fields | field type registry | 4.2 | M1 |
| 1.4 | Media and relation fields | relation type, `cms_reference`; media type in plugin | 4.2, 4.3, 5.1 | M1, M3 |
| 1.5 | Components | flatten or JSON | 4.2, 4.3 | M1 |
| 1.6 | Typed block field | `BlockList`, OpenAPI unions | 4.2, 4.10 | M1, M2 |
| 1.7 | Validation | pipeline, custom validators | 4.4 | M1 |
| 1.8 | Conditional fields | `Condition`, publish enforcement | 4.2, 4.4 | M1 |
| 1.9 | Structured rich text | ProseMirror model, HTML and Markdown renderers | 4.2, `RichText/` | M1 |
| 1.10 | Field metadata | introspection | 4.2 | M1 |
| 1.11 | System fields | `ContentItem`, `cms_document` | 4.2, 4.3 | M1 |
| 1.12 | Schema evolution | migration streams, renames, startup validation | 4.3 | M1 |
| 1.13 | Schema introspection API | endpoint, permission filter | 4.2, 4.8 | M2 |

Should: 1.14 polymorphic relations, 1.15 computed and reverse fields, 1.16 hierarchy and taxonomy, 1.17 extra field types, 1.18 declarative indexes, 1.19 schema export (snapshot is already Must through 10.10).

### 7.2 APIs (category 2)

| Row | Feature | Backend part | Area | Milestone |
| --- | --- | --- | --- | --- |
| 2.1 | Delivery API | read endpoints, caching | 4.10 | M2 |
| 2.2 | Management API | all groups | 4.10 | M2 |
| 2.3 | In-process typed content service | `IContentService<T>` | 4.4 | M1 |
| 2.4 | Filtering | AST, bracket parser, compiler | 4.4 | M1, M2 |
| 2.5 | Sort, pagination, selection, population | query model | 4.4 | M1 |
| 2.6 | Status and locale parameters | query model, resolvers | 4.4, 4.6 | M1 |
| 2.7 | Compact response shape | serializer | 4.4 | M2 |
| 2.8 | Error model | problem details | 4.10 | M2 |
| 2.9 | OpenAPI 3.1 | transformers, Scalar | 4.10 | M2 |
| 2.10, 2.11 | Generated C# SDK and TypeScript client | CLI `generate` only | 6.1 | M6 |
| 2.12 | API keys and scopes | key classes, handler | 4.7 | M2 |
| 2.13 | Bearer tokens | OpenIddict | 4.7 | M2 |
| 2.14 | Bulk operations | `BulkAsync`, endpoint | 4.4 | M1, M2 |
| 2.15 | Optimistic concurrency | version, `If-Match` | 4.4 | M1, M2 |
| 2.16 | GraphQL | plugin | 5.3 | M4 |
| 2.17 | Rate limiting | policies | 4.10 | M2 |
| 2.18 | CORS and security headers | middleware | 4.10 | M2 |
| 2.19 | Keyset pagination | cursor | 4.4 | M1 |
| 2.20 | HTTP caching | headers, output cache, tags | 4.10 | M2 |

Should: 2.21 separately hostable Delivery (`MapFluentCmsDelivery()` only), 2.22 aggregations, 2.23 full-text `q=`, 2.24 change feed, 2.25 more SDK languages.

### 7.3 Identity and permissions (category 4)

| Row | Feature | Backend part | Area | Milestone |
| --- | --- | --- | --- | --- |
| 4.1 | Authorization server | OpenIddict, hosted pages | 4.7 | M2 |
| 4.2 | Admin users | Identity, invitations, sessions | 4.7 | M2 |
| 4.3 | Roles | `cms_role`, site scope, built-ins | 4.8 | M1 |
| 4.4 | Type-level permissions | catalogue, evaluator | 4.8 | M1 |
| 4.5 | Field-level permissions | field allow-list constraint, response filter | 4.8 | M1 |
| 4.6 | Row-level rules | rule registry, query append | 4.8 | M1 |
| 4.7 | Locale and site scoping | constraints | 4.8 | M1 |
| 4.8 | Public access | Public role per site | 4.8 | M1 |
| 4.9 | API keys | table, handler | 4.7 | M2 |
| 4.10 | Member authentication | member pool, endpoints, tokens | 4.7 | M5 |
| 4.11 | External providers for members | handlers | 4.7 | M5 |
| 4.12 | Checks in one place | evaluator, architecture test | 2.3, 3.4 | M1 |
| 4.13 | Security defaults | options, startup refusals | 4.7, 4.15 | M2 |
| 4.14 | Activity log | table, subscribers, retention, endpoint | 4.9 | M1, M2 |
| 4.15 | Admin SSO | federation on hosted login | 4.7 | M2 |
| 4.16 | Two-factor | TOTP, per-role enforcement | 4.7 | M2 |
| 4.17 | Permission-aware introspection | evaluator in introspection | 4.2, 4.8 | M2 |

Should: 4.18 SAML, 4.19 passkeys, 4.20 full audit log, 4.21 service accounts (client credentials exist in M2; the admin management of them is Should), 4.22 member profile fields, 4.23 IP allow-lists, 4.24 session and device management beyond the session list.

### 7.4 Media (category 5)

| Row | Feature | Backend part | Area | Milestone |
| --- | --- | --- | --- | --- |
| 5.1 | Storage provider abstraction | `IFileStorage`, three providers | 5.1 | M3 |
| 5.2 | Upload | endpoint, sniffing, limits | 5.1 | M3 |
| 5.3 | Asset metadata | table, localized alt and caption | 5.1 | M3 |
| 5.4 | Folders | table, permissions | 5.1 | M3 |
| 5.5 | On-demand transforms | pipeline, variants | 5.1 | M3 |
| 5.6 | Signed transform URLs | HMAC | 5.1 | M3 |
| 5.7 | Focal point and presets | columns, `cms_media_preset` | 5.1 | M3 |
| 5.8 | Image processing provider | `IImageProcessor` | 5.1 | M3 |
| 5.9 | Media Delivery API | endpoint | 5.1 | M3 |
| 5.10 | Usage tracking | `cms_reference` queries, replace | 5.1 | M3 |
| 5.11 | Private assets | signed download URLs | 5.1 | M3 |
| 5.12 | Media in rich text and blocks | `IMediaResolver` | 5.1, `RichText/` | M3 |

Should: 5.13 bulk operations, 5.14 video and audio metadata, 5.15 resumable upload, 5.16 document previews, 5.17 upload from URL, 5.18 duplicate detection.

### 7.5 Publishing (category 6)

| Row | Feature | Backend part | Area | Milestone |
| --- | --- | --- | --- | --- |
| 6.1 | Draft and published states | `cms_document`, state rows | 4.5 | M1 |
| 6.2 | Publish, unpublish, discard | transitions | 4.5 | M1 |
| 6.3 | Version on every write | `cms_version`, pruning | 4.5 | M1 |
| 6.4 | Diff and restore | structural diff, restore | 4.5 | M1 |
| 6.5 | Scheduled publish and unpublish | `cms_schedule`, jobs | 4.5, 4.12 | M1 |
| 6.6 | Job runner | runner | 4.12 | M1 |
| 6.7 | Publish-time validation | pipeline, reference integrity | 4.4, 4.5 | M1 |
| 6.8 | Optimistic concurrency | see 2.15 | 4.4 | M1 |
| 6.9 | Soft delete and trash | `deleted_at`, purge job | 4.5 | M1 |
| 6.10 | Publishing events | catalogue | 4.11 | M1 |
| 6.11 | Agent-safe publishing | origin tagging, MCP tool split | 4.5, 5.4 | M1, M4 |

Should: 6.12 approval workflows, 6.13 workflow engine extension point, 6.14 document locking, 6.15 autosave (backend: lightweight saves without versions), 6.16 version labels, 6.17 bulk publish and schedule (bulk exists; the list-view part is admin), 6.18 content calendar data.

### 7.6 Localization and multi-site (category 7)

| Row | Feature | Backend part | Area | Milestone |
| --- | --- | --- | --- | --- |
| 7.1 | Locales per site | `cms_site_locale`, BCP 47 validation | 4.6 | M1 |
| 7.2 | Per-field localizable flag | attribute, `_i18n` tables | 4.2, 4.3 | M1 |
| 7.3 | Row-per-locale storage | tables | 4.3 | M1 |
| 7.4 | Locale in every API | resolver, `locale=all` | 4.4, 4.6 | M1, M2 |
| 7.5 | Fallback chain | per-field fallback, `fallbacks` | 4.6 | M1 |
| 7.6 | Per-locale publish and schedule | `(document, locale)` state | 4.5 | M1 |
| 7.8 | Per-locale permissions | locale constraint | 4.8 | M1 |
| 7.10 | Multi-site model | `cms_site`, site-scoped tables | 4.6 | M1 |
| 7.11 | Site context in APIs | resolution order, key binding; hostname in plugin | 4.6, 5.6 | M2, M5 |
| 7.12 | Shared content across sites | `ContentScope.Global` | 4.6 | M1 |
| 7.13 | Per-site admin | site-scoped roles and settings (data side) | 4.8, 4.13 | M1 |

7.7 and 7.9 are admin-only. Should: 7.14 translation workflow data, 7.15 per-site media roots, 7.16 locale-specific slugs and URL rules, 7.17 XLIFF export and import, 7.18 cross-site copy.

### 7.7 Plugin system (category 8)

| Row | Feature | Backend part | Area | Milestone |
| --- | --- | --- | --- | --- |
| 8.1 | Plugin package model | server half contract | 4.13 | M3 |
| 8.2 | Manifest and compatibility | attribute, startup validation | 4.13 | M3 |
| 8.3 | Public SDK package | `FluentCMS.Sdk` | 4.1 | M1 |
| 8.4 | Lifecycle hooks | hook pipeline | 4.11 | M1 |
| 8.5 | Custom field types | `IFieldType` registration (storage, validation, OpenAPI); editor is admin | 4.2 | M1 |
| 8.6 | Custom API endpoints | `IPluginAppBuilder`, GraphQL contributor | 4.13, 5.3 | M3, M4 |
| 8.7 | Settings pages | typed settings model and endpoints (page is admin) | 4.13 | M3 |
| 8.10 | Background jobs | `AddJob<T>()` | 4.12, 4.13 | M3 |
| 8.11 | Provider slots | SDK interfaces, defaults | 4.1, 4.13 | M1, M3 |
| 8.12 | MCP tools from plugins | `IMcpToolProvider` | 4.1, 5.4 | M4 |
| 8.13 | Plugin-owned migrations | plugin stream | 4.3, 4.13 | M3 |
| 8.14 | Plugin permissions | `IPermissionProvider` | 4.8, 4.13 | M3 |
| 8.15 | Enable and disable per site | `cms_plugin_site`, gating | 4.13 | M3 |
| 8.16 | Event bus | bus, outbox | 4.11 | M1 |
| 8.17 | Plugin template and test harness | server template, `FluentCMS.Testing` | 6.2, 6.3 | M1, M6 |

8.8 and 8.9 are admin-only. Should: 8.18 plugin diagnostics data (the list endpoint is Must), 8.19 plugin CLI verbs, 8.20 content type packs.

### 7.8 AI and agents (category 9)

| Row | Feature | Backend part | Area | Milestone |
| --- | --- | --- | --- | --- |
| 9.1 | In-process MCP server | `/mcp` | 5.4 | M4 |
| 9.2 | OAuth 2.1 for MCP clients | resource metadata, DCR | 5.4, 4.7 | M4 |
| 9.3 | Agent as principal | evaluator | 5.4 | M4 |
| 9.4 | Content tools | generated tools | 5.4 | M4 |
| 9.5 | Publish as separate tool | tools and permission | 5.4 | M4 |
| 9.6 | Media tools | tools | 5.4 | M4 |
| 9.7 | Schema resources | resources | 5.4 | M4 |
| 9.8 | Safety on destructive tools | dry run, confirmation token | 5.4 | M4 |
| 9.9 | Origin tagging | context | 2.2, 5.4 | M1, M4 |
| 9.10 | Per-type agent instructions | `cms_mcp_instruction` | 5.4 | M4 |
| 9.11 | Prompts | providers, stored prompts | 5.4 | M4 |
| 9.12 | Rate and size limits | per-client limits | 5.4 | M4 |
| 9.13 | MCP client management | endpoints (UI is admin) | 5.4 | M4 |
| 9.14 | AI assist in the editor | action endpoints (UI is admin) | 5.5 | M5 |
| 9.15 | Provider-agnostic AI layer | Microsoft.Extensions.AI, usage log | 5.5 | M5 |

Should: 9.16 semantic search, 9.17 agent-context resource, 9.18 content provenance, 9.19 workflow stage for agent drafts, 9.20 alt text on upload, 9.21 MCP tools from plugins (the contract is Must through 8.12).

### 7.9 Infrastructure (category 10)

| Row | Feature | Backend part | Area | Milestone |
| --- | --- | --- | --- | --- |
| 10.1 | EF Core providers | three packages | 4.3 | M1 |
| 10.2 | Reference host image | `FluentCMS.Host`, Dockerfile | 4.16 | M2 |
| 10.3 | Configuration | options, validation | 4.15 | M2 |
| 10.4 | Migrations at startup or by CLI | runner, `migrate` | 4.3, 6.1 | M1, M6 |
| 10.5 | Health and readiness | checks | 4.15 | M2 |
| 10.6 | Observability | instrumentation | 4.15 | M2 |
| 10.7 | Multi-instance safety | locks, key persistence | 4.15 | M2 |
| 10.8 | Response caching | output cache, tags, purge event | 4.10 | M2 |
| 10.9 | Content export and import | transfer | 4.14 | M6 |
| 10.10 | Schema snapshot and diff | snapshot, CLI | 4.2, 6.1 | M1, M6 |
| 10.11 | Backups guidance | docs, `backup` | 4.15, 6.1 | M6 |
| 10.12 | Rate and request limits | policies, caps | 4.10 | M2 |
| 10.13 | Reverse proxy and TLS | options | 4.15 | M2 |
| 10.14 | Startup diagnostics | report, endpoint | 4.15 | M2 |
| 10.15 | Supported platforms | CI matrix, docs | 3.2 | M0 |

Should: 10.16 environment promotion, 10.17 Redis provider pack, 10.18 read replicas, 10.19 metrics dashboards, 10.20 maintenance jobs (several already exist as built-in jobs), 10.21 Helm and compose, 10.22 secret rotation.

### 7.10 Developer experience (category 11)

| Row | Feature | Backend part | Area | Milestone |
| --- | --- | --- | --- | --- |
| 11.1 | Project templates | `fluentcms`, `fluentcms-plugin` server half | 6.2 | M6 |
| 11.2 | CLI | all commands | 6.1 | M6 |
| 11.3 | Type generation | `generate`, source generator | 6.1, 6.4 | M6 |
| 11.4 | Fast local loop | SQLite default, schema sync | 4.3 | M1 |
| 11.5 | Seed data and sample project | `Seed` API, sample pack, `samples/Blog` | 4.16, 6.3 | M2 |
| 11.6 | Test host | `FluentCMS.Testing` | 6.3 | M1 |
| 11.12 | Upgrade tooling | `doctor`, obsoletion analyzers | 6.1, 6.4 | M6 |
| 11.13 | Analyzers | `FluentCMS.Analyzers` | 6.4 | M6 |
| 11.14 | Error messages with links | `FCMS` codes, problem `type` | 4.10 | M2 |

11.7 to 11.11 are docs, agent instructions, starters and policy; not backend. Should: 11.15 playground, 11.17 editor snippets, 11.18 deployment recipes, 11.19 migration guides.

## 8. Milestones

Follows the sequencing note in the decisions log: SDK and core first, because every plugin depends on them.

| Milestone | Goal | Exit criteria |
| --- | --- | --- |
| M0 Foundation | Solution, build and CI exist; nothing runs yet. | `dotnet build` and `dotnet test` green in CI on Linux and Windows; packages pack; the test matrix job runs on SQLite (PostgreSQL and SQL Server deferred, Q19). |
| M1 SDK and core engine | A host can define types in C#, store and query entries in-process with permissions, versions, publishing, scheduling, events and jobs on SQLite (other providers deferred, Q19). No HTTP yet. | The sample `Blog` types round-trip through `IContentService<T>` in the test host; the storage spike's decision is recorded; migrations generate for SQLite. |
| M2 HTTP and identity | Delivery and Management APIs, authorization server with hosted pages, admin users, roles, keys, 2FA, SSO federation, activity log endpoint, OpenAPI, caching, limits, health, observability, startup diagnostics, reference host image. | An editor can be invited, sign in through the hosted pages, obtain a token with PKCE, create and publish content through the Management API, and read it through the Delivery API with a key, all from the Docker image on SQLite and on PostgreSQL. |
| M3 Plugin host, media, webhooks | Plugin contract proven by two first-party plugins. | A plugin project from the server template boots in the test host with its own tables, permissions, settings and job; images upload and transform through signed URLs on local, S3 and Azure storage; a publish triggers a signed webhook with retries. |
| M4 Agents and GraphQL | MCP server and GraphQL plugin. | Claude or an IDE client connects to `/mcp` through OAuth, lists tools filtered by role, drafts an entry and cannot publish without the permission; GraphQL queries and mutations mirror REST. |
| M5 Multi-site, members, AI assist | Hostname-resolved sites, member authentication with external providers, AI actions. | Two sites on different hostnames share types and media with separate content and roles; a member registers, verifies and signs in with Google; an AI action returns a suggestion and logs usage. |
| M6 Tooling and transfer | CLI, templates, source generator, analyzers, export and import, backups. | `dotnet new fluentcms`, `fluentcms migrations add`, `fluentcms migrate`, `fluentcms generate typescript`, `fluentcms export` and `import --dry-run` work end to end on a fresh machine. |

M1 and M2 are the critical path. M3 can start once the SDK is stable (end of M1). M6 pieces that unblock earlier milestones (`FluentCMS.Testing`, the content migration command) are pulled into M1.

## 9. Proposed issues

Grouped by milestone. Each becomes one GitHub issue with the Feature template (problem, proposal, acceptance), labelled `type:feature`, an `area:*` label and the milestone. One tracking issue per milestone holds the checklist. Sizes: S under two days, M a week, L more than a week and probably split further during implementation.

### 9.1 M0 Foundation

Tracking issue: [#2707](https://github.com/fluentcms/FluentCMS/issues/2707).

| Id | Issue | Spec | Size |
| --- | --- | --- | --- |
| B-01 [#2676](https://github.com/fluentcms/FluentCMS/issues/2676) | Solution skeleton: `FluentCMS.slnx`, `Directory.Build.props`, `Directory.Packages.props`, project stubs for every package in 2.4 | 10.15, 59 | S |
| B-02 [#2677](https://github.com/fluentcms/FluentCMS/issues/2677) | CI: build, test, pack on Linux and Windows; provider matrix with Testcontainers; markdown lint kept | 10.15 | M |
| B-03 [#2678](https://github.com/fluentcms/FluentCMS/issues/2678) | Package metadata, SourceLink, symbol packages, public API analyzers and package validation on `FluentCMS.Sdk` | 14, 61 | S |
| B-04 [#2679](https://github.com/fluentcms/FluentCMS/issues/2679) | Architecture test project with the layering rules of 3.4 | 4.12 | S |
| B-05 [#2680](https://github.com/fluentcms/FluentCMS/issues/2680) | Contributing guide update: issue-first flow, branch naming, labels, milestone process, ADR folder (Q12) | | S |
| B-06 [#2712](https://github.com/fluentcms/FluentCMS/issues/2712) | Record the deferred database provider testing in this document (Q19) | | S |

### 9.2 M1 SDK and core engine

Tracking issue: [#2708](https://github.com/fluentcms/FluentCMS/issues/2708).

| Id | Issue | Spec | Size |
| --- | --- | --- | --- |
| B-10 [#2681](https://github.com/fluentcms/FluentCMS/issues/2681) | Storage spike: property bags versus generated rows, JSON filtering on all three providers, partial unique indexes; outcome recorded as ADR | 19, 60, Q5, Q7 | M |
| B-11 [#2682](https://github.com/fluentcms/FluentCMS/issues/2682) | SDK: `OperationContext`, `Principal`, `Origin`, `SiteId`, `ContentItem`, `RichText`, `BlockList`, `Ref<T>`, `RefList<T>`, `MediaRef` | 4.1 | M |
| B-12 [#2683](https://github.com/fluentcms/FluentCMS/issues/2683) | Schema attributes and fluent builder producing `SchemaModel`; startup validation with `FCMS12xx` codes | 1.1, 1.2, 1.5, 1.6, 1.10, 1.11 | L |
| B-13 [#2684](https://github.com/fluentcms/FluentCMS/issues/2684) | Field type registry and the core scalar, select, json, relation, component and block field types | 1.3, 1.4, 8.5 | L |
| B-14 [#2685](https://github.com/fluentcms/FluentCMS/issues/2685) | Validation attributes, custom validators, `Condition` fluent API and serialisation | 1.7, 1.8 | M |
| B-15 [#2686](https://github.com/fluentcms/FluentCMS/issues/2686) | Rich text: ProseMirror document model, HTML and Markdown renderers, reference extraction | 1.9 | M |
| B-16 [#2687](https://github.com/fluentcms/FluentCMS/issues/2687) | Schema snapshot, hash and `cms_schema_history` | 1.19, 10.10 | S |
| B-17 [#2688](https://github.com/fluentcms/FluentCMS/issues/2688) | `CmsDbContext` model builder from `SchemaModel`: `cms_document`, content tables, `_i18n`, `cms_reference`, `cms_version`, indexes | 1.12, 7.2, 7.3 | L |
| B-18 [#2689](https://github.com/fluentcms/FluentCMS/issues/2689) | Data provider packages: PostgreSQL, SQLite, SQL Server with `IJsonSqlDialect` and job-claim dialect | 10.1 | L |
| B-19 [#2690](https://github.com/fluentcms/FluentCMS/issues/2690) | Migration streams and runner: system, plugin and content contexts, history tables, ordering, `MigrateOnStartup`, `SchemaSync=Auto`, startup validation | 1.12, 10.4, 11.4 | L |
| B-20 [#2691](https://github.com/fluentcms/FluentCMS/issues/2691) | Sites and locales in core: tables, default site seed, resolvers, fallback chain, BCP 47 validation, global scope | 7.1, 7.4, 7.5, 7.10, 7.12 | M |
| B-21 [#2692](https://github.com/fluentcms/FluentCMS/issues/2692) | `ContentQuery` AST, LINQ compiler over property bags, relation path joins, JSON dialect filters, projection, population with depth cap | 2.4, 2.5, 2.6, 2.19 | L |
| B-22 [#2693](https://github.com/fluentcms/FluentCMS/issues/2693) | `IContentService` and `IContentService<T>`: get, query, create, update with concurrency, delete, restore, count; typed mapper | 2.3, 2.15 | L |
| B-23 [#2694](https://github.com/fluentcms/FluentCMS/issues/2694) | Validation and hook pipelines, `HookRejectedException` | 1.7, 8.4 | M |
| B-24 [#2695](https://github.com/fluentcms/FluentCMS/issues/2695) | Publishing state machine: states, publish, unpublish, discard, versions, pruning, diff, restore, reference integrity | 6.1 to 6.4, 6.7 | L |
| B-25 [#2696](https://github.com/fluentcms/FluentCMS/issues/2696) | Trash and purge with retention job | 6.9 | S |
| B-26 [#2697](https://github.com/fluentcms/FluentCMS/issues/2697) | Event bus with after-commit dispatch and `cms_outbox` | 8.16, 6.10 | M |
| B-27 [#2698](https://github.com/fluentcms/FluentCMS/issues/2698) | Job runner: tables, scheduler, workers, leases, retries, dead letter, recurring jobs, `IJobRunner` | 6.6, 8.10 | L |
| B-28 [#2699](https://github.com/fluentcms/FluentCMS/issues/2699) | Scheduling: `cms_schedule`, publish and unpublish jobs, cancel and reschedule | 6.5, 7.6 | M |
| B-29 [#2700](https://github.com/fluentcms/FluentCMS/issues/2700) | Permission catalogue, roles with site scope, built-in roles, `cms_role_permission` constraints | 4.3, 4.4, 4.7, 4.8 | M |
| B-30 [#2701](https://github.com/fluentcms/FluentCMS/issues/2701) | `IAuthorizationEvaluator`: effective permissions, field filtering, row rule registry and query append, caching | 4.5, 4.6, 4.12 | L |
| B-31 [#2702](https://github.com/fluentcms/FluentCMS/issues/2702) | Activity log writer, event subscribers, retention job | 4.14 | M |
| B-32 [#2703](https://github.com/fluentcms/FluentCMS/issues/2703) | Bulk operations in the content service with per-item results and filter cap | 2.14 | M |
| B-33 [#2704](https://github.com/fluentcms/FluentCMS/issues/2704) | Typed settings model and `cms_setting` persistence with change events | 8.7 | S |
| B-34 [#2705](https://github.com/fluentcms/FluentCMS/issues/2705) | `FluentCMS.Testing`: test host, fixtures, SQLite modes | 11.6, 8.17 | M |
| B-35 [#2706](https://github.com/fluentcms/FluentCMS/issues/2706) | Sample `Blog` types and seed API used by tests | 11.5 | S |

### 9.3 M2 HTTP and identity

| Id | Issue | Spec | Size |
| --- | --- | --- | --- |
| B-40 | `AddFluentCms()` and `MapFluentCms()`, options binding and `ValidateOnStart`, Production safety refusals | 10.3, 4.13 | M |
| B-41 | ASP.NET Core Identity stores for admin users and roles; invitations; password policy; lockout; deactivate | 4.2 | M |
| B-42 | OpenIddict server: endpoints, flows, scopes, audiences, signing key persistence, session list and revoke | 4.1, 2.13 | L |
| B-43 | Hosted pages: login, logout, consent, 2FA, recovery codes, forgot and reset, invitation, external callback, error; branding settings | 4.1 | L |
| B-44 | Two-factor TOTP with per-role enforcement | 4.16 | M |
| B-45 | Admin SSO federation: generic OIDC with presets, auto-provisioning, group mapping, local-login disable | 4.15 | M |
| B-46 | API keys: table, classes, format, hashing, authentication handler, per-key rate limit | 4.9, 2.12 | M |
| B-47 | Delivery API endpoints with bracket filter parser, status and locale handling, `by/{field}` lookup | 2.1, 2.4 | L |
| B-48 | Management API: content group (CRUD, publishing, versions, schedules, bulk, trash) | 2.2 | L |
| B-49 | Management API: schema introspection with permission filtering | 1.13, 4.17 | M |
| B-50 | Management API: users, invitations, roles, permissions catalogue, API keys, sites, locales, settings, jobs, activity, plugins, system, me | 2.2 | L |
| B-51 | Problem details error model and `FCMS` code registry | 2.8, 11.14 | S |
| B-52 | OpenAPI 3.1 document transformers and Scalar UI | 2.9 | M |
| B-53 | Response caching: headers, output cache tags, eviction on events, `CacheInvalidatedEvent` | 2.20, 10.8 | M |
| B-54 | Rate limiting policies, CORS allow-lists, security headers, request limits | 2.17, 2.18, 10.12 | M |
| B-55 | Health checks, OpenTelemetry instrumentation, structured log event ids | 10.5, 10.6 | M |
| B-56 | Multi-instance: Data Protection to database, `IDistributedLock` with `cms_lock` default | 10.7 | S |
| B-57 | Startup report and diagnostics endpoint; reverse proxy options | 10.13, 10.14 | S |
| B-58 | Reference host: project, schema file loading (Q2), sample pack, Dockerfile, compose, probes | 10.2 | M |

### 9.4 M3 Plugin host, media, webhooks

| Id | Issue | Spec | Size |
| --- | --- | --- | --- |
| B-60 | Plugin host: `IPlugin`, manifest attribute, explicit and discovered registration, startup validation, ordering | 8.1, 8.2 | M |
| B-61 | Plugin contributions: endpoints under the CMS group, permissions, settings, model contributor, migrations stream, jobs, event handlers | 8.6, 8.13, 8.14 | L |
| B-62 | Per-site enable and disable with gating of endpoints, hooks, jobs and tools | 8.15 | M |
| B-63 | Provider slot defaults: `IEmailSender` (Q9), `IDistributedLock`, memory cache; slot registration API | 8.11 | S |
| B-64 | Media plugin: tables, folders, `IFileStorage` with local provider, upload with sniffing and limits, metadata, management endpoints | 5.1 to 5.4 | L |
| B-65 | Media transforms: `IImageProcessor`, pipeline, variants, presets, focal point, signed URLs, `f=auto` | 5.5 to 5.8 | L |
| B-66 | Media Delivery API, usage tracking, replace, private assets with signed download URLs | 5.9 to 5.11 | M |
| B-67 | `media` field type with `IMediaResolver` for population, rich text and blocks | 1.4, 5.12 | M |
| B-68 | S3-compatible and Azure Blob storage providers | 5.1 | M |
| B-69 | Webhooks plugin: tables, outbox subscription, signing, delivery job with retries, delivery log, redeliver, ping, endpoints | 2.20, 6.10, 8.16 | L |

### 9.5 M4 Agents and GraphQL

| Id | Issue | Spec | Size |
| --- | --- | --- | --- |
| B-70 | MCP plugin: Streamable HTTP server, per-site enable, bearer and key auth, protected resource metadata | 9.1, 9.3 | M |
| B-71 | Dynamic client registration over OpenIddict and MCP client management endpoints (Q11) | 9.2, 9.13 | M |
| B-72 | Generated content tools, `query` meta-tool, publish tools, media tools, permission-filtered tool list and input schemas | 9.4 to 9.6 | L |
| B-73 | Schema resources, instructions, prompts providers and stored prompts | 9.7, 9.10, 9.11 | M |
| B-74 | Safety: dry run, confirmation tokens, bulk caps, per-client limits, truncation and paging | 9.8, 9.12 | M |
| B-75 | GraphQL plugin: schema generation, filters, pagination, mutations, limits, introspection policy, `IGraphQLSchemaContributor` | 2.16 | L |

### 9.6 M5 Multi-site, members, AI assist

| Id | Issue | Spec | Size |
| --- | --- | --- | --- |
| B-80 | Multi-site plugin: `cms_site_host`, hostname middleware, site creation endpoints | 7.11, 7.13 | M |
| B-81 | Member pool: tables, `UserManager<CmsMember>`, groups, register, verify, reset, `me`, member audience tokens, direct login grant (Q4) | 4.10 | L |
| B-82 | Member external providers: Google, Microsoft, generic OIDC, GitHub and Apple (Q10) | 4.11 | M |
| B-83 | AI assist plugin: provider configuration, actions endpoint, SSE streaming, usage log, permissions | 9.14, 9.15 | L |

### 9.7 M6 Tooling and transfer

| Id | Issue | Spec | Size |
| --- | --- | --- | --- |
| B-90 | CLI skeleton with `--project` and `--url` modes; `migrate`, `migrations *`, `schema *`, `doctor` | 11.2, 10.4, 10.10 | L |
| B-91 | CLI: `apikey`, `user`, `jobs`, `mcp` proxy, `backup` | 11.2 | M |
| B-92 | Export package writer and import with dry run; transfer endpoints; CLI `export` and `import` | 10.9 | L |
| B-93 | `generate csharp`, `generate typescript`, `generate openapi` (Q15) | 11.3 | L |
| B-94 | Templates package: `fluentcms` host and `fluentcms-plugin` server half | 11.1 | M |
| B-95 | Source generator: typed filter builders and mappers | 11.3 | L |
| B-96 | Analyzers: schema attribute rules and SDK obsoletion | 11.13, 11.12 | M |

77 issues for the Must scope, created milestone by milestone (Q17), plus [#2711](https://github.com/fluentcms/FluentCMS/issues/2711) for the deferred PostgreSQL and SQL Server validation (Q19), which has no milestone until the code is complete. Should items get issues when their milestone is planned, not now.

## 10. Open questions and suggestions

All eighteen questions were decided on 1 Nov 2026, and Q19 was added the same day after approval. The table keeps each question and its decision; where the decision differs from the recommendation that was made, it says so.

### 10.1 Decisions on the open questions

| Id | Question | Decision (1 Nov 2026) |
| --- | --- | --- |
| Q1 | Package granularity: one `FluentCMS.Core` holding engine, REST and identity, or split `FluentCMS.Core`, `FluentCMS.Api`, `FluentCMS.Identity`? | One `FluentCMS.Core` plus data providers. The existing `FluentCMS.Identity` 0.0.5 ID is unlisted and deprecated per decision 61. |
| Q2 | How does a Docker-only user define content types? The image cannot load a DLL (decision 11) and types are code-first. | The fluent builder loads a `schema.json` mounted into the image. Schema in a file under version control is not a UI builder (1.24). Code-first stays primary; the sample pack stays for evaluation. |
| Q3 | Argon2id (4.2) versus Identity's PBKDF2 default; 4.32 says Identity primitives only. | PBKDF2 behind the replaceable hasher. Argon2id as an optional package later if asked. Row 4.2 is amended. |
| Q4 | Member login: OAuth 2.1 has no password grant. Hosted pages only, or also a first-party direct login? | Both: hosted pages by default, and an opt-in `member_password` custom grant for first-party apps, documented as the less secure option. |
| Q5 | EF model from property bags (runtime) or from source-generated row classes (compile time)? | Property bags; spike B-10 validates them on all three providers before the content tables are built. |
| Q6a | Core versus multi-site plugin boundary for site and locale management. | Sites and locales CRUD in core; hostnames, cross-site copy and per-site storage in the plugin. |
| Q6b | Single components flattened to columns, or JSON for all components? | Flatten single components to prefixed columns; JSON for repeatable ones. |
| Q7 | Block-field filtering depth across three providers (2.4 makes it Must). | Block-type presence and scalar paths one level deep, identical on all providers. Deeper paths deferred, PostgreSQL-only when they come. |
| Q8 | ImageSharp is under the Six Labors Split License (free for open source and small companies, paid above a revenue threshold). Keep it as the default `IImageProcessor`? | **ImageSharp stays the default, as the specification says.** The recommendation to evaluate SkiaSharp or NetVips was not taken. The documentation carries a licence note; the slot stays replaceable. |
| Q9 | New dependencies: OpenIddict, HotChocolate, ModelContextProtocol, Microsoft.Extensions.AI and providers, Scalar.AspNetCore, Cronos, MailKit, AWSSDK.S3, Azure.Storage.Blobs, Testcontainers, ImageSharp. | Approved in principle; each is named again in the issue that introduces it. Email ships as SMTP through MailKit plus a log-only sender for Development. |
| Q10 | GitHub and Apple member login need the community `AspNet.Security.OAuth.Providers` package. | Accepted (Apache 2.0, aspnet-contrib). |
| Q11 | Dynamic client registration policy for MCP clients. | Approval required by default; open-with-rate-limit as a setting. |
| Q12 | Record implementation-time architecture decisions as ADRs? | Yes, short ADRs in `docs/decisions/`, only for choices not already in the decisions log. |
| Q13 | Test assertion library. | Shouldly. |
| Q14 | Response shape: system fields flat with reserved names, or under a `sys` object? | Flat with reserved names. |
| Q15 | Client generation from OpenAPI. | Own TypeScript emitter; the C# generator (Kiota or own) decided in B-93. |
| Q16 | The uploaded `research/` folder names v2 and a possible paid tier; the public `main` must not mention v2. | Commit `specification/` and this document only. `research/` stays out of the repository and out of the planning folder; this document no longer refers to it. |
| Q17 | Issue granularity. | The 77 issues of section 9 under seven milestone tracking issues, created milestone by milestone, M0 and M1 first. |
| Q18 | Is the tooling part of this backend batch? | Yes. `FluentCMS.Testing` and the `migrations add` command sit in M1 because the core tests need them; the rest is M6 in this batch. |
| Q19 | Database providers during implementation: SQLite only locally, or PostgreSQL and SQL Server in CI from M0? | **SQLite only, locally and in CI, until the backend code is complete.** PostgreSQL and SQL Server validation and the completion of their provider packages are deferred to [#2711](https://github.com/fluentcms/FluentCMS/issues/2711). The recommendation to run the provider matrix in CI from M0 was not taken. Risk accepted: JSON filtering, job claiming, partial unique indexes and migration generation stay unverified on the primary production provider until then; issues #2677, #2681 and #2689 carry scope notes. |

### 10.2 Suggestions

| Id | Suggestion | Why |
| --- | --- | --- |
| S1 | Make `OperationContext` and `IAuthorizationEvaluator` the first two SDK types written and never let HTTP, GraphQL or MCP bypass the service (architecture test). | Every differentiator in the research (origin tagging, agent safety, one permission model) depends on it. |
| S2 | Run the storage spike (B-10) before any content-table code; time-box it to one week and record the result as an ADR. | Decision 19 says SQL Server JSON must be verified; the property-bag choice shapes everything in M1. |
| S3 | One polymorphic `cms_reference` table instead of a `_rels` table per type. | Media usage, reference integrity and delete warnings become one indexed query; polymorphic relations (Should) cost nothing later. |
| S4 | A type-independent `cms_document` state table. | Cross-type lists, trash, scheduling views and the MCP `query` tool need it; it keeps the state machine out of generated tables. |
| S5 | Outbox for events that feed webhooks, activity and cache invalidation. | Signed, retried webhooks are a stated differentiator; without an outbox a crash after commit loses the event. |
| S6 | Ship `FluentCMS.Testing` in M1, not M6. | The core's own integration tests are the first consumer; plugin authors inherit a proven harness. |
| S7 | Public API analyzers and package validation on `FluentCMS.Sdk` from day one. | Decision 14 promises semver; the build should enforce it. |
| S8 | `TimeProvider` everywhere, no `DateTimeOffset.UtcNow`. | Scheduling, leases, retention and token expiry are all testable without waiting. |
| S9 | Treat the hosted authorization pages as a backend deliverable with their own issue (B-43). | They are easy to forget because they are UI, and nothing signs in without them. |
| S10 | Reserve `/admin`, `/media`, `/mcp`, `/graphql`, `/connect`, `/health`, `/openapi`, `/scalar` in the route map now. | Hosts mount FluentCMS next to their own routes; a published list avoids collisions later. |
| S11 | Add a `Seed` API to the SDK in M1 and use it for the sample pack, tests and the template. | 11.5 is Must and three consumers need the same thing. |
| S12 | The research folder stays a private planning input outside the repository (Q16). | The restart announcement avoided mentioning v2, and the research text does. |

## Appendix A: worked example

The `Article` type from 4.2 on PostgreSQL, prefix `cms_`.

```sql
-- shared, non-localized fields, one row per document and state
CREATE TABLE cms_article (
    id              uuid PRIMARY KEY,
    document_id     uuid NOT NULL,
    site_id         uuid NOT NULL,
    state           smallint NOT NULL,           -- 0 draft, 1 published
    category        varchar(64),
    published_on    timestamptz,
    sections        jsonb NOT NULL DEFAULT '[]',
    seo_meta_title  varchar(70),
    seo_meta_description varchar(160),
    UNIQUE (document_id, state)
);

-- localized fields, one row per document, locale and state
CREATE TABLE cms_article_i18n (
    id              uuid PRIMARY KEY,
    document_id     uuid NOT NULL,
    site_id         uuid NOT NULL,
    locale          varchar(16) NOT NULL,
    state           smallint NOT NULL,
    title           varchar(200) NOT NULL,
    slug            varchar(200) NOT NULL,
    body            jsonb NOT NULL,
    video_url       varchar(2048),
    UNIQUE (document_id, locale, state)
);
CREATE UNIQUE INDEX ux_cms_article_i18n_slug
    ON cms_article_i18n (site_id, locale, slug) WHERE state = 1;
CREATE INDEX ix_cms_article_category ON cms_article (site_id, state, category);

-- author, tags and hero live in cms_reference:
-- (source_type='article', source_document_id, state, locale NULL, field='author', position 0,
--  target_type='author', target_id)
```

A Delivery request and response:

```http
GET /api/delivery/v1/article?filter[category][eq]=news&sort=-publishedOn&pageSize=2&populate=author&locale=fr-CA
Authorization: ApiKey fcms_dk_...
```

```json
{
  "items": [
    {
      "id": "0192f2a4-7b1c-7d2e-9f10-2a3b4c5d6e7f",
      "status": "published",
      "locale": "fr-CA",
      "version": 7,
      "publishedAt": "2026-11-01T10:41:00Z",
      "updatedAt": "2026-11-01T10:40:00Z",
      "createdAt": "2026-10-30T08:00:00Z",
      "title": "Bonjour",
      "slug": "bonjour",
      "category": "news",
      "publishedOn": "2026-11-01T00:00:00Z",
      "author": { "id": "0192f2a4-1111-7000-8000-000000000001", "type": "author", "name": "Amir" },
      "tags": [{ "id": "0192f2a4-2222-7000-8000-000000000002", "type": "tag" }],
      "hero": { "id": "0192f2a4-3333-7000-8000-000000000003", "type": "media" },
      "sections": [{ "type": "hero", "heading": "Bienvenue", "image": { "id": "0192f2a4-3333-7000-8000-000000000003", "type": "media" } }],
      "seo": { "metaTitle": "Bonjour", "metaDescription": null },
      "fallbacks": { "body": "fr" }
    }
  ],
  "total": 41,
  "page": 1,
  "pageSize": 2,
  "nextCursor": "eyJwdWJsaXNoZWRPbiI6Ii4uLiIsImlkIjoiLi4uIn0"
}
```

## Appendix B: route map

| Prefix | Surface | Auth |
| --- | --- | --- |
| `/api/delivery/v1` | Delivery API | none (Public role), delivery key, preview key, bearer with `draft` |
| `/api/management/v1` | Management API | bearer, management key |
| `/api/members/v1` | member registration and account | none for register and reset, member bearer for `me` |
| `/connect/*`, `/.well-known/*` | OpenIddict and metadata | per endpoint |
| `/account/*` | hosted pages | cookie |
| `/media/{id}/{filename}` | asset bytes and transforms | signature, or folder permission for private |
| `/graphql` | GraphQL plugin | same as REST |
| `/mcp` | MCP plugin | bearer, API key |
| `/health/live`, `/health/ready` | probes | none |
| `/openapi/v1.json`, `/scalar` | document and UI | configurable |
| `/admin` | reserved for the Blazor admin | |

All prefixes are relative to the configured `PathBase`. The version segment allows Delivery and Management to evolve independently; schema changes are never API versions (2.32).

## Appendix C: system table catalogue

| Table | Area |
| --- | --- |
| `cms_site`, `cms_site_locale` | sites and locales |
| `cms_document`, `cms_reference`, `cms_version`, `cms_schedule`, `cms_schema_history` | content and publishing |
| `cms_user`, `cms_user_role`, `cms_user_claim`, `cms_user_login`, `cms_user_token`, `cms_invitation`, `cms_role`, `cms_role_permission` | identity and authorization |
| `cms_oidc_application`, `cms_oidc_authorization`, `cms_oidc_scope`, `cms_oidc_token`, `cms_signing_key`, `cms_data_protection_key` | authorization server |
| `cms_api_key` | API keys |
| `cms_member`, `cms_member_login`, `cms_member_token`, `cms_member_group`, `cms_member_group_member` | members |
| `cms_activity` | activity log |
| `cms_job`, `cms_job_attempt`, `cms_recurring_job`, `cms_lock`, `cms_outbox` | jobs and infrastructure |
| `cms_setting`, `cms_plugin_site` | settings and plugins |
| `cms_media_asset`, `cms_media_folder`, `cms_media_variant`, `cms_media_preset` | media plugin |
| `cms_webhook`, `cms_webhook_delivery` | webhooks plugin |
| `cms_mcp_instruction`, `cms_mcp_prompt` | MCP plugin |
| `cms_ai_usage` | AI assist plugin |
| `cms_site_host` | multi-site plugin |

Content type tables (`cms_{type}`, `cms_{type}_i18n`) are generated from the host's schema and owned by the host's migrations.
