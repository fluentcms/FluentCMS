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
|   IContentService<T>, IMediaService, IAuthorizationEvaluator,
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

Every answer is about the principal of the current `OperationContext`, so a check without one fails closed. The content service throws (4.4), and so do the permission checks of the Management services (users, roles, keys, sites, jobs, plugins, the activity log, schema introspection) and `IPermissionChecker`: an `InvalidOperationException` with `FCMS1408` that names `SystemOperation.Begin`. Throwing rather than denying makes code that forgot its operation fail where it runs, not as a 403. `SystemOperation.Begin`, from `Origin.Job`, `Origin.Cli` or `Origin.Seed`, is the one way to run as the system, which every check allows. The job runner and the seed runner open one for every job and seeder. Other host code that calls a Management service, such as a hosted service or a test fixture, opens its own.

### 2.4 Package map

Proposed NuGet packages, all `FluentCMS.*`, first release 1.0.0. A host adds two packages (`FluentCMS.Core` and one data provider) for a working API. Heavy optional dependencies stay out of core.

| Package | Contents | Depends on |
| --- | --- | --- |
| `FluentCMS.Sdk` | Interfaces, attributes, base classes, DTOs, events, permission names. No implementation. Semver-guaranteed, public API tracked by analyzers. | `Microsoft.Extensions.*` abstractions only |
| `FluentCMS.Plugins.Abstractions` | The plugin contracts that need EF Core or ASP.NET Core ([ADR 0003](../decisions/0003-plugin-contribution-contracts.md)): `IPluginModelContributor`, `PluginDbContext<TPlugin>`, `AddModel`, `AddMigrations`, `MapGroup`, `MapManagement` and `MapDelivery`. Semver-guaranteed like the SDK. | `FluentCMS.Sdk`, EF Core Relational, the ASP.NET Core framework |
| `FluentCMS.Core` | The engine and the HTTP surfaces: schema, storage model, content service, query model, versioning, publishing, localization, sites, identity and authorization server, RBAC, activity log, events, hooks, job runner, settings, plugin host, Delivery and Management APIs, OpenAPI, caching, health, diagnostics, export and import, rich text renderers. | `FluentCMS.Sdk`, `FluentCMS.Plugins.Abstractions`, EF Core, ASP.NET Core Identity, OpenIddict |
| `FluentCMS.Data.PostgreSql` | Npgsql wiring, provider-specific SQL (JSON paths, `SKIP LOCKED`, full-text search), the PostgreSQL system migration set. | `FluentCMS.Core`, Npgsql.EntityFrameworkCore.PostgreSQL |
| `FluentCMS.Data.Sqlite` | Same for SQLite (polling runner, JSON1, FTS5, auto-migrate on by default). | `FluentCMS.Core`, Microsoft.EntityFrameworkCore.Sqlite |
| `FluentCMS.Data.SqlServer` | Same for SQL Server (`READPAST`, `JSON_VALUE`, native `json` on 2025). | `FluentCMS.Core`, Microsoft.EntityFrameworkCore.SqlServer |
| `FluentCMS.Plugins.Media` | Assets, folders, local storage provider, transforms, signed URLs, media Delivery API, the `media` field type. | `FluentCMS.Sdk`, `FluentCMS.Plugins.Abstractions`, SixLabors.ImageSharp |
| `FluentCMS.Plugins.Media.S3`, `.AzureBlob` | Storage providers. | `FluentCMS.Plugins.Media`, AWSSDK.S3 or Azure.Storage.Blobs |
| `FluentCMS.Plugins.Webhooks` | Subscriptions, HMAC signing, delivery with retries, delivery log. | `FluentCMS.Sdk`, `FluentCMS.Plugins.Abstractions` |
| `FluentCMS.Plugins.GraphQL` | Schema generated from the content schema, queries and mutations, limits. | `FluentCMS.Sdk`, `FluentCMS.Plugins.Abstractions`, HotChocolate |
| `FluentCMS.Plugins.Mcp` | Streamable HTTP MCP server, tools, resources, prompts, client management, agent instructions. | `FluentCMS.Sdk`, `FluentCMS.Plugins.Abstractions`, ModelContextProtocol |
| `FluentCMS.Plugins.AiAssist` | Provider-agnostic AI actions and usage log. | `FluentCMS.Sdk`, `FluentCMS.Plugins.Abstractions`, Microsoft.Extensions.AI |
| `FluentCMS.Plugins.MultiSite` | Hostname resolution, per-site hosts, cross-site operations. | `FluentCMS.Sdk`, `FluentCMS.Plugins.Abstractions` |
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
|   +-- FluentCMS.Plugins.Abstractions/
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
| Content index and check | `uq_`, `ux_`, `ix_`, `gin_` or `ck_` with the table and column (4.3); over 63 characters, the first 54 and a hash | `ux_cms_article_i18n_slug` |
| Permission | dotted, lower case | `content.article.publish` |
| Route | the type name as declared, no pluralisation | `/api/delivery/v1/article` |
| Event | past tense, suffix `Event` | `EntryPublishedEvent` |
| Hook | `IBefore{Operation}Hook<T>` and `IAfter{Operation}Hook<T>` | `IBeforePublishHook<Article>` |
| Error code | `FCMS` plus four digits, grouped by area | `FCMS1001` |

**Name limits.** Names are held to PostgreSQL's identifier budget of 63 bytes, the smallest of the three providers, and to the 64-character columns that store type names and field paths. A type name or a field name has at most 64 characters, a content type's tables (`cms_{type}`, `cms_{type}_i18n`) and a column at most 63, and the path of a relation or media field (`seo.ogImage`) at most 64; schema validation refuses a longer one with `FCMS1218`, which names the type and the field. An index or check name over 63 characters is shortened instead (4.3, "Identifier budget"). This answers [#2873](https://github.com/fluentcms/FluentCMS/issues/2873) of the review of 7 Nov 2026 ([#2847](https://github.com/fluentcms/FluentCMS/issues/2847)).

### 3.4 Architecture tests

`FluentCMS.Architecture.Tests` asserts: `Api/*` never references `Storage/*` or `CmsDbContext`; plugins reference only `FluentCMS.Sdk` and `FluentCMS.Plugins.Abstractions` among FluentCMS packages, never Core; `FluentCMS.Sdk` references no implementation package; `FluentCMS.Plugins.Abstractions` references only the SDK, EF Core and the ASP.NET Core framework; nothing outside `Authorization/*` reads role permission tables.

## 4. Core areas

Each area lists the specification rows it implements (category.row, for example 1.3 is content modeling row 3), the design, key types, storage, endpoints and open points.

### 4.1 SDK contract

Rows 8.3, 8.11, 8.14, 8.16 and every attribute and interface named below.

`FluentCMS.Sdk` is the only package plugins and host code compile against. Namespaces:

| Namespace | Holds |
| --- | --- |
| `FluentCMS.Schema` | content type and field attributes, `ContentItem` base class, `RichText`, `BlockList`, `Ref<T>`, `RefList<T>`, `IFieldType`, fluent `ISchemaBuilder`, rich text contracts `IRichTextRenderer`, `IRichTextNodeRenderer`, `IRichTextNodeContext`, `RichTextRenderOptions`, `RichTextFormats` |
| `FluentCMS.Content` | `IContentService`, `IContentService<T>`, `ContentDocument`, `ContentQuery`, filter AST, `PagedResult<T>`, `OperationContext`, `Origin`, `Principal` |
| `FluentCMS.Publishing` | `EntryStatus`, `VersionInfo`, `VersionChange`, `PublishResult<T>`, `PublishWarning`, `PublishPolicyAttribute`, `ScheduleRequest` |
| `FluentCMS.Hooks` | hook interfaces and `HookContext` |
| `FluentCMS.Events` | `IEventBus`, `IEventHandler<T>`, event records |
| `FluentCMS.Jobs` | `IJob<TPayload>`, `IJobScheduler`, `JobOptions` |
| `FluentCMS.Authorization` | `Permission`, `IPermissionProvider`, `IPermissionChecker`, `PermissionDeniedException`, `IRowRule<T>` |
| `FluentCMS.Plugins` | `IPlugin`, `FluentCmsPluginAttribute`, `IPluginServiceBuilder`, `IPluginAppBuilder`, `PluginException`, `IPluginState` |
| `FluentCMS.Providers` | `IFileStorage`, `IEmailSender`, `ISearchIndex`, `IDistributedLock`, `IImageProcessor` |
| `FluentCMS.Media` | `MediaRef`, `MediaRefList`, `IMediaService`, `IMediaResolver` (contract only; implemented by the media plugin) |
| `FluentCMS.Sites`, `FluentCMS.Localization` | `SiteId`, `ISiteResolver`, `ILocaleResolver`, `FallbackChain` |
| `FluentCMS.Settings` | `SettingsAttribute`, `ISettings<T>` |
| `FluentCMS.Diagnostics` | `StartupReport`, `IInsecureDefaultSource`, `FluentCmsHealthCheckTags` |
| `FluentCMS.Agents` | `IMcpToolProvider`, `IMcpResourceProvider`, `IMcpPromptProvider` (contracts; implemented by the MCP plugin) |

The SDK has no dependency on EF Core or ASP.NET Core beyond `Microsoft.Extensions.*` abstractions, so plugin test projects stay light. The plugin contracts that need them, `IPluginModelContributor`, `PluginDbContext<TPlugin>` and the extension methods `AddModel`, `AddMigrations`, `MapGroup`, `MapManagement` and `MapDelivery`, are in `FluentCMS.Plugins.Abstractions`, in the same `FluentCMS.Plugins` namespace ([ADR 0003](../decisions/0003-plugin-contribution-contracts.md)).

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

    [Relation(Reverse = "articles")]
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

`ContentItem` carries the system fields (`Id`, `Status`, `Locale`, `Version`, `CreatedAt`, `CreatedBy`, `UpdatedAt`, `UpdatedBy`, `PublishedAt`) with setters the engine uses. Their names are reserved, and so is `fallbacks`, which a response adds (`ContentItem.ReservedFieldNames`, compared without regard to case); a field with a reserved name fails schema validation. A write ignores `fallbacks`. The system tables of Appendix C are reserved too: a collection or singleton may not take a name that maps to one of them (`job`, `user`, `site`, `member`, `site-host` and the rest, the table name without `cms_` and with hyphens for underscores), and fails with `FCMS1206`. Components and blocks have no table and may use these names.

**Field types.** Each field type is an `IFieldType` descriptor: CLR type(s), storage kind (`Column`, `Json`, `Reference`), whether it supports `Multiple` (`SupportsMultiple`, `false` unless the type says otherwise), default validation, OpenAPI schema fragment, supported filter operators, whether it can be sorted or indexed. Core types that support `Multiple` are select, relation and component; the media plugin's `media` does too. Core registers text, longText, richText, integer, decimal, boolean, date, dateTime, email, url, slug, select (single and multi), json, relation, component and blocks. The media plugin registers `media`, which is the proof of the custom field type extension point (8.5): core without the media plugin reports `FCMS1203: field type 'media' used by article.hero is not registered`.

**Components.** `[Component]` classes are reusable field groups. A single component flattens to prefixed columns (`seo_metaTitle`) so its fields are filterable and indexable; a repeatable component (`List<Address>`) is stored as JSON (Q6b).

**Blocks.** `[Block("name")]` classes form the union of a `BlockList` field. Blocks may contain components and nested block lists up to depth 3 (decision 20), validated at startup. Every block schema is emitted in OpenAPI as a `oneOf` with a `type` discriminator. A stored block also has an `id` beside `type`, unique within its top-level field at every depth ([ADR 0002](../decisions/0002-stable-block-ids.md)). The service gives a block that has none 12 random URL-safe characters before the entry is written, and returns the id on reads. A write may send any id of 1 to 64 of them (`A-Z`, `a-z`, `0-9`, `-`, `_`), so imported ids survive, and refuses one sent twice in a top-level field (`FCMS1407`). `type` and `id` are reserved block field names (`FCMS1206`). A block class keeps its id through `IContentService<T>` with a `string? Id` property and no field attribute. A block stored before ids existed gets one on the entry's next write. Repeated components have no ids.

**Rich text editor contract.** A `RichText` value is a ProseMirror JSON document whose node and mark names follow Tiptap, which is what the admin editor emits. Stored node types are `doc`, `paragraph`, `heading` (`level` 1 to 6), `bulletList`, `orderedList` (optional `start`), `listItem`, `blockquote`, `codeBlock` (optional `language`), `horizontalRule`, `hardBreak`, `text`, `media` and `inlineBlock`. Stored mark types are `bold`, `italic`, `underline`, `strike`, `code`, `link` (`href`, optional `title` and `target`) and `entryLink`. Validation reports any other name, including the stock ProseMirror basic schema names `image`, `bullet_list`, `ordered_list`, `code_block`, `hard_break`, `horizontal_rule` and `strong`, with the JSON path; nothing is dropped silently. FluentCMS adds three types to Tiptap:

- `media` node: `mediaId` (GUID), optional `alt` and `title`. It replaces `image`.
- `inlineBlock` node: `blockType` (the name of a registered block), `data` (an object) and `id` (as on a block in a list), the typed embed rendered by a plugin `IRichTextNodeRenderer`.
- `entryLink` mark: `entryId` (GUID) and `entryType` (content type name), a link to an entry that is resolved when rendered.

The `data` of an inline block has the rules of the same block in a block list. A write coerces and validates it against the block's fields, with errors at its path in the document (`body.content[0].content[1].attrs.data.label`), and refuses a hidden or read-only field or a key that is not a field from anyone but the system principal, except a read-only value sent back as the read showed it (**Read-only values sent back**, 4.5). A read leaves its hidden fields out, in JSON, HTML and Markdown, in versions and diffs, and in populated entries. A client that sends the document back keeps what it could not see: inline blocks pair up as the blocks of a list do (4.5), by `attrs.id` anywhere in the top-level field and otherwise over the whole document. An inline block of a type the schema does not know is left as written.

**Validation.** Declarative: `Required`, `Length`, `Range`, `RegularExpression`, `Unique`, enum membership. Custom: `IFieldValidator<TField>` and `IEntryValidator<T>` receive a `ValidationContext` (entry, sibling values, principal, operation, locale). Draft saves skip `Required` and `RequiredWhen`; publish runs everything.

**Conditions.** `VisibleWhen` and `RequiredWhen` reference a static `Condition` expressed with a small fluent API (`Field(...).Equals/In/IsEmpty/And/Or/Not`). Being data rather than arbitrary C#, a condition is serialised into the introspection document so the admin and agents can evaluate it, and the server enforces `RequiredWhen` at publish. Arbitrary C# predicates stay available through `IEntryValidator<T>` for server-only rules.

**Metadata.** `Display(Label, Help, Placeholder, Group, Tab, Width)`, `DefaultValue`, `ReadOnly`, `Hidden`, `Localizable`, `Indexed`, `Searchable`. All of it appears in introspection; none of it affects storage except `Indexed`, `Unique` and `Localizable`.

**Validation at startup.** Reserved names, nesting depth, relation targets exist, slug sources exist, `Localizable` only on localized types, field types registered, `Multiple` only on field types that support it, duplicate type names, duplicate reverse relation names, name lengths (4.3, "Identifier budget"). Errors carry `FCMS12xx` codes and stop the host.

**Introspection.** `GET /api/management/v1/schema` and `GET /api/management/v1/schema/types/{type}` return the model filtered by the caller's permissions (4.17 in the identity category): types, fields, field types, validation, conditions, metadata, relations, block unions, localized flags. The same DTOs feed the MCP schema resources and the CLI's schema diff.

**Snapshot and evolution.** `SchemaModel` serialises to a deterministic JSON snapshot; its SHA-256 is the schema hash. The hash is stored in `cms_schema_history` whenever content migrations are applied, so startup can report drift. Field renames are declared (`[FormerNames("subtitle")]`) so the migration generator emits a rename instead of drop and add.

**Snapshot as built.** The snapshot is the canonical form `SchemaJsonWriter` writes, which `schema.json` reads back: UTF-8 JSON with LF newlines, types ordered by name, fields in declaration order, settings by key, defaults left out, and neither the CLR class nor any runtime value in it. Two builds of one schema give the same bytes. The schema hash is the SHA-256 of those bytes as lowercase hex, and it also keys the EF model cache. `cms_schema_history (id, hash, snapshot, applied_at, fluentcms_version)` holds one row per schema the content tables were brought up to. `id` is a `bigint` identity, and the newest row is the one with the highest `id`, because the clocks of nodes disagree; `applied_at` is for people. `hash` is not unique because a rollback adds a row with an older hash. `snapshot` is plain text (not `jsonb`, which would reorder keys and break the hash) and `fluentcms_version` is the informational version without build metadata. The runner adds a row, unless the newest row has the same hash, after the content stream is applied and checked at startup, after `MigrateAsync` (which `fluentcms migrate` of M6 will call) if the database has every table and column of the schema, and after a `SchemaSync = Auto` sync that left nothing unapplied, at startup or through `SyncContentSchemaAsync`. The read of the newest row and the insert are one serializable transaction, and a node that loses a deadlock or a serialization failure reads again, so nodes that start together add one row between them. A change the database does not store, such as `Required`, is recorded when startup or `MigrateAsync` runs. When startup does not migrate (`FluentCms:Database:MigrateOnStartup = false`), it compares the code with the newest row and logs one warning that names the types and fields that differ, never their values: `type 'x' added`, `field 'article.title' changed (required)`, `field 'article.subtitle' renamed to 'summary'`; a component or a block is named as such (`block 'hero' removed`, `field 'component seo.title' changed (maxLength)`), because it may share a name with another kind. Collections and singletons share one namespace, components and blocks have one each. A new field that lists a field that has gone in its `formerNames` is a rename; without that it is a drop and an add. A missing history logs information; a stored snapshot of any other shape than the writer's logs the drift with "details are unavailable". Neither stops the host, and neither does a history that cannot be written or read (a deadlock that outlasts four attempts, a missing table): that logs a warning with the exception type and the host starts, since only a failure of the migrations themselves may stop it. The check reads one row, so it adds no noticeable time to boot. During a rolling deploy an older node boots against columns a newer node added and records its own hash, so alternating boots add rows and the drift warning fires on the older node; that is expected, and the history then shows the order the schemas were applied in.

Related decisions: Q2 (schema file for the reference host), Q6b (component storage).

### 4.3 Storage and migrations

Rows 1.12, 8.13, 10.1, 10.4, 10.10; decisions 8, 19, 39, 43, 48, 54, 55, 60.

**Model building.** The EF Core model is generated from `SchemaModel` using shared-type (property bag) entity types, so there is no CLR entity class per content type. This supports attribute-defined and builder-defined types alike, lets one typed `Article` project out of two tables (shared row plus locale row), and keeps the user's class a plain projection. Source-generated row classes were the alternative (Q5); the M1 storage spike (issue B-10) validated the choice on SQLite only (Q19), and [#2711](https://github.com/fluentcms/FluentCMS/issues/2711) validates it on PostgreSQL and SQL Server.

**Tables per content type.** For `article` (localized, prefix `cms_`):

| Table | Key | Holds |
| --- | --- | --- |
| `cms_document` | `(document_id, locale)` unique; `id` v7 PK | One row per document and locale, any type: `type`, `site_id`, `status`, `draft_version`, `published_version`, `published_at`, `first_published_at`, `deleted_at`, `deleted_by`, `created_*`, `updated_*`, `schedule_publish_at`, `schedule_unpublish_at`. `locale` is `''` for a non-localized document, never null, because a null `locale` defeats the unique key. The type-independent state table used by lists across types, trash, scheduling and the MCP `query` tool. |
| `cms_article` | `(document_id, state)` unique; `id` v7 PK | Non-localized fields, one row per document and state (0 draft, 1 published). `site_id` denormalised for partial unique indexes and future partitioning. |
| `cms_article_i18n` | `(document_id, locale, state)` unique; `id` v7 PK | Localized fields and `site_id`, one row per document, locale and state. `site_id` is repeated here because an index cannot span tables and a localized `[Unique]` field needs it beside the field. Absent for non-localized types, whose fields all live in `cms_article`. |
| `cms_reference` | `(source_type, source_document_id, state, locale, field, position)` unique; `id` v7 PK; index on `(target_type, target_id)` | Every relation and media reference in one polymorphic table. `locale` is `''` for non-localized values, never null, because a null `locale` defeats the unique key. Gives "what references X" for media usage tracking, reference integrity at publish and delete warnings in one query. Polymorphic relations (1.14, Should) need no new table. |
| `cms_version` | `(document_id, locale, number)` unique; `id` v7 PK | Full JSON snapshot per save plus `status_after`, `author_id`, `origin`, `client_id`, `message`, `created_at`. |

**Column mapping.** text `varchar(n)` (unbounded `TEXT` on SQLite, which does not enforce `n`, so the validation pipeline is the only length limit); longText `text`; richText, json, blocks, multi-select and repeatable components `jsonb` (PostgreSQL), `nvarchar(max)` or `json` (SQL Server), `text` with JSON1 and `CHECK (json_valid(col))` (SQLite); integer `bigint` (`long`, always, because widening a populated column later is a migration); decimal `numeric(p,s)` (text on SQLite, which EF compares and sorts numerically without a conversion); boolean; date `date`; dateTime `timestamptz` (PostgreSQL), `datetimeoffset` written in UTC (SQL Server), UTC ticks in an `integer` column (SQLite, through a provider convention that also covers system columns such as `schedule_publish_at`, so that scheduling and `sort=-publishedOn` can compare and order; SQLite supports only equality on `DateTimeOffset`, and the conversion drops the offset); email, url, slug `varchar`; single select the enum name as `varchar`; relations and media as `cms_reference` rows only, no denormalised id column.

**Nullability.** The storage model, the content service and the validation pipeline share these rules. A draft may be incomplete, so a column is `NOT NULL` only where there is an honest placeholder:

- `text` and `longText` are `NOT NULL` when `[Required]`; `''` stands in for a missing draft value.
- `slug` is `NOT NULL` always, because it is derived.
- `richText` is `NOT NULL` always; the empty document stands in for a missing value. There is no store default.
- Lists (`blocks`, multi `select`, repeatable components and any other field of a type that supports `Multiple` and stores JSON) are `NOT NULL DEFAULT '[]'`.
- Everything else is nullable even when `[Required]`: `email`, `url`, single `select`, `integer`, `decimal`, `boolean`, `date`, `dateTime` and `json`. A draft has no honest placeholder for them, and format checks would reject `''`.
- A column flattened from a single component is nullable even when its field is `[Required]`, `slug` or `richText`, because the whole component may be absent. A list inside a component keeps `NOT NULL DEFAULT '[]'`.
- `site_id` is `NOT NULL` except on `ContentScope.Global` types, where it is null. `locale` is always `NOT NULL`.

**Default lengths.** When a field declares no maximum length, `slug` is 200, `url` 2048, `email` 254 and a single `select` 64, or the length of its longest option if that is longer. `text` and `longText` without a limit stay unbounded. The column is sized with that length, and the validation pipeline refuses a longer value with `FCMS2103` before the write, wherever the field is (in a block or a repeated component too), so no provider sees one: SQLite would store it, and PostgreSQL and SQL Server would fail the write. An email address longer than 254 is no address and fails with `FCMS2108` first. `FieldLengths` in core holds the defaults for the storage model, the validator and slug derivation.

**Indexes.** Primary and unique keys above; `[Indexed]` fields; `[Unique]` fields as a partial unique index `WHERE state = 1`, with `AND "{column}" IS NOT NULL` when the column is nullable, scoped by table with the draft side checked in the validation pipeline: `(site_id, locale, field)` on `cms_{type}_i18n` for localized fields, `(site_id, field)` on `cms_{type}` for non-localized fields (that table has no `locale`), and the same without `site_id` for `ContentScope.Global` types, whose `site_id` is null and would defeat the index; GIN indexes on `jsonb` columns on PostgreSQL when `[Indexed]` is set on a JSON field.

`[Indexed]` on a column field is a plain index `ix_{table}_{column}`, named after the table that holds the column:

- `(site_id, state, field)` on `cms_{type}` for a non-localized field.
- `(site_id, locale, state, field)` on `cms_{type}_i18n` for a localized field, because lists always filter on one locale.
- The same without `site_id` for `ContentScope.Global` types.
- Nothing for a relation or media field, because `cms_reference` already indexes the target. On a JSON field it is the provider's JSON index above.

Key indexes are named `uq_{table}_...`, which leaves `ux_{table}_{column}` for `[Unique]` fields and `ix_{table}_{column}` for `[Indexed]` fields. A column flattened from a single component is named by its prefixed column, and `[Indexed]` and `[Unique]` on the component's field index that column.

**Identifier budget, as built.** PostgreSQL keeps the first 63 bytes of an identifier and drops the rest without an error, SQL Server allows 128 and SQLite has no limit, so 63 is the budget on every provider. Schema validation holds the names that are stored or mapped to it (`FCMS1218`, naming the type and the field): a type name and a field name have at most 64 characters, the size of `cms_document.type`, `cms_reference.source_type`, `field` and `target_type` and `cms_activity.target_type`; a content type's tables at most 63, so a collection or singleton name has at most 59 characters, or 54 when it is localized; a column, flattened from components included, at most 63; and the path of a relation or media field, which is its name in `cms_reference.field`, at most 64. Index and check names are built from the table and the column, so a valid schema may still make them longer, and they are shortened instead of refused. A name over 63 characters keeps its first 54, without a trailing underscore, and ends in `_` and the first 8 hex digits of the SHA-256 of the whole name: `ux_cms_customer_testimonial_highlight_i18n_hero_section_configuration_background_image_alt_text` becomes `ux_cms_customer_testimonial_highlight_i18n_hero_sectio_358eeeb1`. This is how EF shortens the names it makes, with a hash in place of EF's counter, so the result does not depend on the order of the model, and two names with the same first 63 characters, which PostgreSQL would cut to one and then refuse as a duplicate, stay apart. It covers the names core gives (`uq_`, `ux_`, `ix_`) and those a provider convention gives (`ck_..._json`, `gin_`), after the conventions run. A name that fits is unchanged, so no existing database sees a rename. Names are ASCII, so a character is a byte. This answers [#2873](https://github.com/fluentcms/FluentCMS/issues/2873) of the review of 7 Nov 2026 ([#2847](https://github.com/fluentcms/FluentCMS/issues/2847)): the budget is PostgreSQL's 63 bytes.

**Text, NULL and dates across providers, as built.** Three behaviours that differed on SQL Server now match the other providers:

- A `[Unique]` index over a nullable column (see "Nullability") is filtered on `state = 1 AND "{column}" IS NOT NULL` on every provider. SQL Server holds NULLs equal in a unique index, and EF adds its own `IS NOT NULL` only to an index with no filter, so two published entries with an empty `[Unique] email` collided there alone. SQLite and PostgreSQL never compare NULLs in a unique index, so for them only the definition changes. The validation pipeline already skips an empty value.
- On SQL Server, the text columns of content fields that are not JSON have the binary collation `Latin1_General_100_BIN2`, the one the JSON dialect applies to JSON text, through `IStorageConventions.ConfigureTextColumn`. Equality, `contains`, `startsWith`, `lt`, `gt` and `[Unique]` are then case-sensitive, as on SQLite and PostgreSQL, so `Hello` and `hello` slugs no longer collide on SQL Server alone, and text sorts by code point, as on SQLite. System columns keep the database's collation. `startsWith` keeps its `IndexOf == 0` check, because SQLite's `LIKE` ignores case.
- `SqlServerStorageConventions` writes every `DateTimeOffset` in UTC, system columns included, as `PostgreSqlStorageConventions` does, so `createdAt` reads back with offset zero on every provider instead of in the writer's offset. The converter keeps the column type, so the system migrations do not change.

A host's next content migration on PostgreSQL or SQL Server rebuilds the unique indexes of nullable columns with the new filter, and on SQL Server changes the collation of the content text columns. Schema sync on SQLite compares only whether an index is partial, so it reports nothing. These behaviours are checked on the model and on the SQL that EF generates (`OfflineProviderTests`); running the filter and unique tests on SQL Server is [#2711](https://github.com/fluentcms/FluentCMS/issues/2711)'s. This answers [#2874](https://github.com/fluentcms/FluentCMS/issues/2874) of the review of 7 Nov 2026 ([#2847](https://github.com/fluentcms/FluentCMS/issues/2847)): a binary collation on content text columns, and NULL left out of a nullable `[Unique]` index on every provider.

**Singleton key, as built.** The shared table of a singleton has the unique index `uq_{table}_singleton` on `(site_id, state)`, or on `(state)` for a `ContentScope.Global` singleton, with no filter. An entry has one shared row per state whatever its locales, so the database holds the type to one entry per site. A key on `cms_document` could not: that table has a row per locale, so `(type, site_id)` would refuse a singleton's second locale, and `(type, site_id, locale)` would let two entries in different locales through. The trash keeps an entry's content rows, so a trashed singleton keeps its place until it is purged, which is what the check before the insert already counts (it reads the trash). The index is part of the content model, so it arrives with the host's content migration (`fluentcms migrations add`) or schema sync, like any new index, and needs no system migration. A schema that later turns a singleton into a collection leaves the index in place: schema sync reports the drop and does not apply it, as for any other index.

**List and trash indexes, as built.** Every content table has `ix_{table}_state` on its scope and `state`, as an `[Indexed]` field index without the field: `(site_id, state)` on `cms_{type}`, `(site_id, locale, state)` on `cms_{type}_i18n`, and the same without `site_id` for `ContentScope.Global` types. `state` is a system column, so no field index takes the name. The shared table of a singleton has none, because `uq_{table}_singleton` covers those columns. Without it a list that filters on no `[Indexed]` field read `cms_document` of every type in id order; with it the list seeks its own rows and sorts them, because SQLite does not take the order on `cms_document.document_id` from an index of the content table. It is part of the content model, so it arrives with the host's content migration or schema sync, like the singleton key. `cms_document` has `ix_cms_document_type_site (type, site_id, deleted_at)`, which the check before a singleton is created seeks (`type` alone for a global singleton), and `ix_cms_document_trash (type, deleted_at) WHERE deleted_at IS NOT NULL`, which the trash purge seeks for one type's trash in the order it was trashed. On `deleted_at` alone SQLite took the first index for the purge and sorted every document of the type. Both come with the system migration `AddDocumentIndexes`. Deleting a site and removing a locale filter `cms_document` on `site_id` and `locale` and still scan it; they are rare administrative writes. `QueryPlanTests` checks these plans on SQLite.

**SQLite connections, as built.** The SQLite provider opens a file database in WAL mode, where readers and the writer do not block each other. A connection interceptor sets `busy_timeout` to 5 seconds on every connection, so a writer waits for the write lock inside SQLite, asks for `journal_mode = WAL` until SQLite reports it (the mode belongs to the file and persists), and then sets `synchronous = NORMAL`, which in WAL mode loses at most the last commits on a power cut and never the database. A file that cannot change mode keeps the rollback journal and `synchronous = FULL`. EF already creates a new database in WAL mode, so this matters for a file made another way. An in-memory or temporary database names no file and is left as it is. `cms.UseSqlite(connectionString, writeAheadLog: false)` turns all of it off. `SqliteWriteInterceptor` and `SqliteCommandTransactions` stay in both modes: they run every write in a transaction, which in the rollback journal keeps a writer from starving behind readers. WAL keeps `-wal` and `-shm` files beside the database and needs a local disk ([Docker](../docker.md), [host configuration](../host-configuration.md#sqlite)).

**JSON dialect.** Filters on block and JSON paths compile to LINQ. `IJsonSqlDialect`, implemented in each data provider package (`jsonb` operators, `JSON_VALUE`, `json_extract`), contributes EF function mappings and LINQ expressions, not SQL text, with one comparison per field type, because a JSON extraction returns the value's own type (on SQLite a JSON number never equals the text `'12'`). The supported set in the first release is: block list contains a block of type *X*, and equality or containment on a scalar path one level inside blocks or JSON, identical on all providers. Deeper paths are deferred and will be PostgreSQL-only when they come (Q7).

**Migration streams.** Three kinds of EF migration contexts share one database, each with its own history table:

| Stream | Owner | Generated by | History table |
| --- | --- | --- | --- |
| System tables (`cms_document`, `cms_version`, `cms_reference`, identity, jobs, settings, activity, sites, locales, plugins, locks, outbox) | `FluentCMS.Data.{Provider}` | CI, one set per provider | `cms__migrations` |
| Plugin tables (`cms_media_*`, `cms_webhook_*`, ...) | each plugin package, per provider folder | plugin CI | `cms_{plugin}__migrations` |
| Content type tables (`cms_article`, `cms_article_i18n`, ...) | the host project | `fluentcms migrations add <name>` using the host's `SchemaModel` and provider | `cms_content__migrations` |

At runtime one `CmsDbContext` holds the union model for querying, with the system and plugin tables marked `ExcludeFromMigrations`. The migration runner applies the streams in order system, plugins (dependency order), content, at startup when `FluentCms:Database:MigrateOnStartup` is true (default true on SQLite, false otherwise) or through `fluentcms migrate`. `fluentcms migrate --check` lists pending migrations and exits non-zero.

**Runner messages as built.** `fluentcms migrate` and `migrate --check` come with the CLI in M6 (6.1), so the CLI has `migrations add` only and nothing outside the runner calls `MigrateAsync` yet. Until then the runner's messages point at the startup option: `FCMS1305` and an `FCMS1301` whose content migration only needs applying end with `set FluentCms:Database:MigrateOnStartup to true to apply the pending migrations`, and the schema history logs (9314, 9315) name a start with that option (#2889).

**Schema sync and plugin tables as built.** Schema sync builds its context from the options of the host's `CmsDbContext` with the target schema swapped in, so its model maps the plugins' tables as the runtime context does. A table under the prefix `cms_{plugin}_` of a plugin of the plugin host or of a stream registered through `IPluginMigrations` is never a stale content table, whether the plugin maps it, migrates it or neither, and neither is any `__migrations` history table. A host that runs schema sync with a plugin that owns tables therefore records its schema history row. The code review of 7 Nov 2026 asked whether the plugin-less context was deliberate; it was not (#2872).

**The provider of a migration set as built.** A content or plugin set carries the store types of the provider it was scaffolded for, and startup validation compares names only, so EF would apply a SQLite set to PostgreSQL unnoticed. A provider switch on an existing database is not supported (#2875). Startup and `MigrateAsync` read the provider that EF writes on the model of every snapshot and migration target model: PostgreSQL and SQL Server record their value generation strategy under `Npgsql:` and `SqlServer:`, and SQLite records neither. They compare it with the provider of the context's own model and refuse a set that differs with `FCMS1306`, which names each stream, the set's provider and the host's (`stream content was scaffolded for SQLite; this host runs PostgreSQL, and a migration set applies only to the provider it was scaffolded for`). This happens before anything is applied and without opening the database. The migration files need no marker. A hand-written migration with no target model names no provider and is not refused. The system stream ships with its provider package and is not checked, and schema sync skips the content stream, so a host can sync on SQLite in Development while its content set is for its server provider. Per-provider plugin folders belong to #2711, and comparing store types at startup belongs to #2879. Until #2711, a host on PostgreSQL or SQL Server that registers the webhooks plugin stops with `FCMS1306`.

**Generation rules.** Each schema gets its own EF model, so `IModelCacheKeyFactory` is replaced with one keyed on the schema hash. Migrations are always scaffolded on a fresh context, because a context that has already called `HasPendingModelChanges()` silently emits `CreateTable` for every table. Generated migration files are marked `<auto-generated />`, because EF marks only the designer and snapshot files and the composite index columns in the migration file fail the build with CA1861. On SQLite, `Migrate()` creates `__EFMigrationsLock`, which all streams share; it is the one exception to the `cms_` prefix rule.

**Development loop.** With `FluentCms:Database:SchemaSync = Auto` (default in Development on SQLite only) the content stream is skipped and additive changes (new tables, new nullable columns, new indexes) are applied by diffing the model against the database at startup. Destructive changes are reported, never applied. This gives "schema changes apply without restart" for new fields and keeps production on explicit migrations.

**Development loop outside Development, as built.** A host whose types are data has no compiled content migrations, which is the reference host with a schema file or the sample (Q2). `cms.AllowSchemaSyncOutsideDevelopment()`, in code only so that no configuration key turns it on, makes `SchemaSync` default to `Auto` on SQLite in every environment, with the rules of Development: additive changes apply at start and destructive ones are logged and left undone. `FluentCms:Database:SchemaSync = Off` still turns it off. A provider without schema sync (PostgreSQL, SQL Server) refuses the opt-in when `AddFluentCms` runs (`FCMS3154`), so their content tables stay on migrations. The startup report lists `schema-sync-outside-development` when sync runs outside Development. This is how Q2 works in Production until the M6 CLI generates migrations from a schema file.

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
| `UpdateAsync(id, entry, expectedVersion)` | full update of the draft: a field left out is cleared, except one the principal cannot read or write, which keeps its value. Conflict throws `VersionConflictException` with the current version. `expectedVersion: 0` adds a locale the entry does not have yet, and needs `update`, not `create` |
| `PatchAsync(id, patch, expectedVersion)` | partial update of the draft with a JSON merge patch; a null removes a field. Its own method, because a class cannot express absence |
| `DeleteAsync(id)` | trash, in every locale |
| `RestoreAsync(id)` | from trash, in every locale |
| `QueryTrashAsync(ContentQuery)`, `PurgeAsync(id)` | list the trash; delete an entry in it for good (4.5) |
| `PublishAsync(id, locale?, expectedVersion?)`, `UnpublishAsync(id, locale?)`, `DiscardDraftAsync(id, locale?, expectedVersion?)` | publishing transitions (4.5); publish returns the entry and `warnings` |
| `ScheduleAsync(id, ScheduleRequest)`, `CancelScheduleAsync` | per locale |
| `GetVersionsAsync(id, locale?)`, `GetVersionAsync(id, number, locale?)`, `RestoreVersionAsync(id, number, locale?, expectedVersion?)`, `DiffAsync(id, a, b, locale?)` | versions |
| `BulkAsync(BulkRequest)` | create many; update, publish, unpublish, delete by id list or by filter with a required cap; per-item result |
| `CountAsync(filter)` | |

**Contract as built.** Every method of `IContentService` takes the type name first. On a save `expectedVersion` is a required `int`, so no caller skips it; on publish, discard and version restore it is optional, for a caller that wants to act only on what it reviewed.

- **Refusals.** A principal that lacks the permission on the type gets `ContentAccessDeniedException` (HTTP 401 or 403). An entry that does not exist, is in another site or of another type, or is refused by its row rule gives `EntryNotFoundException` (404), so a client cannot probe; a read returns null. Delete and restore need the operation on every locale of the entry: none reached is not found, some reached is denied. An unreadable type on a read stays the engine's `ValidationException` on `type`, the same as an unknown type.
- **System operations.** An operation with no ambient `OperationContext` throws. `Principal.System` is accepted only from `Origin.Job`, `Origin.Cli` and `Origin.Seed`, entered through `SystemOperation.Begin`; from `Api`, `Admin` or `Mcp` it throws.
- **Audit fields.** `created_by`, `updated_by`, `deleted_by` and `author_id` come from `OperationContext.AuditPrincipal` (4.12). Authorization reads `Principal` alone.
- **Row rule and grants after the write.** A create or update that puts the entry outside the row rule that allowed it is refused and rolled back. A field the client sent or a hook changed must be allowed by a grant whose row rule holds for the new entry.
- **Hooks.** A before hook that changes a field outside the principal's grants fails the operation (`FCMS1406`); a change to a `Hidden` or `ReadOnly` field is allowed, since that is what a hook fills.
- **Uniqueness counts the trash**, because the partial unique index does; only a purge frees a value.
- **Bulk.** `BulkAsync(type, BulkRequest)` runs the single call for each entry (`CreateAsync`, `PatchAsync`, `PublishAsync`, `UnpublishAsync`, `DeleteAsync`), each in its own transaction, so every entry gets its own permission check, hooks, validation, publish policy, version, events and activity row, and one that fails rolls back only itself. Its result per entry is `Succeeded`, `Failed` with the exception the single call threw (an entry the principal cannot reach is the same `EntryNotFoundException` as a missing id), `Duplicate` for an id named again, or `NotRun` after cancellation, which is checked between entries. A request by filter needs a cap of at most `BulkRequest.MaxItems` (1000): the filter is read against the drafts, keeps only entries the principal may read and may also perform the operation on, and is read in full before the first write, so one over the cap is refused (`FCMS2207` on `cap`) with nothing written. The operation's row rule joins the read rule in the selection query, on the entries selected and not on their relation targets, so the database returns only entries that can be changed, and a filter that still reaches more than ten times the cap of entries the single check refuses is refused (`FCMS2207` on `filter`). An update by filter applies one merge patch, and a publish by filter publishes, at the version the filter read, so a draft changed since is a conflict; an unpublish or a delete by filter acts on what matched when the filter was read, and both can be undone. A failure that is none of the single call's refusals is logged and reported as `Failed` with an `InvalidOperationException` that names no cause, and the next entry runs. An entry that committed is `Succeeded` even when reading it back fails, which is logged. A value the operation does not use, such as a cap on a request by id, is refused rather than ignored, and a bulk call inside a transaction throws, since there a failed entry could undo the others.

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

**Compilation.** The AST compiles to LINQ over the property-bag entities. Relation paths join through `cms_reference` to the target type's row in the same state and locale (decision 44: no fallback-aware filtering). Block and JSON paths go through `IJsonSqlDialect`. Row rules from the evaluator are appended as an `And` node before compilation, so authorization and filtering are one query. `state` is inlined as a literal and every user value is bound as a parameter, by reading a member of a holder object, because a bare expression constant becomes a SQL literal. SQLite does not need the inlined `state`, but a literal matches the `WHERE state = 1` index predicate as written, and [#2711](https://github.com/fluentcms/FluentCMS/issues/2711) checks the other providers.

**Population.** Default depth 1, cap configurable (default 3). Beyond the depth a relation is returned as `{ "id": "...", "type": "author" }`. A target the principal cannot read, by type or by row rule, is returned as null at any depth, or left out of a multiple relation. Cycles stop at the cap. Field selection applies to populated entries with dotted paths.

**Pagination.** Offset and page with `total` (skip the count with `withTotal=false`). Keyset cursor: base64 of the sort key values plus the document id, which is always appended as the final sort key for stability. A v7 `document_id` makes that key stable, but it does not give creation order within one millisecond.

**Concurrency.** Each `(document, locale)` has an integer `version`. Writes carry `expectedVersion` (`If-Match: "7"` on REST). A mismatch is rejected with 412 and the current entry in the problem details `current` extension.

**Unique races, as built.** Each of these refusals comes from a check before the write: an existing singleton, a locale the entry already has, a taken `[Unique]` value, a taken site slug. The database enforces the same keys: the singleton key and the published side of `[Unique]` (4.3), `uq_cms_document_document_locale`, and `uq_cms_site_slug`. On SQLite the content service's transaction takes the write lock when it begins, so a second writer waits and its checks see the first. On PostgreSQL and SQL Server under READ COMMITTED, two writers can pass their checks together, and the second then breaks the key at its insert. `IDataProvider.IsUniqueViolation` recognises that error, in each provider package: SQLSTATE 23505 on PostgreSQL, errors 2601 and 2627 on SQL Server, and `SQLITE_CONSTRAINT_UNIQUE` or `SQLITE_CONSTRAINT_PRIMARYKEY` on SQLite (a primary key counts on the other two as well). `ContentService` runs a write that meets it once more, in a new transaction and on the context as it was before the first run (what that run left tracked is detached, and the caller's entries go back to the state and values they had), and its checks then see the winner, so a loser gets exactly what a writer that came second gets and never a 500:

- a second singleton: `FCMS2401` (409);
- a locale added twice, as with a stale `expectedVersion`: `VersionConflictException` (412, `FCMS3105`) with the current entry;
- a publish of a value another entry published first: `NotUnique` (`FCMS2111`, 422) on the field.

A hook that ran before the failed insert runs again in the second run, inside its transaction, unless a check refuses first; what the first run raised is never delivered, since its events and outbox rows went with its rollback. A second unique violation in that run is a key no check covers, and it fails as it is. A write joined to a transaction the service began runs again with it; one inside a transaction the caller began fails with the database error, because only the owner can start over. The draft side of `[Unique]` has no index, so on PostgreSQL and SQL Server two drafts saved together may share a value; each is refused at publish while the other holds it, and an empty value, which drafts do not clash on, goes to whichever publish commits first. `SiteManager.CreateAsync` answers a slug taken between its check and its insert with the error of a taken slug (`FCMS2304` on `slug`). This settles decision 2 of [#2847](https://github.com/fluentcms/FluentCMS/issues/2847): the database enforces the singleton, and a lost race answers as a sequential loser does. Only SQLite has run these paths; the race tests in `FluentCMS.Data.Tests` move to the provider matrix with [#2711](https://github.com/fluentcms/FluentCMS/issues/2711).

**Round trips of a write, as built.** A save reads the entry's documents, its draft rows and references, and the entry as the principal's read shows it, checks each `[Unique]` value, advances the version, replaces the references, and saves the rows and the version in one save. That save writes over the rows the transaction read without reading them again, and has no savepoint of its own, since the service's transaction or savepoint already takes back a save that fails. A check of the entry the service has just found or written in its site does not look for it in that site again (`AccessTarget.Found`), so with no row rule the checks before and after the write send nothing. The reads inside a write use the locale its operation resolved. A read asks once per target type which relation targets, at the top level and nested, the principal may read. The versions of an entry are counted only once its draft version passes the cap, since versions are numbered by it. The entry is copied into JSON once before the hooks and once after each kind of hook that ran, and the version snapshot is the last copy. `ContentRoundTripTests` holds a typical update to these numbers: on a non-localized type with three relations, blocks and a `[Unique]` slug, an editor with no row rule makes 18 round trips, counting a save as one, where it made 33, and copies the entry twice, where it copied it ten times.

**Validation pipeline.** Field type coercion and declarative rules, custom field validators, entry validators, uniqueness (both states, per site and locale), then, on publish, `Required`, `RequiredWhen` and reference integrity. On a draft the content service writes `''` and the empty rich text document for missing `NOT NULL` values (4.3), and publish treats `''` as missing.

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
  "sections": [{ "type": "hero", "id": "V1StGXR8_Z5j", "heading": "..." }],
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
| Discard draft | copy state 1 rows to state 0, as a new version; needs `update` | permission | `EntryDraftDiscardedEvent` |
| Restore version | new draft from snapshot; needs `update` | draft rules | `EntryVersionRestoredEvent` |
| Trash | set `deleted_at`; excluded by the global query filter | permission | `EntryTrashedEvent` |
| Restore from trash | clear `deleted_at` | permission | `EntryRestoredEvent` |
| Purge | delete all rows; separate permission `content.{type}.purge` | | `EntryPurgedEvent` |
| Schedule | row in `cms_schedule`, job enqueued | | `EntryScheduledEvent` |

**Versions.** Every save writes an immutable snapshot (decision 38) with author, origin, client id and an optional message. A per-type cap (default 50, `[PublishPolicy(MaxVersions = n)]`) prunes oldest drafts when a version is written; versions that were published are never pruned, and only a publish marks one so, in `status_after`. Diff is computed on demand as a field-level structural diff (blocks and rich text compared as trees) and returned as a list of `{ path, kind, before, after }`. A snapshot holds hidden fields, so reads and diffs go through the reader's field limits as they are today, and need the Management scope as well as the draft read. Access to versions is decided on the entry's current row: history is part of the entry, so a reader the row rule lets in also sees versions from before the entry came within the rule. A restore is a write of the principal's own: what it may not write or read, and what is hidden or read-only, keeps the value the draft holds, except for the system principal. A principal that can write a field it cannot read may not discard or restore at all, whatever that field holds, because the status and version those answer with would check a saved guess against the value.

**Trash.** `DeleteAsync` sets `deleted_at` and `deleted_by` on every locale of the entry, and the global query filter on `cms_document` hides it from reads, queries, versions, publishing and reference checks; the content rows, references and versions stay as they were, so a restore brings back the entry as it was. `QueryTrashAsync` reads the trash as drafts through the query engine, in one locale, so the read grant, its row rule, its locale and field limits and the site hold; it needs `restore` or `purge` on the type and lists only what the principal may read. Every entry carries `deletedAt`, and `deletedBy` where the principal may read who changed an entry. The query filter is off for that whole query, so every row reached through a relation says explicitly that it is not in the trash: a trashed target never matches a filter or a row rule and is never populated. A filter on the id of a relation matches only a target the same read would show: of that type, not in the trash, in the site of a site-scoped type, and published on a published read. `PurgeAsync` needs `content.{type}.purge`, deletes the content rows of every state and locale, the references the entry holds and every version of it, published ones included, and raises `EntryPurgedEvent` per locale. It follows the rules of restore: an entry that is not in the trash, in another site or type, or refused by the row rule of the grant is `EntryNotFoundException`, the same for all of them; one the principal reaches in some locales only is `ContentAccessDeniedException`. References from other entries to a purged one are the values of their fields and stay; a read leaves out a target that is not there, and publishing warns with `FCMS2404` as it did for the trashed one. The values the entry held are free again, since uniqueness counted the trash.

**Retention.** `[PublishPolicy(TrashRetentionDays = n)]` (`PublishPolicy(trashRetentionDays: n)` on the builder, `trashRetentionDays` in schema JSON) sets how long a type keeps its trash; the default is 30 days, the least 1 and the most 36500. The job `content.trash.purge` runs daily at 03:17 UTC from a recurring schedule stored at every startup. Each run purges, per type and site, the entries trashed longer ago than the type keeps them, through `PurgeAsync` as `Principal.System` with `Origin.Job`, one transaction per entry, at most 500 purges per type per run (`Limit` in the payload). Due entries are read oldest first in pages (`PageSize`), so an entry that fails is logged and left for the next run without using up the limit or hiding those behind it, and a type that fails is logged while the others still run. The purge checks inside its transaction that the entry went to the trash before the cutoff, so one trashed again since is left alone. A run logs the number purged and failed. A run that finds nothing due does nothing, so it is safe to run again.

**Reference integrity.** On publish, references to unpublished or trashed entries produce warnings in the response (`warnings[]`) by default; `[PublishPolicy(UnpublishedReferences = Block)]` on a type turns them into a 422. A target counts as published when it has a published row in the entry's locale or its fallback chain. A self-reference is exempt. A cycle is not: under `Block` neither side can go first, and the way out is to publish one side without the reference and set it afterwards.

The answer must not be a way to ask about an entry the principal may not read, since a write takes any id:

| Target, for a principal other than the system principal | Warn | Block |
| --- | --- | --- |
| Published, and it may read it | nothing | publishes |
| Unpublished, and it may read it | `FCMS2403` with the id | `FCMS2403` on the field |
| Trashed, missing, in another site or of another type; refused by its row rule, asked of the locale that serves the target; or with rows only in locales its grant does not cover | `FCMS2404`, no id, all identical | `FCMS2404` on the field |
| Of a type it may not read, or of no type at all | nothing | always one refusal, no path |
| In a field it can write but not read, which is where it chooses the id | nothing | always one refusal, no path |
| In a field it can neither read nor write, or hidden: it did not choose the id | nothing | a refusal with no path, only if the target really is a problem |

The system principal is told the id on `FCMS2404`.

**Reference checks, as built.** Publishing, scheduling, a diff and a version restore ask about an entry's targets per type, not per reference: one query reads the rows of every target of a type, and one for each locale that serves them asks the row rule (`IAuthorizationEvaluator.AccessibleEntriesAsync`), as a read does (4.4). A diff asks about the targets of both versions at once. `ContentRoundTripTests` checks that 2 and 20 references cost the same number of queries, with and without a row rule on the target type.

**Relation targets on a read.** The same rules hold for a top-level relation, a relation inside a block or component, an entry link inside rich text, and a relation in the `data` of an inline block, which also count in `cms_reference` and reference integrity. A target the principal may not read by type or row rule, in another site, of another type than the field's, or with rows only in locales its grants do not cover, is returned as null or left out of a list. A row rule is asked only of the locale being read, so where that locale has no rule, only grants with no rule count as covering a locale: a rule on another locale's grant cannot vouch for a row there, and such a target is left out even if the principal could read it directly in that locale. A rich text link keeps its text and loses its target, and a link to an unknown type is treated as one to an unreadable type. A published read shows only what is published: a target that is trashed or has no published row in any locale goes too, and one published only in another locale stays `{ id, type }`, since a reader with a fallback can follow it. A draft read keeps a trashed target of a type the principal may read as a reference, because that is what its author attached. A filter on the relation still matches a target left out: it answers about the source entry and a value the caller named.

A write answers what a read showed, so what the read left out stays: a nested target goes back in its block, and a rich text document keeps its links when it comes back as it was read. Rich text nodes have no ids, so an edited document loses the links the principal could not see. Keeping must not test whether the target exists, or a later warning would say so: a principal keeps every target its read left out, whatever the id is. Only one whose read grant on that type has no row rule and no locale limit, which a read tells anyway, loses a target that does not exist in that type and site, trashed counting as existing. So a limited principal cannot clear a ghost id, and under `Block` the entry stays refused until a wider principal does. The system principal keeps nothing it did not send.

**Read-only values sent back.** A full update may send back a top-level field the principal can see but not change: it is no write while it is the same as the read showed. The same holds inside a component, a block, the `data` of an inline block and an inline block inside a block, on an update and a patch alike, since such a field is sent whole: a read-only value that is the same as the read showed in the stored item it pairs with is taken out of the write, as if the client had left it out, and the stored value stays. A single component is its own pair. The items of a block list or a repeated component, and the inline blocks of a document, pair as the write keeps their hidden values ([ADR 0002](../decisions/0002-stable-block-ids.md)). A block or an inline block sent with its `id` pairs with the stored block of the same id and type anywhere in the same top-level field, so it may move into or out of a nested list or the document of another inline block. One whose id is not stored is new and pairs with none. An item without an id, as every item of a repeated component is, pairs within its list or document among the stored items whose ids the write does not send: first one of the same type with the same visible content, then the next one of that type in order. A block then takes the id of its pair. So a read-only value that moves with its block, or stays on a block whose neighbour was added or removed, is no change, and one that is not the value of the stored item its item pairs with is refused (`FCMS1406`): kept in place while the content around it moved, sent on an added item that pairs with none, set where its pair has none, or sent as null where its pair has one. A client that edits blocks may leave the read-only values out, and they follow their blocks as hidden values do. The comparison is with what the read showed, so the answer tells the principal nothing it could not read, and a value that names a hidden field is never the same. Only a field the principal may write is looked into, and a new locale compares only shared fields, since a localizable field has nothing in that locale to show. A client that drops ids still gets a guess. A block moved and edited without its id, or a new block without one in a write that removes a block of its type, may take the read-only and hidden values of another block, whether or not they are sent. A client that wants a block treated as new sends an id of its own.

**Shared fields.** Non-localizable fields live on one row per state, so publishing one locale publishes them for every published locale. A grant limited to some locales may publish only when those fields have not changed, or no other locale is published. The other locales' status is recomputed after a transition that moves the shared row. So `published_version` names the version a locale last published, and what that locale serves can differ from that snapshot in its shared fields.

**Scheduling.** `cms_schedule (id, document_id, locale, action, run_at_utc, time_zone, status, job_id, created_by)`. The job runner executes `ScheduledPublishJob` and `ScheduledUnpublishJob`; `cms_document` mirrors the next scheduled times for list views. Cancel and reschedule update the row and the job.

**Schedules as built.** `ScheduleAsync(type, id, ScheduleRequest, locale)` and `CancelScheduleAsync(type, id, action, locale)`, per locale and action. A request is an instant in UTC, or a wall-clock time with an IANA zone: the zone's rules on that day give the instant, a time the clocks skip is refused (`FCMS2408`), a time they repeat runs the first time, and a time not in the future is refused (`FCMS2407`). `cms_schedule` also holds `version` (the draft version when scheduled), `created_at`, the `claims` of the scheduling principal, `completed_at` and `error`. `status` is `pending`, `executed`, `cancelled`, `skipped` or `failed`; at most one row is pending per document, locale and action, so a reschedule moves it, and the others stay as history. `cms_document.schedule_publish_at` and `schedule_unpublish_at` mirror the pending times of that locale.

The schedule and its `cms_job` row are written in the transaction of the call, so there is never one without the other, and cancel and reschedule remove the job if it has not been claimed. The row is what counts: a job runs only if its schedule is pending and names that job, and it claims the schedule with a guarded update, so a cancelled or moved schedule is never run by the job it left behind, and two workers run it once. A schedule needs the permission, row rule, locale, site and field limits of doing it now, and for a publish under `Block` the reference rules of the principal, with a target that is not published yet let through, since it may be by then. An entry the principal cannot reach, or a request that is wrong, answers the same whatever the id, and cancel does not tell a hidden entry with a schedule from one without.

The jobs `content.schedule.publish` and `content.schedule.unpublish` run as the system principal with the scheduler as `OperationContext.Actor`, so versions, events and the activity log name that user and the origin is `Job`. Before it acts the job reads the scheduler's claims again, looks up the grants of its roles as they are now, and applies the checks of a schedule in full, the reference rules included, so a role that has lost the permission, or been deleted, publishes nothing. A refusal is final: the schedule is `failed` with the codes and paths of the refusal, and the job succeeds. Any other failure is retried by the runner, and recorded as `failed` when the last attempt is used, unless the attempt was stopped by the job's own token. A job the runner dead-letters some other way (an attempt that ran out of time on the last try, a worker that died) is heard through `JobDeadLetteredEvent` by an in-process handler, which closes its schedule as `failed` and clears the mirror (an outbox handler would give every host an outbox; a crash between the settling of the job and the event leaves the schedule pending and its job in the dead-letter list). A `failed` or `skipped` schedule is final: bringing its job back from the dead-letter list finds the schedule closed and does nothing, and the entry must be scheduled again. An entry in the trash is `skipped` (one restored before it is due still runs), a purge removes its schedules and their jobs, and a publish of an entry that is already published or an unpublish of one that is not is `skipped`. A scheduled publish publishes the draft version that was scheduled (`version`), not whatever the draft has become: if the draft has been saved since, or a field shared by every locale has (`shared_hash` holds a hash of those fields when a publish is scheduled, since a save through another locale leaves this locale's version as it was), the schedule is `skipped` with `FCMS2409`, the mirror is cleared and nothing is published, so an editor who may only update cannot have text published under another's name; the entry is scheduled again to publish the new draft. An edit to another locale's own fields does not stop it, and the publish passes the scheduled version as `expectedVersion`, so a draft saved after the check fails the attempt with a conflict, which the runner retries and the next attempt skips. An unpublish does not depend on the draft. Cancelling a schedule needs the checks that creating it needed (the grant's field limits as well), after the same reach as any call, so it tells nothing of an entry the principal cannot read. An admin user's roles are read from `cms_user_role` as they are when the job runs, and a deactivated user holds none; the claims kept at scheduling still give the scopes and the claims row rules read, and the roles of a principal that is not a stored user, such as an API key until keys are stored.

**Agent-safe publishing.** Nothing in core treats `Origin.Mcp` differently: publish needs `content.{type}.publish` for everyone. The safety comes from the MCP plugin exposing publish as a separate tool and from roles given to agent principals. Origin is recorded on every version and activity row, so agent writes are filterable.

### 4.6 Localization and sites in core

Rows 7.1 to 7.6, 7.8, 7.10 to 7.12 (model and API parts); decisions 42 to 45.

**Tables.** `cms_site (id, name, slug, default_locale, is_default, created_at)`, `cms_site_locale (site_id, code, position, fallbacks json, enabled)`. The first system migration seeds the default site with locale `en`.

**Locale resolution.** `locale` parameter or `X-Cms-Locale` header, else the site's default. `locale=all` returns every locale of an entry as a map. No `Accept-Language` negotiation, to keep Delivery responses cacheable by URL.

**Fallback.** Per site an ordered chain (`fr-CA` to `fr` to `en`), applied per field on read; `fallbacks` in the response names the locale that served each fallen-back field; `fallback=none` disables. Lists filter on the requested locale only (decision 44).

**Sites.** `site_id` is on `cms_document`, `cms_*` content tables, roles, API keys, settings and activity rows. Resolution order: API key binding, `X-Cms-Site` header, `site` parameter, hostname (multi-site plugin), default site. Content types are site-scoped by default; `[Collection("tag", Scope = ContentScope.Global)]` makes one entry set shared by all sites (`site_id` null) and allowed as a relation target from any site.

**Management endpoints.** Sites and locales CRUD live in core (`/api/management/v1/sites`, `/api/management/v1/sites/{id}/locales`) because the data model is core; the multi-site plugin adds hostnames and anything beyond the default site (Q6a).

**Site and locale caching as built.** Every request resolves its site and locale before its own work, so `SiteCache` keeps, on each instance, the default site, the site of each slug, that a site exists, and each site's locales (its default and its `cms_site_locale` rows), for 30 seconds (`SiteCache.CacheDuration`). A write through `SiteManager` (creating or deleting a site, a site's name or default locale, adding, changing or removing a locale, `EnsureLocaleAsync`) drops all of it at once, and so does a `SiteUpdatedEvent` raised on the instance, which is how a plugin that changes a site, such as the multi-site plugin, does the same; a read that was under way during the write is not kept. What is not found is never kept, so a site that another instance created is found on the next request. No signal reaches another instance (an outbox delivery runs on one instance only), so another instance sees a change to an existing site or its locales when its copy expires, 30 seconds at the most, as plugin enablement (4.13) and settings already do. This answers [#2857](https://github.com/fluentcms/FluentCMS/issues/2857) of the review of 7 Nov 2026 ([#2847](https://github.com/fluentcms/FluentCMS/issues/2847)); the round trips it saves are in 4.10.

### 4.7 Identity and the authorization server

Rows 4.1, 4.2, 4.9 to 4.11, 4.13, 4.15, 4.16, 2.12, 2.13; decisions 7, 30, 33.

**Foundation.** ASP.NET Core Identity with `CmsUser` and `CmsRole` (GUID keys) in `cms_user*` and `cms_role*` tables, and OpenIddict server with its stores in `cms_oidc_*`.

**Endpoints.** OpenIddict defaults: `/connect/authorize`, `/connect/token`, `/connect/userinfo`, `/connect/introspect`, `/connect/revoke`, `/connect/logout`, `/.well-known/openid-configuration`. Flows: Authorization Code with PKCE for public clients (admin, MCP clients, CLI), rotating refresh tokens, client credentials for service accounts. Access tokens are JWTs signed with keys from a configured certificate or, when none is configured, a generated key persisted in `cms_signing_key` through Data Protection. Audiences: `fluentcms:management` for admins and keys, `fluentcms:members` for members.

**Authorization server as built.** OpenIddict 7 with its EF Core stores on the system model in `cms_oidc_application`, `cms_oidc_authorization`, `cms_oidc_scope` and `cms_oidc_token` (GUID keys, snake_case columns), and `cms_signing_key`. The endpoints are in the `Connect` route group, which takes the `auth` rate limit, the Management CORS origins and the security headers; discovery and the key set are at `/.well-known/openid-configuration` and `/.well-known/jwks` in a `WellKnown` group with the same origins. PKCE is required of every client, not only public ones, and only `S256` is offered. A refresh token rotates, and a used one is refused once OpenIddict's reuse leeway of 30 seconds is over, which lets two refreshes that cross, as two tabs of one application make, both succeed; a reuse after it revokes the session's tokens. A zero leeway was the first choice and was given up (#2854), since it signed users out on such a race. The token endpoint reads the user again for a code or a refresh token and refuses one who is deactivated or locked out, or whose security stamp, which codes and refresh tokens carry, has changed, so a password change or reset ends them. A bearer token is read from the `Authorization` header only. Access tokens are signed, not encrypted, JWTs with the audience `fluentcms:management`, and carry `sub`, `name`, `fcms_kind` (`AdminUser`, or `ServiceAccount` for client credentials, whose `sub` is the client's id), `scope`, `client_id` and, for a service account only, `roles`; a user's roles are read from `cms_user_role` on each request as before. `fluentcms:members` is defined for the member pool, which issues no tokens yet, and the bearer scheme accepts the management audience only. Scopes are `openid`, `profile`, `email`, `offline_access`, `draft` and `management`, and narrow what roles allow as 4.8 says. The `FluentCms` scheme's bearer scheme is OpenIddict's validation scheme on the local server, which rewrites a valid token's principal to those claims and checks the token and its authorization in the database on each request, so a revoked session stops its access tokens at once. The request bridge sets `OperationContext.ClientId` from `client_id`, and a request from the client named by `cms.Clients.AdminClientId` (`fluentcms-admin` by default) runs with `Origin.Admin`. A refused credential keeps its handler's 401 code over HTTP (`FCMS3016` or `FCMS3019`), and `FCMS3102` otherwise. Tokens are signed with `FluentCms:Security:SigningCertificate` (a PKCS#12 file with an RSA key; `FCMS3022` when it cannot be read) or else with a generated RSA key kept in `cms_signing_key` and protected with Data Protection, which is what `ISigningKeyStatus` reports for `FCMS1102`; the symmetric key that encrypts codes and refresh tokens is always generated and kept there. The first key of each use has a fixed id, so instances that start together on an empty table insert one row and the others read it; the primary key settles that race with no wait, so it does not take `IDistributedLock` (4.15). The Data Protection key ring is in `cms_data_protection_key` under the application name `FluentCMS` unless the host persists it elsewhere. Clients are registered at start from `cms.Clients.Applications`, bound from `FluentCms:Clients:Applications:{clientId}` (`FCMS3021` for a bad one): a client with redirect URIs uses the code flow and refresh tokens, one with a secret is confidential and may use client credentials with the instance-wide roles it names, a listed client is replaced by its options at each start, one the options registered and no longer list is deleted with its sessions and tokens and a warning, unless the options list none at all, and others are left alone. Consent is implicit for these clients unless one sets `RequireConsent` (see the hosted pages as built). The authorize endpoint challenges the Identity application cookie, whose login page is `/account/login`, and answers `login_required` to `prompt=none`; logout ends that cookie on the POST of the logout page, which carries an antiforgery token, or with a valid `id_token_hint` of the user the cookie belongs to; any other request, a cross-site POST or a POST with a hint but without the cookie included, goes to the logout page, which asks, and a request without the cookie only returns to the client, with no expiring cookie (#2854). `UserSessions` lists a user's valid authorizations that still hold a token in use as sessions and revokes one with its tokens, for the Management API (#2786), and the `oidc.prune` job removes unusable tokens and authorizations after 14 days. The Data Protection ring is stored unencrypted unless the host adds an encryptor, which `IDataProtectionStatus` reports, and Production refuses to start without one (`FCMS1106`, `UnencryptedKeyRingCheck`) unless `FluentCms:Security:AllowUnencryptedKeyRing` is true. A host adds it with Data Protection's own builder, `AddDataProtection().ProtectKeysWithCertificate(...)` or `ProtectKeysWithAzureKeyVault(...)` or any other `IXmlEncryptor`, which it reaches from `IServiceCollection` like any host, so Core has no pass-through for it. Export and backup packages must never include `cms_data_protection_key`, `cms_signing_key` or `cms_oidc_*`. Codes `FCMS3021` and `FCMS3022`.

**Sign-ins and sessions as built.** The follow-ups of the code review of 7 Nov 2026 (#2854). Lifetimes are `cms.Clients`, bound from `FluentCms:Clients`, and keep the libraries' defaults: `AccessTokenLifetime` (1 hour) and `RefreshTokenLifetime` (14 days, how long a refresh token lasts unused, since using one gives a new one) set OpenIddict's, `SignInCookieLifetime` (14 days, sliding) sets the `ExpireTimeSpan` of the Identity application cookie, and `AbsoluteSessionLifetime` (unset, no limit) is how long a session lasts after the sign-in however often it is refreshed. Each is more than zero and at most 365 days, and the access token lasts no longer than the refresh token (`FCMS3021`). `CmsSignInManager.SignInWithClaimsAsync` stores the time of the sign-in in the cookie's properties (`.fcms.signed_in_at`), which the cookie's renewals and the stamp check keep while they move its issue time; a cookie from before falls back to its issue time. The authorize endpoint signs the user out and challenges, as for a second factor, on `prompt=login`, when the sign-in is older than the request's `max_age`, and when it is older than `AbsoluteSessionLifetime`; with `prompt=none` it answers `login_required`. The request the login page returns with drops `login` from `prompt` and drops `max_age`, which the new sign-in answers, so the user is not asked again. The identity token carries `auth_time`. Codes and refresh tokens carry `fcms_session_start`, the time of the authorize request, and the token endpoint refuses a refresh past `AbsoluteSessionLifetime` with `invalid_grant`; a refresh token from before counts from its first refresh. A wrong recovery code counts towards the lockout of `cms.Users` and a locked-out user's code is refused unspent, as for the authenticator, since Identity's `TwoFactorRecoveryCodeSignInAsync` does neither. A password sign-in of a user who may not sign in, deactivated or locked out, runs the password hash before it is refused, as an unknown name does, so the time of the answer does not tell them apart. Token and authorization entry validation stay on for every bearer request: a cache keyed by token id would let a revoked token through until the cache expired, and instant revocation is worth the lookup; it can be revisited with the database work of #2857. Data Protection's hosted service is taken out by the full name of an internal type, and a unit test fails when the framework no longer has it.

**Hosted pages.** The Authorization Code flow needs server-rendered pages: login, logout, consent (third-party MCP clients), two-factor challenge and recovery codes, forgot and reset password, accept invitation, external login callback, error. They ship as Razor Pages in `FluentCMS.Core` with minimal CSS, brandable through settings (logo, colours, name) and overridable by the host. This is the one piece of UI in the backend.

**Hosted pages as built.** Razor Pages of the `FluentCms` area in `FluentCMS.Core`, which builds with the Razor SDK, at `/account/login`, `/account/logout`, `/account/consent`, `/account/two-factor`, `/account/recovery-code`, `/account/enable-two-factor`, `/account/forgot-password`, `/account/reset-password`, `/account/accept-invitation`, `/account/change-password`, `/account/external-login` and `/account/error`. `MapFluentCms` maps them with `MapRazorPages`, on the host's data source so that a host that maps its own pages gets each once, and gives the pages of the area, and only those, the `/account` conventions: the FluentCMS headers with `frame-ancestors`, the `auth` rate limit by client address (which the check of the path before authentication counts) and the `account` surface; the builder it returns passes a host's conventions on to them. No script runs on the pages: they send `Content-Security-Policy: default-src 'none'; style-src 'unsafe-inline'; img-src 'self' https:; base-uri 'none'` unless a page sets its own, and every form carries an antiforgery token, the logout form included, which posts to `/connect/logout` and is checked there (#2854). The page models are public (`FluentCMS.Core.Api.Account`), so a host overrides a page or the layout with its own at the same path, such as `Areas/FluentCms/Pages/Account/Login.cshtml`, on the same model. Branding is the settings area `branding` (`BrandingSettings`: `Name`, `LogoUrl`, `PrimaryColor`, `BackgroundColor`, `TextColor`), read from the instance defaults since the pages belong to no site; a colour is a hex colour and the logo an https URL or a path of the host, so neither can leave the style sheet or the markup. Login takes a user name or an email address, signs in `IdentityConstants.ApplicationScheme` with lockout, gives every refusal the same answer, and returns to a local `ReturnUrl` only, which is the authorize request the endpoint challenged from. The authorize endpoint sends a user who must change the password to `/account/change-password` (`interaction_required` for `prompt=none`), and, for a client with `RequireConsent`, a user who has not allowed it the scopes it asks to `/account/consent` (`consent_required` for `prompt=none`): consent is a permanent OpenIddict authorization that the client's later codes are issued under, so ending that session ends the consent, and refusing returns `access_denied` to the client. A logout without the logout page's token or the session's hint goes to the logout page, which asks and posts the request back; the server then returns to the client's post-logout URI or to the logout page. An error of authorize or logout that cannot go back to the client, such as an unknown client or redirect URI, goes to the error page with the server's code and description (OpenIddict's error passthrough). An exception on a page answers with a plain HTML page that names its `FCMS` code, written without the layout or the database, instead of a problem document. A reset link goes through `IEmailSender` with a token of Identity's Data Protection provider that carries the security stamp, which the reset changes, so it works once, and expires after `cms.Users.PasswordResetLifetime` (an hour); an unknown or deactivated address, or a send that fails (event 9004), gets the same answer and no email. The link, like an invitation's, is built from the public origin when one is set, and otherwise from the request's scheme, host and path base, as the issuer is, which Production allows only with `AllowRequestOrigin` (public origin as built, 4.15). An invitation is accepted on `/account/accept-invitation` with the token of its link, with a password or through a provider (linking as built). The two-factor pages check an authenticator code, with Identity's authenticator provider, or a recovery code; enrolment and enforcement per role are in two-factor as built. The external login page signs in a user linked to the external login, creates one through a provider of `cms.Sso` (admin SSO as built), and otherwise says so. Every step of a sign-in raises `LoginSucceededEvent` or `LoginFailedEvent` from `CmsSignInManager`, so a host's own pages raise them too, with the step (`LoginMethods`), the reason of a refusal (`LoginFailureReasons`) and the client address; a refusal that matched no user keeps no name. They are the activity rows `user.login_succeeded`, whose actor is the user, and `user.login_failed`, whose actor is the anonymous principal, of no site, with the `Admin` origin and the address in `ip`. No new codes.

**Admin users.** Invitation by email (`cms_invitation` with hashed token and expiry), password policy from options, lockout, forced reset flag, deactivate, session list from OpenIddict authorizations with revoke. Password hashing uses Identity's PBKDF2 hasher behind the replaceable `IPasswordHasher<CmsUser>`; Argon2id was considered and deferred to an optional package if demand appears (Q3, which amends row 4.2 of the specification).

**Admin users as built.** `CmsUser` in `cms_user`, on Identity's EF stores for `cms_user_claim`, `cms_user_login` and `cms_user_token`. There is no `CmsRole`: a user holds roles of `cms_role` through `cms_user_role`, by id through `CmsUserManager` (instance-wide or of a site), or by name through Identity among the instance-wide roles only, since a name is unique only within its site. Roles stay with the authorization model, so there is no role store and no role claims, and no `cms_role_claim`. Password policy and lockout come from `cms.Users`, bound from `FluentCms:Users` (`FCMS3001` names a bad key), which keeps Identity's defaults with a minimum of 12 characters; an email address belongs to one user. A user is deactivated (`deactivated_at` and a new security stamp), never deleted: it cannot sign in (`CanSignInAsync`) and holds no roles. A password change clears `must_change_password`. An invitation is 32 random bytes sent through `IEmailSender` and stored as SHA-256; it lasts 7 days by default (`cms.Users.InvitationLifetime`), is used once, ends when a new one goes to the same address or when the user who sent it is deactivated, and becomes the user with its roles in one transaction. Whether the sender may give those roles is checked by `InvitationService` (4.10). `IEmailSender` defaults to a sender that writes to the log in Development and refuses to send anywhere else, unless `FluentCms:Email:Smtp:Host` is set: then every environment sends through SMTP with MailKit (`EmailOptions`: `Host`, `Port` 587, `Security` `StartTls` by default, `SslOnConnect` or `None`, `UserName`, `Password` and `From`, which must parse as a mailbox; `FCMS1101` otherwise), a connection per message. User created, updated (with the names of what changed, `UserChanges`; a rehash at sign-in is none), deactivated and role changed are events staged in the outbox with the change, and `user.*` activity rows of no site. Codes `FCMS3001` to `FCMS3009`.

**The last Super Admin as built.** At least one active user always holds `*` with no constraints through an instance-wide role, which is what Super Admin holds, so that the instance can be administered without editing the database (#2850). The code review of 7 Nov 2026 asked whether this was intended or whether lockout recovery belonged to the command line tool and the system principal; it is intended (#2847, decision 5). `SuperAdminGuard` (`FluentCMS.Core.Authorization`) refuses a change that would leave no such user with `FCMS3052` (409), whoever asks, a Management API key or a service account included: deactivating a user and taking a role from it (`UserAdministration`), the roles that follow a provider's groups (admin SSO as built), and a role edit that drops the grant (4.8). The change and its events roll back, and the refused attempt is the activity row `user.change_refused` or `role.change_refused` of no site, with the `change` (`deactivate`, `remove_role`, `follow_groups` or `update`) and the `code`. Each of these changes takes the `IDistributedLock` named `fluentcms:super-admins` (4.15) before it opens a transaction, waiting up to 45 seconds. A change of a user counts the holders before and after its write inside its identity transaction, and a role edit counts, before it saves, whether anyone keeps the grant through another role. So two changes at once, on one instance or on several, run one after the other on every provider, not only on SQLite, which serialises writers anyway, and the second finds no other holder. A change that adds a holder takes no lock. An invitation can carry Super Admin (4.10), and since it only adds a holder it is unchanged. Users are not deleted (`FCMS3007`), and a role is deleted only while no user holds it (4.10), so neither can take the grant away. `CmsUserManager` does not check, so host code that calls it directly keeps a holder itself.

**Two-factor.** TOTP through Identity's authenticator support, recovery codes, enforcement per role: a role flagged `RequireTwoFactor` makes the authorize endpoint refuse a session without an `mfa` authentication method reference.

**Two-factor as built.** `TwoFactorService` (`FluentCMS.Core.Identity`) runs it on Identity's authenticator provider (RFC 6238: SHA-1, 30-second steps, six digits), which keeps the key and the recovery codes in `cms_user_token`, unhashed, as Identity does. A user enrols a key, which waits until a code of it turns two-factor on; setting a waiting key keeps the security stamp, so the user's sessions go on, and a new enrolment replaces it. Turning two-factor on returns ten recovery codes, shown once, and gives a new security stamp, which ends the user's codes and refresh tokens, so the next sign-in asks for the second factor. A recovery code signs in once and is then spent. Replacing the recovery codes needs an authenticator code, and turning two-factor off needs an authenticator or a recovery code, so a stolen access token alone cannot undo the second factor; turning it off removes the key and the codes and gives a new stamp. A wrong code counts as a failed sign-in towards the lockout of `cms.Users`. Two-factor is on when the flag is set and a key exists, which is when Identity's sign-in asks for it; the flag alone counts as off. `cms_role.require_two_factor` is the flag, `requireTwoFactor` on the `/roles` routes (4.10). A user who holds a flagged role, instance-wide or of a site, needs the authentication method reference `mfa` on the Identity cookie, which Identity writes after the authenticator or a recovery code, and `pwd` after a password alone. Without it, the authorize endpoint signs out a user who has two-factor and challenges again (`login_required` for `prompt=none`), and sends one who has none to `/account/enable-two-factor` (`interaction_required` for `prompt=none`). That page enrols a key, turns two-factor on with a code of it, shows the recovery codes once and signs the cookie in again with `mfa`, since the user has just given both factors. The check comes before the forced password change, and the token endpoint refuses a code or refresh token without `mfa` of such a user (`invalid_grant`), so a session from before the flag ends at its next refresh. **Remembered devices.** The two-factor page's "Do not ask again on this device" (Identity's `TwoFactorRememberMe` cookie) skips the second factor and signs in with `pwd`, so a remembered device does not satisfy `RequireTwoFactor`: `CmsSignInManager.IsTwoFactorClientRememberedAsync` answers false for a user who holds a flagged role, who is therefore asked for the second factor at every sign-in, and the page does not offer that user the option. `amr` stays accurate this way. An external sign-in carries no `amr`, so a flagged role asks for the local second factor after it too, unless its provider is trusted for its own `mfa` (admin SSO as built). Identity's stamp check rebuilds the cookie's principal every half hour, and keeps `amr` through `SecurityStampValidatorOptions.OnRefreshingPrincipal`. The authorize endpoint copies `amr` from the cookie into the code, and the code and refresh tokens carry it into the access and identity tokens. **Resetting another user's two-factor.** A user who lost the app and every recovery code, or whose role requires two-factor and who cannot finish enrolling, is reset by an administrator with `POST /users/{id}/two-factor/reset` (4.10, #2851). `UserAdministration.ResetTwoFactorAsync` needs `users.manage` for the instance and every grant of the user's roles, as any change of a user does (`FCMS3103`), and a caller that is a user whose access token carries the `amr` `mfa` (`FCMS3064`, 403). A Management API key or a service account has no second factor and is refused, and so is a user signed in with a password alone. The request principal carries the token's `amr` values as the claim `amr` for this check. A user does not reset its own two-factor (`FCMS3049`): it turns it off with a code through `/me/two-factor/disable`. A user with neither the flag nor a key has nothing to reset (`FCMS3061`). The reset removes the key, the recovery codes and the flag with a new security stamp in one save, which ends the user's codes, refresh tokens and cookie, and then revokes every session of the user through `UserSessions`, so its access tokens stop at once too. A role that requires two-factor then sends the user to `/account/enable-two-factor` at its next sign-in. The reset raises `UserTwoFactorResetEvent`, staged in the outbox with the change, which is the activity row `user.two_factor_reset` of no site with the administrator as its actor; the store's `user.updated` row names `twoFactor` too when the flag was on. Codes `FCMS3060` to `FCMS3064`.

**Admin SSO.** External OIDC providers (Entra ID, Okta, Keycloak and generic OIDC are the same handler with presets) configured in the host configuration and shown read-only in the admin in the first release (runtime editing is a Should). Federation happens on the hosted login page: auto-provisioning, default role, group claim to role mapping, optional local-login disable.

**Admin SSO as built.** `cms.Sso`, bound from `FluentCms:Sso` (`FCMS3070` names a bad key), lists the providers under `Providers:{name}`. A name is 1 to 64 letters, digits, `-` or `_`, and is the provider's authentication scheme, its login provider in `cms_user_login` and its callback path, `/account/sso/{name}`. Every provider is ASP.NET Core's OpenID Connect handler, added to the authentication options when the host starts. `Preset` (`Oidc`, `EntraId`, `Okta` or `Keycloak`) only builds the authority and names the button: from `TenantId` (`https://login.microsoftonline.com/{tenant}/v2.0`; `common`, `organizations` and `consumers` are refused, since their tokens come from any tenant), from `Domain` and `AuthorizationServerId` (`https://{domain}`, or `https://{domain}/oauth2/{id}`), or from `BaseUrl` and `Realm` (`{base}/realms/{realm}`). `Authority` overrides it, and generic OIDC requires it. The handler runs the code flow with PKCE, with `ClientSecret` when one is set, asks for `openid profile email` and the `Scopes` listed, takes the answer as a form post, and signs into Identity's external cookie with the token's claims under their own names; a host tunes a provider with `services.Configure<OpenIdConnectOptions>(name, ...)`. The login page offers a button per provider, and the external login page asks `SsoSignIn`. A user linked to the login signs in. Someone with no user gets one when `AutoProvision` is on (the default), named and addressed by the `EmailClaim` (`email`), with no password, linked in `cms_user_login` and holding `DefaultRole` and the mapped roles, in one transaction, so the user created and role changed events are staged with it (event 9040). A role is named by its id or by the exact name of an instance-wide role; one that does not exist is not given (`FCMS3071`, event 9042). `RoleMappings` is a list of `Group` and `Role`, matched exactly against the values of `GroupsClaim` (`groups`). No group gives a role by its name, so Super Admin is given only by a mapping or a default role that names it. At every later sign-in of a user who is neither deactivated nor locked out, the mapped roles follow the token's groups: a mapped role whose group is gone is taken away, and the default role and the roles the provider does not map stay. A token with no groups claim at all takes the mapped roles away too, and logs event 9044, since Entra ID's group overage or a missing scope looks the same. Groups that would take Super Admin from its last active holder change no role and refuse the sign-in (`not_allowed`, event 9045, `user.change_refused` with `follow_groups`), since the provider no longer vouches for that user and the instance keeps its last Super Admin (admin users as built). A token without an email address, or that does not say `email_verified` is true while the provider's `RequireVerifiedEmail` is on (the default; off, only a false one is refused) (`FCMS3072`), or whose address belongs to a user not linked to the provider (`FCMS3073`), creates no user, and a creation that fails (`FCMS3074`, with Identity's codes only) leaves none, so an address is never taken over unless the provider links existing users (linking as built). Those refusals log event 9043 and raise `LoginFailedEvent` (`external`, `not_allowed`, no user). The provider's remote sign-out path, `/account/sso/{name}/signout`, signs out the Identity application cookie (`SignOutScheme`), so an IdP-initiated logout ends the admin session (#2854); the cookie holds no session id of the provider for the handler to match, so only a sign-in made through that provider, which the cookie's authentication method names and the stamp check keeps, is ended, and another is left as it is. A provider's error, or a callback that belongs to no sign-in started here, goes back to the login page as a failed sign-in (`invalid_credentials`, event 9041) rather than to an error page. **The provider's `amr`.** It counts only from a provider with `TrustMfaClaim`, which is off by default: a token of such a provider whose `amr` holds `mfa` signs in with `amr=mfa` and without the local second factor. Any other external sign-in carries no `amr`, so a role with `RequireTwoFactor` asks for the local second factor after the provider, or sends the user to `/account/enable-two-factor`, as two-factor as built says. `DisableLocalLogin`, which needs a provider, hides the password form and the forgotten-password link, and `CmsSignInManager` refuses every password sign-in (`not_allowed`). A user created by a provider has no password; with local login on, a reset link gives it one. With local login off, an invitation is accepted through a provider (linking as built). Providers come from configuration only: runtime editing and a read-only list in the admin are not built. Codes `FCMS3070` to `FCMS3074`.

**Linking external logins as built.** The page that accepts an invitation offers a button per provider of `cms.Sso` next to the password form, and with `DisableLocalLogin` the providers only, since `InvitationService` then refuses a password (`FCMS3093`). The provider comes back to that page, and `SsoSignIn` accepts the invitation with the external login when the address of the provider's `EmailClaim` is the invitation's, as Identity normalizes addresses, and the token does not say `email_verified` is false (`FCMS3072`); another address is refused (`FCMS3092`) and the invitation stays pending. The user is named by the invitation's address, has no password, holds the invitation's roles and is linked in the transaction that uses the invitation up; it then signs in as at any sign-in through the provider, so the mapped roles follow its groups and `TrustMfaClaim` and `RequireTwoFactor` apply as before, and the default role is not given. An error or a cancel at the provider returns to the invitation. `LinkExistingUsers` on a provider, off by default, links at its first sign-in through the provider the user whose address the token carries and who has no login of that provider, only when the token says `email_verified` is true, so a provider that sends no such claim links no one; a user who cannot sign in now is not linked, and without the option or the claim the address is refused as before. An invitation needs no explicit `email_verified` because its link already showed that the person reads the mailbox, and an existing user has no such proof. Linking and removing a login raise `UserLoginLinkedEvent` and `UserLoginUnlinkedEvent` from `CmsUserStore` with the change, the activity rows `user.login_linked` and `user.login_unlinked` with the provider. `GET /users/{id}/logins` (`users.manage`) and `GET /me/logins` list `{ "provider", "providerKey", "displayName" }`, and `DELETE /users/{id}/logins/{provider}` (with every grant of the user's roles too) and `DELETE /me/logins/{provider}` remove the user's logins of that provider (`FCMS3091` when it has none) with a new security stamp, which ends the user's sessions. The last way to sign in is not removed (`FCMS3090`, 409): a login counts while its provider is offered on the login page, and a password while local login is on. With a public origin (4.15), the `redirect_uri` a provider is given, and the code is redeemed with, is built from it in `OnRedirectToIdentityProvider`. Codes `FCMS3090` to `FCMS3093`; event ids 9060 to 9062.

**API keys.** `cms_api_key (id, name, class, key_prefix, key_hash, role_id, site_ids json, type_names json, expires_at, last_used_at, rate_limit, revoked_at, created_by)`. Classes: `delivery` (read published), `preview` (read drafts too), `management` (whatever the bound role allows). Format `fcms_dk_...`, `fcms_pk_...`, `fcms_mk_...` with 32 random bytes, shown once, stored as SHA-256. Sent as `Authorization: ApiKey <key>` or `X-Api-Key`. A dedicated authentication handler produces a principal with the role's claims and the key class as scope.

**API keys as built.** `cms_api_key` also has `created_at`. A key is stored with its SHA-256 and its first 16 characters, which find the row; the hash is compared in constant time. The key is returned once, by the create call, and no event, activity row or log holds it or its hash. A key is bound to one role, and to sites and content types when the lists are given; an empty list is refused (`FCMS3020`), since a key must not read it as every site. A role is not deleted while a key that is not revoked uses it (4.10). The `ApiKey` scheme reads `Authorization: ApiKey <key>` or `X-Api-Key` and puts these claims on the request: `sub` (key id), `name`, `fcms_kind` (`ApiKey`), `roles` (the role id), `scope` (the class), and `fcms_sites`, `fcms_types` and `fcms_rate_limit` when the key has them. A wrong, expired or revoked key fails authentication with `FCMS3016` alike, so the answer does not tell that a secret was ever a key, and so does a request that carries a key and any other credential, two keys included (`FCMS3019`). The reason stays on the server (#2854): each refusal logs event 9010 (`ApiKeyRefused`, Information) with its code (`FCMS3016`, `FCMS3017` expired, `FCMS3018` revoked or `FCMS3019`), the client address and the id of a key that was found, never the key, and adds one to the counter `fluentcms.api_keys.refused` by `fluentcms.code`. It writes no activity row: a database write per refused key would let a flood of keys write as fast as it is sent, and the rate limit before authentication already bounds it. The `FluentCms` scheme, which the FluentCMS endpoints authenticate with, takes a request with a key to the `ApiKey` scheme and any other to the bearer scheme the authorization server registers, and leaves it anonymous when a host has none. It never falls back to the host's default scheme, which may be a cookie and would open the APIs to cross-site requests, and a scheme that forwards back to it gets no result instead of recursing. `last_used_at` is written at most once every five minutes. The binding is enforced by the evaluator, not only over HTTP: a principal with `fcms_sites` reaches only operations in those sites, so it writes no global type, and one with `fcms_types` holds only the content permissions of those types; a claim that names nothing usable reaches nothing. The evaluator reads a key's role from `cms_api_key` on each request, so a revoked or expired key holds no role at once. A schedule made by a key checks the key as it is when the job runs, its whole binding read again, so a revoked, expired, deleted or rebound key publishes nothing and the schedule ends `failed`. Expiry can be brought forward, never extended, by a conditional update. Key created, key revoked and key expiry changed are events, staged in the outbox with the change, and `api_key.*` activity rows of no site. `SecretScrubber` masks an `ApiKey` credential and anything shaped like a key. Codes `FCMS3010` to `FCMS3020`.

**Members.** A separate pool (decision 33): `cms_member*` tables with their own `UserManager<CmsMember>`, `cms_member_group` and membership table. REST endpoints under `/api/members/v1`: `register`, `verify-email`, `resend-verification`, `forgot-password`, `reset-password`, `me`. Tokens come from OpenIddict with the members audience; login runs through the hosted pages (code with PKCE) and, when `Members:AllowDirectLogin` is on, through a custom `member_password` grant for first-party apps (Q4). External providers for members: Google and Microsoft (in-box handlers), GitHub and Apple (Q10), generic OIDC. Member groups appear in row rules and public access.

**Security defaults.** Closed by default: no anonymous Delivery access until the Public role grants it; CORS allow-list with no wildcard; secure cookie flags; strict rate limit on `/connect/*` and `/api/members/v1/*`; secrets only from configuration providers; Production refuses to start with a generated signing key (`FCMS1102`), without a public origin (`FCMS1105`) or with the Data Protection ring unencrypted (`FCMS1106`), unless `Security:AllowGeneratedSigningKey`, `Security:AllowRequestOrigin` or `Security:AllowUnencryptedKeyRing` is set for it. The last two were report lines until the code review of 7 Nov 2026, which made them refusals like the first (#2847, decision 1; #2848, #2849): an unset origin lets a forged `Host` receive a real user's reset link, and an unencrypted ring lets a reader of the database forge the sign-in cookie. A setting accepted by its opt-in stays in the startup report (4.15), and other environments start as before.

### 4.8 Authorization

Rows 4.3 to 4.8, 4.12, 4.17, 8.14; decisions 31, 32.

**Permission catalogue.** Core permissions (`users.manage`, `roles.manage`, `apikeys.manage`, `sites.manage`, `settings.{area}.manage`, `jobs.manage`, `activity.read`, `schema.read`, `plugins.manage`, `transfer.manage`, `system.read`), generated per type (`content.{type}.create|read|update|delete|publish|unpublish|restore|purge`), and plugin-declared ones through `IPermissionProvider`. All stored in `cms_role_permission (role_id, permission, constraints json)`.

**Constraints.** Additive only, no deny rules. A grant may be constrained to sites, locales, a field allow-list (`fields: ["title", "slug"]`, null means all), a row rule name and a plugin's resources (`resources`, such as media folders, [ADR 0004](../decisions/0004-folder-limited-media-grants.md)). A grant with `resources` counts only in `IPermissionChecker.ResourcesAsync`, which answers null when a grant with no resource limit holds the permission and otherwise the union of the resources of the grants that hold it; every other check, core's and content's included, leaves it out, so `*` limited to resources gives no core or content permission. A principal's effective permission set is the union of its roles' grants. Field lists and row rules are tied together where the union would otherwise give access no grant did: a write is decided per grant against the entry, and until responses can project per row, the rows of a read stay the union while a field is readable only if a grant with no row rule allows it or every applying grant does.

**Global types.** A write to a type with `ContentScope.Global` (create, update, patch, delete, restore) needs a grant with no site limit, from a role with no `site_id`, because a global entry is shared by every site. Reads are unchanged. The seeded instance-wide roles are unaffected.

**Shared fields.** A non-localizable field of a localized type is shared by all locales, so writing it needs a grant with no locale limit, or one that covers every locale of the entry, including a locale being added. Two grants that cover the locales only together do not count. Otherwise the field is unwritable and keeps its value.

**Roles.** `cms_role` has an optional `site_id` (null is instance-wide). Built-ins seeded by the first migration: Super Admin (everything), Editor, Author (own entries row rule), Public (the anonymous principal per site, which is how public access configuration is stored), Member (default for the member pool).

**Last Super Admin.** The instance always keeps one active user who holds `*` with no constraints through an instance-wide role (#2847, decision 5; #2850). Super Admin's own grants do not change (`FCMS3043`), and an edit of another instance-wide role that drops that grant is refused with `FCMS3052` when no other active user would hold it. Deactivating a user and taking roles from it follow the same rule (admin users as built, 4.7).

**Row rules.** Registered in code with a name and used by roles as data:

```csharp
cms.Authorization.AddRowRule<Article>("own-entries",
    ctx => article => article.CreatedBy == ctx.Principal.Id);
cms.Authorization.AddRowRule<Article>("region",
    ctx => article => article.Region == ctx.Principal.Claim("region"));
```

The evaluator compiles the predicate into the list query and checks it on single-entry operations. JSON-authored rules are a Should (decision 32).

**Evaluator.** `IAuthorizationEvaluator` caches the effective permission set per principal for the request; a `permissions_version` stamp on roles invalidates it across instances. Scopes narrow but never widen: a token with `draft` may read drafts if its roles allow; `management` tokens are bounded by roles. The set keeps its answers about content (the grants behind an operation, the readable fields and the row rule) per type, operation, site and locale for the request, so filtering the fields of a list goes through the grants once per type, not once per entry and field. A grant's pattern is split once, when its role's grants are loaded into the cache across requests. `RoleService.DeleteAsync` takes a deleted role out of that cache on its instance; another instance keeps the entry, which no one asks for again, until it restarts, bounded by the number of roles (#2854). Without an operation nothing is answered (2.3).

**Role stamps on every request as built.** The stamps of the caller's roles are read on every request, in one query, and are what carries a role change to every instance: the grants are kept per role and stamp, and the role an anonymous caller holds, its site's own Public role or else the instance-wide one, is kept per site with the stamp it had when it was found. A warm anonymous request reads that one stamp; a stamp that moved, or a role that is gone, finds the Public role again in the query that reads the stamps. An API key's role comes from the row the authentication handler read and checked for the request (it is kept in `HttpContext.Items` until the request ends, and its expiry is checked again), so the key is read once per request; a job, which has no request, reads the key again, so a key revoked since the job was scheduled holds no role (#2857).

**Introspection and MCP.** Both call the evaluator to drop types and fields the principal cannot read, so generated admin forms and agent tools match permissions (4.17).

### 4.9 Activity log

Row 4.14; the backend half of 3.26 and the data for 4.20 (Should).

`cms_activity (id v7, site_id, occurred_at, actor_type, actor_id, actor_name, origin, client_id, action, target_type, target_id, target_label, locale, details json, correlation_id, ip)`. Written by event subscribers, so plugins get logged for free when they raise events. Retention job, default 30 days, configurable per site. `GET /api/management/v1/activity` with filters on actor, origin, action, target, date range; permission `activity.read`.

**As built.** Every event of the catalogue that exists has an outbox handler named `activity.{action}`, registered with `cms.Events.AddActivity<TEvent>(name, describe)`, which a plugin calls for its own events. The row says what the event says: actor, origin, client id, correlation id, site and time come from its `EventSource`, not from the job that delivers it, and the id of the row is the delivery key, so a repeated delivery writes nothing. A row names its target by type and id and never by a field of content, and text is passed through `SecretScrubber` and cut to its column; `IActivityLog` is the direct writer for what no event covers: it writes on a context of its own, refuses inside a transaction, and stores actions in lower case; `ip` is filled only by a writer that knows it. `IActivityQuery` reads the site of the current operation, newest first, filtered by actor, origin, action (exact, or a prefix ending in `*`), target and date range (`From` inclusive, `To` exclusive), and leaves the permission check to the endpoint. Rows of no site (the failures of jobs, whose text can name internals of the host) are returned only with `IncludeInstance`, which is for instance-wide readers only: the query cannot tell a reader of one site from a reader of the instance, so the Management endpoint must check an instance-level permission before it sets it. Retention is `cms.Activity.Retention` (30 days) with `SiteRetention` by site slug until sites have settings of their own; the instance-wide rows use the default. The job is `activity.retention`, daily at 03:30 UTC, deleting in batches of `PruneBatchSize` (at most 1000), served by an index on `occurred_at`. `cms.Activity.Enabled = false` stops new rows and stores no schedule, leaves a stored one alone (the job returns at once while the log is off), and drops the rows already waiting in the outbox. `role.updated` has the details `added` and `removed`, the grants the update gave and took, each its permission followed by its constraints as JSON when it has any, separated by semicolons and cut to 256 characters like every detail (#2854). A refused API key writes no row (4.7).

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

**As built.** A request bridge in `FluentCMS.Core.Api.Common`, shared with the Management API, sets the `OperationContext`. It reads the principal from the `FluentCms` scheme that `AddFluentCms()` registers, which goes to the API key handler when a key is sent and otherwise to the configured bearer scheme, never to the host's default. A host without that scheme uses the request's user. A request without one is the anonymous principal. An authenticated principal without a usable `fcms_kind` or `sub`, or one whose credentials the scheme refuses, is 401 (`FCMS3102`), never anonymous. The site follows 4.6. A key bound to one site reads it. A key bound to several reads the one named, or the default site when it is among them, and is otherwise refused (`FCMS3122`, 400). A named site outside the binding is 403 (`FCMS3121`) whether it exists or not, and an `X-Cms-Site` header and a `site` parameter that name different sites are refused (`FCMS2201`). Delivery reads drafts only with the `preview` or `draft` scope, even for a principal whose `management` scope lets the engine read them. An unknown type and one the principal cannot read are the same 404 (`FCMS3104` on `type`). A problem's `errors[].path` names the parameter the client sent (`filter[publishedOn][gt]`, `limit`, or `field` and `value` on `by/`), not the field the engine reports. `by/{field}` needs a unique field (`FCMS3123`), which is checked before the query runs, so a field that is not unique costs no query; a field the principal may not read is refused as the unknown field the query would report (`FCMS2202`), so that no field is shown to exist by its error. Under `locale=all` an entry is `{ "id", "locales": { code: entry } }`, and a type that is not localized has one entry, keyed by the empty string, as its `locale` field is.

**Caching as built.** The `ETag` is a hash of the body, which holds the version and locale of every entry, populated ones included, and follows from the query, so it also changes when a populated target or what the caller may read changes. There is no `Last-Modified`, and only `If-None-Match` decides a 304: no date in the body moves when an entry leaves a list, a populated target is unpublished or a role changes what the caller may read, so a date would let a client or a CDN keep a stale copy. `If-Modified-Since` is ignored, as RFC 9110 (13.1.3) asks of a resource with no modification date, and the Delivery policy takes it off the request before the output cache looks, which would otherwise answer it by the date it stored the copy; such a request is answered in full, from the cache when it holds a copy, rather than running the endpoint each time. `FluentCms:Delivery:Cache:CacheControl` is sent as written to a published read without credentials, in its `private` form to one with a key or a token (a CDN keys by URL, not by credentials), `no-store` on a draft read, and an error keeps the exception handler's `no-cache,no-store`; responses vary on `Authorization`, `X-Api-Key`, `X-Cms-Site` and `X-Cms-Locale`. The three routes answer `HEAD` as `GET` without the body, `Content-Length` included, and the output cache keeps a `HEAD` apart from its `GET`. `UseFluentCms()` adds the output cache last, after the rate limiter, authentication and authorization, so a policy the host puts on `MapFluentCms()` holds for a cached answer too, and the Delivery group carries its policy: one copy for every request without credentials and one per API key, keyed by a hash of its claims (role, sites, scope), for `OutputCacheDuration` (60 seconds; `OutputCache`, `cms.DeliveryCache` in code, turns it off, and with it the evictions and their event). The key also holds the schema hash, as the OpenAPI document's does, so a store that outlives the process or that instances on another schema share serves none of them a copy made for another schema. The schema is built once per process, so a schema change is a new process: the in-memory store starts empty, and the hash keeps a shared one right during a rolling deployment, where evicting at start would not, since an instance still on the old schema would write old copies again. A bearer token, a draft read, refused credentials and anything but a 200 are not cached, and the other FluentCMS groups say so, so a host's base policy leaves them alone. Response compression goes before `UseFluentCms()`: the cache then keeps the body the endpoint wrote and compression encodes each answer for its request, so the key need not vary on `Accept-Encoding`. Compression inside the cache would keep a body encoded for one client, so a response that has a `Content-Encoding` while no compression middleware wraps the cache is not kept, by the Delivery and the OpenAPI policies alike. A cached read is tagged `site:{id}`, `type:{site}:{type}` (`type:global:{type}` for a global type, whose entries every site serves) and `entry:{id}` for every entry in the body, populated ones and references included. An outbox handler evicts `entry:{id}` and the type's tag on `EntryPublishedEvent`, `EntryUnpublishedEvent`, `EntryTrashedEvent` and `EntryRestoredEvent`, and `site:{id}` on `SiteUpdatedEvent`, since a site's locales decide its fallbacks, and raises `CacheInvalidatedEvent` with those tags. A change to a role or its grants evicts `site:{id}` the same way, because the Public role decides what the shared copy holds and a key's role what its own holds: `RoleVersionInterceptor`, which raises the role's permissions version in the same `SaveChanges`, stages an internal `RolePermissionsChangedEvent` in the outbox there, with the role's site, or every site for a role of the instance, so that `RoleService` and anything else that writes roles through `CmsDbContext` evict with the commit, and a crash after it does not leave the copies. A newly published relation target shows when the copy expires. The store is the provider slot `IOutputCacheStore` (4.13), the framework's in-memory store by default, which the startup report lists; an eviction reaches only the store of the instance that delivers the event, so with several instances on the in-memory store the others serve their copies until they expire, and a shared store, such as Redis, evicts for all. Lookups count in `fluentcms.cache.requests` as cache `delivery`. This answers items 6, 7, 13 and 15 of [#2860](https://github.com/fluentcms/FluentCMS/issues/2860) of the review of 7 Nov 2026.

**Round trips as built.** Before its content queries a request used to read the default site or the named one, a site's default locale and its locales (once per lookup), the site's Public role by name or an API key's row a second time, and the role stamps. Now the site and its locales come from `SiteCache` (4.6), the Public role and the grants from the cache keyed by the stamps (4.8), and a key's role from the row its authentication read. A warm anonymous Delivery read of a public entry sends the stamp of the Public role and its content queries only (for a type with a relation: the entry with its document, and its references), three queries where it sent nine, and one with an API key adds the key's lookup by the handler, four where it sent ten (`DeliveryRoundTripTests`). The stamp read stays because it is what makes a role change visible on the next request on every instance; a change to an existing site or its locales reaches another instance within 30 seconds (#2857).

**Plugin Delivery groups as built.** A plugin mounts a group under `/api/delivery/v1` with `MapDelivery(prefix)` (4.13), beside `MapManagement`. Its endpoints get what core's Delivery group has, applied by the same code: the Delivery surface, the output cache policy, rate limit and CORS policy (`GET` and `HEAD`), the FluentCMS authentication with an anonymous caller read as its site's Public role, the request bridge's site and locale, and a place in the OpenAPI document. A copy of an answer is tagged with its site only, so it goes when the site is updated, a role changes or the plugin is disabled in the site (an outbox handler evicts `site:{id}` on `PluginDisabledEvent`, since a copy is served before the plugin's per-site gate); an endpoint whose answer changes on anything else opts out with `CacheOutput(policy => policy.NoCache())`, and sets its own `Cache-Control`. A prefix whose first segment is a route parameter or the name of a content type would take core's `/{type}` routes and stops the host (`FCMS1524`), and `MapGroup` still refuses a raw prefix under a group of core. The media plugin's route of the table above is `GET /api/delivery/v1/media/{id}`, mapped with `MapDelivery("/media")`. This answers [#2888](https://github.com/fluentcms/FluentCMS/issues/2888) of the review of 7 Nov 2026.

**Management API.** Under `/api/management/v1`, bearer or management key:

| Group | Routes |
| --- | --- |
| content | `/content/{type}` list and create; `/content/{type}/{id}` get, patch, delete; `/publish`, `/unpublish`, `/discard`, `/schedule`; `/versions`, `/versions/{n}/restore`, `/versions/{a}/diff/{b}`; `/content/{type}/bulk`; `/content/{type}/trash` list, `/trash/{id}/restore`, `/trash/{id}` purge |
| schema | `/schema`, `/schema/types/{type}`, `/schema/snapshot` |
| identity | `/users`, `/users/{id}/sessions`, `/users/{id}/two-factor/reset`, `/invitations`, `/roles`, `/permissions` (catalogue), `/api-keys`, `/members`, `/member-groups` |
| sites | `/sites`, `/sites/{id}/locales` |
| settings | `/settings/{area}` get and put, with the JSON schema of the settings type |
| jobs | `/jobs`, `/jobs/{id}`, `/jobs/{id}/retry`, `/jobs/{id}/cancel`, `/jobs/dead-letter` |
| activity | `/activity` |
| plugins | `/plugins`, `/plugins/{name}/sites/{site}` enable and disable |
| transfer | `/transfer/export` (job, then download), `/transfer/import` (upload, dry run, apply) |
| system | `/system/diagnostics`, `/system/health` (detailed; `system.read`) |
| me | `/me`, `/me/sessions`, `/me/two-factor` |

**Content group as built.** The routes need a bearer token or a key with the `management` scope: a request that is not signed in is 401 and one without the scope, such as a Delivery key, is 403; what the principal may then do is the content service's to decide, through the request bridge as on Delivery. `GET /content/{type}` and `GET /content/{type}/{id}` take the Delivery query grammar and read the draft unless `status=published` is asked. `POST /content/{type}` creates and answers 201 with `Location`, which keeps the `site` parameter the request named; `PUT /content/{type}/{id}` replaces the draft (`UpdateAsync`) and `PATCH` applies a JSON merge patch, both in the request's locale, where `If-Match: "0"` adds a locale the entry does not have. `DELETE` moves the entry to the trash. `POST .../publish` answers `{ "entry", "warnings" }`, `.../unpublish` and `.../discard` the entry; `POST .../schedule` takes `{ "action": "publish" | "unpublish", "at", "timeZone" }`, where `at` has an offset without a zone and is the wall-clock time there with one, and `DELETE .../schedule/{action}` cancels. `GET .../versions` lists `{ "items" }` newest first, `GET .../versions/{n}` reads one, `POST .../versions/{n}/restore` restores it and `GET .../versions/{a}/diff/{b}` answers `{ "from", "to", "changes" }`, all in one locale. `POST /content/{type}/bulk` takes the list's `filter[...]` parameters in the query string and `{ "operation", "entries", "items", "cap", "patch" }` in the body, and answers `{ "items", "cancelled" }` with an entry's status, id, version, warnings and, for one that failed, the problem the single call would have answered. `GET /content/{type}/trash` lists the trash with the query grammar, `POST /trash/{id}/restore` restores and `DELETE /trash/{id}` purges. A save needs `If-Match` with one tag, `"7"` (`FCMS3130`, 428, when it is missing; `FCMS3131`, 400, for `*`, a list or anything else); publish, discard and version restore take it optionally, with `*` as none. The tag is strong, but a weak one, `W/"7"`, is read by its opaque value: a proxy that compresses responses, such as nginx with `gzip on`, weakens the `ETag` it passes on, and a client that echoes it would be refused on every save. This departs from RFC 9110, whose `If-Match` compares strongly and so never matches a weak tag; the tag names a version rather than the bytes of a representation, so a weakened one still names it. A response that holds a draft gives its version as `ETag`, unless a value came through the fallback chain, which can carry another locale's version, and a conflict is 412 (`FCMS3105`) with the current draft in `current`, in the response shape. A body is a JSON object that names a key once, read before the engine sees it (400 when it cannot be, 415 and `FCMS3107` for another media type), and a key a schedule or bulk body does not use is refused (`FCMS2108`). An unknown type is 404 on every route, and one the principal cannot read is the same 404 on a read; a write leaves it to the content service, since a principal may write without reading. A write and a read of versions take only `site` and `locale` from the query string, bulk its filter too, and `locale=all` is refused where one locale is needed. `IfMatch` in `FluentCMS.Core.Api.Common` is for the other groups that take a version.

**Identity, sites, settings, jobs, activity, plugins and me as built.** These groups, and the system group (4.15), need the `management` scope as the content group does. The routes read the request, and the in-process services check the permission through `ManagementAuthority` (`FluentCMS.Core.Authorization`), so a caller without it is 403 (`FCMS3103`), or 401 when not signed in. A permission for the instance is one that no site limits: a role of a site, a grant limited to sites and a key bound to sites do not hold it. Paged lists take `page` and `pageSize` (50, at most 100) and answer `{ "items", "total", "page", "pageSize" }`, other lists `{ "items" }`. A query parameter a route does not take is refused (`FCMS2201`), and so is a body key (`FCMS2108`). No caller hands out more than it holds: a role, a user's role or a key's role must give nothing beyond the caller's own grants, compared grant by grant with the pattern, sites, locales, fields and row rule (a grant that reaches several sites may be held through a different grant in each).

- Users, with `users.manage` for the instance, since a user belongs to no site: `GET /users` (`search` on user name and email), `GET` and `PATCH /users/{id}` (`userName`, `email`, `mustChangePassword`), `POST /users/{id}/deactivate` and `/reactivate`, `PUT` and `DELETE /users/{id}/roles/{roleId}`, `GET /users/{id}/sessions` and `DELETE /users/{id}/sessions/{sessionId}` through `UserSessions`, `GET /users/{id}/logins` and `DELETE /users/{id}/logins/{provider}` (4.7), and `POST /users/{id}/two-factor/reset`, which answers the user and needs a caller signed in with a second factor (4.7). Changing, deactivating or reactivating a user, ending its session, removing its login or resetting its two-factor needs every grant of the user's roles too, and giving or taking a role every grant of that role. A user does not deactivate itself or reset its own two-factor. Deactivating the last active user who holds `*` with no constraints, or taking from it the role that grants it, is refused (`FCMS3052`, 4.7).
- Invitations: `GET /invitations` lists the pending ones, `POST /invitations` `{ "email", "roleIds" }` sends one linked to `/account/accept-invitation` on the request's origin, and `DELETE /invitations/{id}` revokes; `InvitationService` needs `users.manage` for the instance and every grant of each role named.
- Roles, with `roles.manage` where the role applies (the instance for an instance-wide role, its site otherwise): `GET /roles` and `GET /roles/{id}` (also for `users.manage` and `apikeys.manage`), `POST /roles` `{ "name", "description", "siteId", "requireTwoFactor", "grants": [{ "permission", "constraints" }] }`, `PUT /roles/{id}`, which replaces the name, the description and the grants, and `requireTwoFactor` when the body gives it, and `DELETE /roles/{id}`. A grant names a pattern the catalogue matches, sites (only its own for a role of a site), locales, fields and a registered row rule, and an empty list is refused. What a change adds and what it replaces or deletes must both be within the caller's reach. Built-in roles keep their names and are not deleted, and of Super Admin only `requireTwoFactor` changes (`FCMS3043`). An edit that drops `*` with no constraints from an instance-wide role is refused when it would leave no active user who holds it (`FCMS3052`, 4.8). `RoleService` writes through `CmsDbContext`, whose interceptor raises the permissions version, and deletes a role in one statement only while no user holds it and no key that is not revoked uses it (`FCMS3044`), so the cascade drops no holder without its `UserRoleChangedEvent` and no live key without `api_key.revoked`. `role.created`, `role.updated` and `role.deleted` are activity rows of the role's site. `GET /permissions` lists the catalogue.
- API keys: `GET /api-keys`, `GET /api-keys/{id}`, `POST /api-keys` `{ "name", "class", "roleId", "siteIds", "typeNames", "expiresAt", "rateLimit" }`, which answers 201 with the key and its `secret` once, `DELETE /api-keys/{id}`, which revokes and keeps the row, and `POST /api-keys/{id}/expire` `{ "at" }`. `ApiKeyService` needs `apikeys.manage` for the instance for a key bound to no site and in each of its sites otherwise; a key out of reach is 404 and left out of the list, and a new key's role, as its binding narrows it, holds only what the caller holds, so a key bound to sites mints no key that reaches further.
- Sites, with `sites.manage` for the site: `GET /sites`, `GET` and `PATCH /sites/{id}` (`name`, and `defaultLocale`, an enabled locale of the site; `SiteUpdatedEvent`), and `GET`, `POST` `{ "code", "position", "enabled", "fallbacks" }`, `PATCH` and `DELETE` on `/sites/{id}/locales/{code}`, where a fallback is another locale of the site and `fallbacks: null` is the implicit chain. The default locale stays enabled and is not removed, and a locale with entries, trashed ones included, is disabled rather than removed (`FCMS3136`). `site.locale-added`, `site.locale-updated` and `site.locale-removed` are activity rows of the site. Creating and deleting sites stays with the multi-site plugin.
- Settings: `GET` and `PUT /settings/{area}` for the site of the request and `/settings/{area}/defaults` for the instance defaults, with `settings.{area}.manage` there for both, since the read is what the admin edits. A read answers `{ "area", "siteId", "source", "version", "value", "secretsSet", "schema" }` from the redacted resolve, with the version as `ETag`; a write takes the whole document and needs `If-Match` with the version read (`"0"` for no row), and a row that has moved is `FCMS2808` (412).
- Jobs, with `jobs.manage` for the instance: `GET /jobs` (`status`, `type`, `queue`), `GET /jobs/{id}` with its attempts as `history` and never its payload, `GET /jobs/dead-letter`, `POST /jobs/{id}/retry` for a dead-lettered job (`FCMS3133` otherwise) and `POST /jobs/{id}/cancel` for a pending one (`FCMS3134` otherwise; a running job is left to finish). A cancelled job is dead-lettered with its `JobDeadLetteredEvent` in one transaction, so a schedule it carried closes as for any dead letter, and it can be retried.
- Activity: `GET /activity` with `actorId`, `actorKind`, `origin`, `action` (a trailing `*` for a prefix), `targetType`, `targetId`, and `from` and `to` with an offset, needs `activity.read` in the site of the request, and adds the rows of no site only for `activity.read` for the instance.
- Plugins: `GET /plugins` lists every plugin in plugin order with `name`, `version`, `sdkRange`, `dependsOn`, `enabled` for the site of the request, `contributions` (`fieldTypes`, the `permissions` of its permission providers, the `endpoints` of its groups as `{ "method", "route" }`, and `jobs`) and `sites`, its enablement `{ "siteId", "slug", "enabled" }` in each site where the caller holds `plugins.manage`. Any caller with the `management` scope reads it, because the admin hides a disabled plugin's UI for every user. `PUT /plugins/{name}/sites/{site}` `{ "enabled" }` needs `plugins.manage` for that site and answers `{ "siteId", "slug", "enabled" }`; an unknown plugin is `FCMS1551` and an unknown site `FCMS2301`, both 404 (4.13).
- Me: `GET /me` is the caller, and for a user also its user name, email, `mustChangePassword`, `twoFactorEnabled` and roles; `GET /me/sessions` and `DELETE /me/sessions/{id}` are a user's own sessions. `GET /me/two-factor` is `{ "enabled", "required", "pending", "recoveryCodesLeft" }`, `POST /me/two-factor/enrol` answers `{ "sharedKey", "authenticatorUri" }` (an `otpauth` URI named after the brand of the hosted pages), `POST /me/two-factor/verify` `{ "code" }` turns two-factor on and `POST /me/two-factor/recovery-codes` `{ "code" }` replaces the codes, both answering `{ "recoveryCodes" }`, and `POST /me/two-factor/disable` `{ "code" }` turns it off (4.7); `GET /me/logins` and `DELETE /me/logins/{provider}` are its external logins (4.7); a key or a service account is 403.

Codes `FCMS3040` to `FCMS3052` and `FCMS3132` to `FCMS3137`.

**Schema group as built.** `GET /schema`, `GET /schema/types/{type}` and `GET /schema/snapshot` need the `management` scope and `schema.read` in the site of the request, checked before any type is looked up (403, `FCMS3103`). `SchemaIntrospection` (`FluentCMS.Core.Schema`) then asks the evaluator what the caller may read in the site and locale of the request, which is what `TypeAccess` asks on a content route. A type it may not read is left out of `/schema`, and `/schema/types/{type}` answers it with the same 404 as an unknown type (`FCMS3104` on `type`). A field outside its allow-lists is left out, not blanked, and so is a relation to a type it may not read and the reverse side of a field that went, so that no name of such a type shows. A component or block that only what went used goes too, one that nothing uses stays, and a readable rich text field keeps every block, since an inline block may be of any. A condition or a slug source that reads a field that is left out goes too, so no name of such a field shows either. A condition goes whole, even when only one of its clauses reads such a field, because what is left of it would state a rule other than the one declared, and publishing still enforces a `requiredWhen` as declared. `/schema` answers `{ "types", "components", "blocks" }` and `/schema/types/{type}` one type, as the `SchemaDescription` records of the SDK, which a client can read back; `ISchemaIntrospection` there is the seam for the MCP plugin's schema resources. A type has `kind` and `localized`, and a collection or singleton also `scope`, `unpublishedReferences`, `maxVersions` and `trashRetentionDays`. A field has its flags, `formerNames`, `display`, `defaultValue`, `validation` (`required`, `unique`, the lengths, the range and `pattern`), `visibleWhen` and `requiredWhen` as `{ "operator", "field", "values", "operands" }`, `relation`, and the field type's `settings`, where a blocks field names its union in `blocks` and a select its `options`. Members that are null are left out. `/schema/snapshot` is the canonical `schema.json` text of what the caller sees, with its SHA-256 as `ETag`, so for a caller that reads everything it is the instance's snapshot and schema hash.

**Error model.** RFC 9457 `application/problem+json` on every error: `type` (`urn:fluentcms:error:{code}` until a docs domain exists), `title`, `status`, `detail`, `code` (`FCMS...`), `errors[]` of `{ path, code, message }` for validation, `current` on 412, `traceId`. **As built.** `FluentCMS.Core.Api.Common` maps the engine's exceptions to a status, a code and this body in one place (`ProblemMapper`) and writes it through an `IExceptionHandler` and `IProblemDetailsService` that `AddFluentCms()` registers; the host adds `app.UseExceptionHandler()`. `ErrorCodeRegistry` lists every `FCMS` code once with its status and title, and a test fails when a code in `src` is missing from it. A validation exception is 422 with `errors[]`, or the status its codes share in the registry (400 for query errors, 404, 409 or 412 for the few codes that say so) and `FCMS2100` when its errors have different codes. Access denied is 401 for a request without a signed-in principal and 403 for one with it. A hook's own code is kept, 422 unless the registry says otherwise. A code raised only while the host starts or a job runs is 500, and so is any other exception, with a generic `detail` and `FCMS3199`. The HTTP layer's own codes are `FCMS2100`, `FCMS3101` to `FCMS3105`, `FCMS3107` for a body of another media type (415), `FCMS3110` and `FCMS3111` for the rate limit and the body cap, `FCMS3121` to `FCMS3123` for the request bridge and the Delivery API, and `FCMS3130` and `FCMS3131` for `If-Match`.

**OpenAPI 3.1.** `Microsoft.AspNetCore.OpenApi` with document transformers that add one schema per content type and per block, block unions as `oneOf` with discriminator, the filter grammar as documented parameters, security schemes (`oauth2` authorization code, `apiKey`), examples from the sample pack. Served per deployment at `/openapi/v1.json`; Scalar UI at `/scalar` (dependency approved in Q9).

**OpenAPI as built.** `AddFluentCms()` registers the document `v1` of `Microsoft.AspNetCore.OpenApi` with the Delivery and Management routes only, and the host maps it at `/openapi/v1.json` and Scalar at `/scalar` with `app.MapFluentCmsOpenApi()`, which returns a builder whose conventions, such as an authorization policy, apply to both (Appendix B leaves their authentication to the host). Both are FluentCMS endpoints of the `docs` surface: they carry the security headers, the coarse limit counts them before authentication and the `delivery` policy by client address, and the FluentCMS scheme does not run on them, so a key sent there is never looked up; they are still not held to the path base. The document is built once and kept by the output cache for a day: one copy per scheme, host and path base, which its server follows, or one in all with a public origin, keyed also by the schema hash, so that a store which outlives the process serves no copy made for another schema. The schema is built once per process, so there is no reload to evict on. `UseFluentCms()` runs authorization before the output cache, so a route that needs authorization, by its own requirement or the fallback policy, keeps its copy too and hands it only to a caller the policy lets through. Scalar.AspNetCore serves its bundle and helper beside the page from its embedded assets, so the page's `Content-Security-Policy` allows scripts from its own origin and, by a nonce made per request, the inline script that starts them, and styles from its own origin and inline, which the bundle injects (`default-src 'none'; script-src 'self' 'nonce-…'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self' data:; connect-src 'self'`, the public origin added when it is set, `base-uri 'none'; form-action 'none'; frame-ancestors 'none'`). Scalar's CDN fonts, telemetry and agent chat would leave the origin, so they are off. This answers [#2856](https://github.com/fluentcms/FluentCMS/issues/2856) of the review of 7 Nov 2026. They are mapped in Development, and elsewhere only with `FluentCms:OpenApi:Enabled = true`, because the document names every type and field of the schema to whoever can reach it; this spec did not settle it, so the default is the safer one. The routes come from endpoint metadata. A route under `{type}` carries a `ContentOperation` that says what it reads and answers, and a document transformer writes it once per content type in place of its `{type}` form, so a new content route needs only that metadata; any other route gets a tag from its group, an operation id from its method and path, its security and the problem response. `entry-{type}` holds the system fields as read-only and every field that is not hidden, each from its field type's fragment and nullable unless its storage has an empty value (4.3), a relation also as its populated entry and rich text also as a string. `input-{type}` is what a write sends: the same fields without the system ones, a relation as ids and rich text as a document. No field is required, since a draft may leave any out and `fields` selects. A block is `block-{name}` with `type` as a `const` and an optional `id`, and a block list is the `oneOf` with the `type` discriminator that `BlocksFieldType` writes. The query grammar of 4.4 is shared parameters, and each type has `filter-{type}`, a `deepObject` with the operators of each field it can filter, the fields of its single components as `component.field` and the system fields, and `sort-{type}`, which lists its sort keys. `oauth2` has the authorization code flow, with `x-usePkce` for PKCE, and client credentials, at the document's server, which is `FluentCms:PublicOrigin` when it is set, as the issuer is, and otherwise the request's origin and path base; `apiKey` is the `X-Api-Key` header; a Delivery route also accepts no credential. Examples come from `cms.OpenApi.AddExample<T>()`, which `AddBlogSample()` calls. The schema group answers the SDK's `SchemaDescription` records. The identity, site, settings, job, activity and me groups declare their bodies on each route, `Produces<T>` for the response and `Takes<T>` for a request body, which the route reads key by key, so its record only describes it, with `additionalProperties: false` since the route refuses a key it does not take; `Takes<T>` names no media type, because with one, as `Accepts<T>` gives, routing would refuse another with a bare 415 before the route answers its problem. A record is the service's (`UserInfo`, `SiteInfo`, `ApiKeyInfo` and the like) or, where a route wrote an anonymous object or a JSON tree, one of the same JSON in `AdminBodies`: `ItemList<T>` and `ItemPage<T>` for the lists, and `RoleResponse`, `CreatedApiKeyResponse`, `SettingResponse`, `JobResponse`, `MeResponse` and `RecoveryCodesResponse`. The settings read is `SettingResponse`, whose `value` follows the area's JSON schema under `schema`, and a write takes the document as an object. Enums are camel case names and numbers are numbers whatever the host's `JsonOptions` say, since FluentCMS writes them so and reads a body's numbers only as numbers. The system group answers `HealthReportResponse` at `/system/health`, its checks in order of name with the names of `HealthStatus` as they are, and the SDK's `StartupReport` at `/system/diagnostics`. A route of these groups declares the query parameters it reads with `Query(...)`, with their types, defaults and limits (`page` from 1, `pageSize` from 1 to 100 and 50 by default), and `ManagementQuery` accepts and reads only those beside the bridge's `site` and `locale`, so a parameter read without being declared fails every request to the route. Every route beside content also lists the bridge's `site` and, as `bridge-locale`, its `locale`, which such a route checks against the site's locales and reads nothing by.

**Rate limiting.** ASP.NET Core rate limiting with policies `delivery` (partition by key or IP), `management` (by principal), `auth` (strict, by IP). `RateLimit-*` headers and `Retry-After`. Per-key overrides from `cms_api_key.rate_limit`.

**CORS and headers.** Separate allow-lists for Delivery and Management from configuration and settings; no wildcard by default; security headers middleware (`X-Content-Type-Options`, `Referrer-Policy`, `Content-Security-Policy` with `frame-ancestors` for the hosted pages).

**Request limits.** Body size, page size cap (default 100), populate depth cap, filter node cap, upload caps (media plugin).

**HTTP protections as built.** `app.UseFluentCms()` adds the exception handler, routing, security headers, CORS, a coarse limit per client address, the request limits, authentication, the rate limiter of the endpoints, authorization, the step that drops the body of a `HEAD` and the output cache in that order, and `MapFluentCms()` refuses to run without it (`FCMS1104`). A host does not call `UseAuthorization()` itself; one that still does runs its policies a second time, which changes nothing but the cost, and `AddFluentCms()` registers the authorization services the step needs. The policies are token buckets per caller with limits from `FluentCms:RateLimits` (delivery 600, management 300, auth 20) as the rate per minute: a bucket holds the limit and fills again over a minute, so a quiet caller may send the limit at once and then one request per minute divided by the limit, and a burst at the turn of a minute is not let through twice, as it was with fixed one-minute windows. A bucket is kept as the time it is next full (the generic cell rate algorithm), so one number is its state and no timer runs per caller, and a clock that moves back moves the bucket with it; the coarse limit (`PreAuth`, 6000) runs before authentication on every FluentCMS endpoint, which routing has found by its metadata, so a plugin's routes are covered without registering their prefixes, and on any path under a group of core that no endpoint answers, such as `/connect/token`; `/health` counts in a partition of its own (4.15); a key's `fcms_rate_limit` claim replaces the delivery limit up to `Delivery:MaxKeyLimit`; the caller is read from the `FluentCms` authentication scheme when there is one, and IPv6 addresses are limited by their /64. Responses carry the draft fields `RateLimit-Limit`, `RateLimit-Remaining` (what may be sent now) and `RateLimit-Reset` (seconds until the bucket is full), and a refusal is a 429 problem (`FCMS3110`) with `Retry-After`, the seconds until the next request is let through. The caller is read once per request into an `ApiCaller` when the FluentCMS scheme has run: its kind, `sub`, rate limit claim and, for a key, the hash of its claims that the Delivery output cache keys its copy by, computed on first use; the client address is read once too and shared by the limit before authentication and the endpoint's. A partition is the caller alone, with its limit only when a key's own limit replaces the policy's, so a request builds no partition key or limiter factory of its own. This answers items 8 and 10 of [#2860](https://github.com/fluentcms/FluentCMS/issues/2860). The allow-lists come from configuration only; the settings half is not built. `/connect` answers the Management origins. Both policies allow the `X-Cms-Site` and `X-Cms-Locale` headers and expose `ETag`, `Location`, `Retry-After` and the `RateLimit-*` fields. A FluentCMS response other than Delivery's that sets no `Cache-Control` gets `no-store`, so a key's secret, recovery codes, `/me` and the diagnostics are kept by no browser or proxy. The body cap is `FluentCms:Limits:MaxBodySize` (2 MiB, `FCMS3111` on 413). It runs before authentication, where the authorization server reads the body of a token, introspection or revocation request, and holds a path under a group of core that no endpoint answers by its prefix, as the coarse limit does. The page size, depth and filter caps stay the engine's. Details are in `docs/host-configuration.md`.

### 4.11 Events and hooks

Rows 8.4, 8.16, 6.10.

**Event bus.** `IEventBus.PublishAsync<TEvent>` with `IEventHandler<TEvent>` subscribers, in-process. Events raised during a transaction are buffered and dispatched after commit, in `[HookOrder]` order; a failing handler is logged and does not affect the write. Handlers that must not be lost (webhooks, activity log, cache invalidation) subscribe through the outbox: the event is also written to `cms_outbox` in the same transaction and a runner job drains it, so a crash between commit and dispatch loses nothing.

**In-process handlers, as built.** The handlers of `IEventHandler<T>` stay awaited after the commit, because a caller that reads after a write expects their side effects, such as a cache eviction, to have run; moving them behind the response is declined. Each one is bounded instead. It runs off the caller's thread, so that one that blocks before its first await is cut off too, and the caller waits for it at most `FluentCms:Events:Handlers:Timeout` (default 5 seconds, the default of `OutboxOptions.JobEventTimeout`), with the `WaitAsync(timeout)` that bounds the handlers of a job event. Its token is the host's shutdown linked with that timeout, so a cooperative handler stops when it passes; one that does not is logged as abandoned (event 9615), left to end on its own, and the next handler runs. The handlers of an event run in a scope the dispatch creates, as the handlers of a job event do, never in the caller's, so a handler left running shares no `DbContext` or other scoped service with the request, which goes on to read its response. The abandoned handler keeps its scope until it ends, and the handlers after it run in a fresh one. Whatever it does after the caller went on, such as using the request's services through `HttpContext.RequestServices` once the request has ended, is logged (9617) and never thrown, so it neither reaches the caller nor surfaces as an unobserved task exception; a scope that fails while it is disposed is logged too (9618). The bound is per handler, so an event with several slow handlers waits up to the bound for each. A handler that ends after `FluentCms:Events:Handlers:SlowThreshold` (default 1 second) is logged as slow (9616), and every run is recorded in `fluentcms.event.handler.duration` (histogram, seconds) by `fluentcms.operation` (the event type), `fluentcms.handler` (the handler's class) and `fluentcms.outcome` (`succeeded`, `failed` or `timed-out`). This answers [#2883](https://github.com/fluentcms/FluentCMS/issues/2883) of the review of 7 Nov 2026.

**Publishing inside a transaction, as built.** Outside a transaction of the content service, `IEventBus.PublishAsync` is refused with `FCMS2602` while another transaction is open in the scope, whether on `CmsDbContext`, on `SystemDbContext` or on a plugin's `PluginDbContext<TPlugin>`, because its end cannot be seen and an event that may yet roll back is never delivered. Core adds a transaction interceptor to every plugin context it registers, which remembers the contexts of the scope that began or joined a transaction, and the transaction probe asks whether one of them still has it open. A plugin publishes after its own transaction commits. An explicit `IEventBus.StageAsync(DbContext, ...)`, which would write the outbox rows into the plugin's transaction, is declined before 1.0 to keep the SDK small. The activity log (`FCMS2704`), settings (`FCMS2807`) and the Super Admin guard read the same probe, so they refuse inside a plugin's transaction too. This answers [#2885](https://github.com/fluentcms/FluentCMS/issues/2885) of the review of 7 Nov 2026.

**Catalogue.** Content: `EntryDraftSavedEvent`, `EntryPublishedEvent`, `EntryUnpublishedEvent`, `EntryDraftDiscardedEvent`, `EntryVersionRestoredEvent`, `EntryTrashedEvent`, `EntryRestoredEvent`, `EntryPurgedEvent`, `EntryScheduledEvent`, `EntryScheduleCancelledEvent`. Media (plugin): asset uploaded, updated, replaced, moved, deleted. Identity: user created, updated, deactivated, role changed, login linked, login unlinked, key created, key revoked, login succeeded, login failed, member registered. Settings: `SettingsChangedEvent`. Sites: created, updated. Plugins: `PluginEnabledEvent`, `PluginDisabledEvent`. Infrastructure: `CacheInvalidatedEvent`, `JobFailedEvent`, `JobDeadLetteredEvent`.

**Hooks.** `IBeforeValidateHook<T>`, `IBeforeCreateHook<T>`, `IAfterCreateHook<T>`, the same pairs for Update, Delete, Publish, Unpublish, Restore; `T` is a content class or `ContentDocument` for all types. `HookContext` carries the `OperationContext`, the entry, the previous state, the changed field paths and `Cancel(code, message)`. Ordering: `[HookOrder(int)]` with plugin dependency order as the tiebreak (decision 49).

### 4.12 Job runner

Rows 6.6, 8.10, 10.7 (locks), 10.20 (Should); decision 40.

**Tables.** `cms_job (id, type, payload json, queue, site_id, run_at, priority, status, attempts, max_attempts, lease_until, lease_owner, last_error, created_at, created_by, origin, completed_at)`, `cms_job_attempt (job_id, attempt, started_at, finished_at, outcome, error)`, `cms_recurring_job (name, type, cron, payload json, queue, next_run_at, enabled)`. A recurring job has a `type` because a schedule must say which job it fires. Job `status` is `pending`, `running`, `succeeded` or `dead_lettered`. The claim is served by `ix_cms_job_claim` on `(queue, priority DESC, run_at, id)`, filtered to live jobs (`pending`, `running`), so finished jobs are never walked.

**API.** `IJob<TPayload>.ExecuteAsync(payload, JobContext)`; `IJobScheduler.EnqueueAsync`, `ScheduleAsync(runAt)`, `AddRecurringAsync<TJob, TPayload>(name, cron, payload, queue)`, which is safe to call at every startup on every instance. `IJobDeadLetters` lists dead-lettered jobs and retries one; it checks no permission, so `jobs.manage` is enforced by the caller. Jobs are registered with `cms.Jobs.AddJob<TJob, TPayload>("stable-name")`, and that name is what `cms_job.type` stores.

**Principal and actor.** A job always runs as `Principal.System` with `Origin.Job`. An actor in the payload (a payload that implements `IJobActorPayload`) does not replace the principal: it goes to `OperationContext.Actor`, which authorization never reads, so a payload cannot borrow a user's roles. The actor is built from kind, id and name only, with no claims. `OperationContext.AuditPrincipal` is `Actor ?? Principal`, and audit columns (`created_by`, `updated_by`, `deleted_by`, `author_id`) use it, so a scheduled publish records who scheduled it. A `System`, `Anonymous` or empty actor fails the attempt with `FCMS2507`.

**Runner.** A hosted service with a configurable worker count per queue. Claiming uses the provider dialect: `FOR UPDATE SKIP LOCKED` (PostgreSQL), `UPDLOCK, READPAST` (SQL Server), optimistic `UPDATE` that rechecks every condition of the read (SQLite). Leases with heartbeat; retries with exponential backoff and jitter; dead-letter after `max_attempts`; graceful shutdown; multi-instance safe without extra infrastructure. **Attempt timeout.** Off by default. `JobRunnerOptions.AttemptTimeout` sets a limit for every job, and `AddJob<TJob, TPayload>("name", attemptTimeout)` overrides it for one (`Timeout.InfiniteTimeSpan` turns it off). When an attempt passes its limit the runner stops renewing the lease and cancels `JobContext.CancellationToken`. A job that stops by throwing `OperationCanceledException` fails the attempt with `FCMS2508`, which is retried with backoff or dead-lettered by the usual rules. A job that ignores the token keeps its worker until it returns, but its lease lapses, so another worker retries it, or the sweep dead-letters it on its last attempt; a job that returns normally is a success. `IJobRunner` is an interface so a Hangfire adapter can replace it.

**Built-in jobs.** Scheduled publish and unpublish, trash purge, version pruning, activity retention, outbox dispatch (its own `outbox` queue), webhook delivery (plugin, its own `webhooks` queue), media variant cleanup (plugin).

**Job retention.** A recurring job (`jobs.retention`, daily) removes succeeded jobs and their attempts once `JobRunnerOptions.SucceededRetention` (7 days) has passed since `completed_at`, in batches of at most `RetentionBatchSize` (500, at most 1000), each batch in one transaction. Pending, running and dead-lettered jobs and recurring definitions are never removed. It sets an attempt timeout, and it is safe to run on several instances and to run again.

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
        plugin.AddJob<VariantCleanupJob, VariantCleanupPayload>("media.variant-cleanup");
        plugin.AddEventHandler<EntryPurgedEvent, ReleaseReferencesHandler>();
    }

    public void Configure(IPluginAppBuilder app)
    {
        app.MapGroup("/media").MapMediaEndpoints();   // under the CMS route group, CMS auth by default
        app.MapDelivery("/media").MapMediaDeliveryEndpoints();   // under /api/delivery/v1
        app.MapManagement("/media").MapMediaManagementEndpoints();
    }
}
```

**Registration.** Explicit `AddPlugin<T>()` wins and fixes order; `[assembly: FluentCmsPlugin(typeof(MediaPlugin))]` on referenced assemblies is discovered through the compile-time dependency context. No folder scan, no runtime loading.

**Validation at startup.** SDK range against the running SDK version, dependencies present, duplicate names; failure stops the host with `FCMS15xx` errors (`FCMS14xx` is authorization's).

**Plugin host as built.** The manifest is `[FluentCmsPlugin("name", Version = "1.0.0", SdkRange = "[1.0.0,2.0.0)", DependsOn = ["other"])]` on the plugin class, and the same attribute on an assembly, `[assembly: FluentCmsPlugin(typeof(T))]`, names a plugin class for discovery, as many as the assembly has. A name is lower kebab-case like a content type or a settings area, at most 32 characters, so that `cms_{name}__migrations` and the plugin's tables fit a PostgreSQL identifier. `Version` is a semantic version and, left out, the informational version of the plugin's assembly; `SdkRange` is required. `IPluginServiceBuilder` has the plugin's `Name` and the host's `Services`, and `IPluginAppBuilder` its `Name` and the root `Services`; core implements both and plugins only call them, so the contributions of B-61 are new members that break no plugin. For `IServiceCollection` the SDK references `Microsoft.Extensions.DependencyInjection.Abstractions`, and nothing of EF Core or ASP.NET Core. `cms.AddPlugin<T>(configure)` creates the plugin with its parameterless constructor and hands it to `configure`. Discovery reads the app's deps file (the first of `APP_CONTEXT_DEPS_FILES`, else `{entry}.deps.json`) once per process, loads the runtime assemblies of the libraries that depend on `FluentCMS.Sdk`, directly or through other libraries, and reads their assembly attributes; an app with no deps file on disk, such as a single-file one, discovers nothing and adds its plugins with `AddPlugin`. A plugin that is added and discovered is registered once, as added and where added; one added twice is a duplicate. **Order.** The plugins are taken as they came, the added ones in the order of their `AddPlugin` calls and then the discovered ones in the order of the deps file, and each is placed after the plugins it depends on, which move ahead of it when they come later; plugins with no dependency between them keep that order unless a dependency pulls one of them forward. `ConfigureServices` runs in this order inside `AddFluentCms`, after the host's callback and before core adds its own services, so a plugin's service replaces one that core adds only when none is there, and `Configure` runs in this order inside `MapFluentCms`, after the endpoint groups of core. The plugin host is the `IHookTiebreaker` (a host that registers its own keeps it): among hooks and event handlers of equal `[HookOrder]`, one of a plugin ranks by the plugin's place, and one of no plugin, the host's or core's, after every plugin's, keeping its registration order. A type belongs to the plugin whose `ConfigureServices` registered it, by implementation type or instance, or else to the plugin whose class is the only plugin class in the type's assembly, which covers a hook registered with a factory; the app's own assembly is left out of that, since its other types are the host's. `Configure` runs once, however many times `MapFluentCms` is called, and a discovered plugin whose constructor throws stops the host with its own exception. **Codes.** `FCMS1501` the type cannot be a plugin (no manifest or several on the class, an assembly attribute with a name instead of a type or one that cannot be read, or a discovered class that is abstract, does not implement `IPlugin` or has no public parameterless constructor), `FCMS1502` the name, `FCMS1503` the version, `FCMS1504` an SDK range that is missing or not interval notation, `FCMS1505` a running SDK outside the range, `FCMS1506` two plugins with one name, `FCMS1507` a missing dependency and `FCMS1508` a dependency cycle, whose message lists the cycle; each message names the plugin and its class. `FCMS1520` to `FCMS1526` are the contributions' (below), `FCMS1540` to `FCMS1544` the provider slots', `FCMS1550` to `FCMS1559` per-site enablement's (below), `FCMS1560` to `FCMS1579` the webhooks plugin's (5.2), and `FCMS1580` to `FCMS1599` the media plugin's (5.1). **SDK versions.** The running version is the informational version of `FluentCMS.Sdk` without its build metadata, and the range is parsed in core. A released SDK is held to the range exactly as NuGet holds a version. A pre-release of the SDK counts as the release it leads to when it is held against a bound that is a release, and is compared in full against a bound that is a pre-release: `1.0.0-preview.3` and `1.0.0-dev` satisfy `[1.0.0,2.0.0)`, `2.0.0-preview.1` does not, and `[1.0.0-preview.2,2.0.0)` refuses `1.0.0-preview.1`. NuGet's own precedence puts `1.0.0-preview` below `1.0.0` and `2.0.0-preview` below `2.0.0`, so a range written against released versions would refuse every dev and preview build of the line it names and take the previews of the next major version, which may break the plugin; the rule turns both around and loosens nothing for a release. The startup report and `/system/diagnostics` list the plugins in this order with name and version; the full manifest is the Management plugin list's.

**Plugin contributions as built.** `IPluginServiceBuilder` mirrors the core builders, each method doing what its `cms.*` counterpart does: `AddFieldType<T>()`, `AddPermissions<T>()` (an `IPermissionProvider`), `AddSettings<T>()`, `AddJob<TJob, TPayload>(type, attemptTimeout)`, `AddJobQueue(name, workers)`, `AddEventHandler<TEvent, THandler>()`, `AddOutboxHandler<TEvent, THandler>(name)`, `AddActivity<TEvent>(name, describe)` and `AddErrorCode(code, status, title)`, and `Providers` holds the provider slots (4.13, "Provider slots as built"). `AddJobQueue` adds a job queue whose worker count its factory reads from the host's services when the runner's options are built, so that it can come from the plugin's own options; a count the host sets for that queue, in `cms.Jobs.Options.Queues` or under `FluentCms:Jobs:Queues`, wins, and a name that is empty or longer than 64 characters throws. `FluentCMS.Plugins.Abstractions` adds `AddModel<T>()`, `AddMigrations(assembly)`, `MapGroup(prefix)`, `MapManagement(prefix)` and `MapDelivery(prefix)` as extension methods ([ADR 0003](../decisions/0003-plugin-contribution-contracts.md)). **Tables.** A model contributor maps the plugin's entities on an EF `ModelBuilder`. Each table and view is named `cms_{plugin}_...`, with the hyphens of the name as underscores, and is neither `cms_{plugin}__migrations` nor a table of core or of another plugin; `AddFluentCms` checks this by mapping the contributor on a model without conventions and stops with `FCMS1523`. A content type whose table a plugin maps stops the host with `FCMS1206`. The contributors join `CmsDbContext` after the system tables, excluded from its migrations, and build `PluginDbContext<TPlugin>`, a scoped context with the provider's conventions that the plugin's code queries. **Streams.** `AddMigrations(assembly)` registers the plugin's stream: the migrations of the assembly that name `PluginDbContext<TPlugin>` in `[DbContext]`, with the history table `cms_{plugin}__migrations`, which `IDataProvider.ConfigurePluginMigrations` points at the database. The streams run after the system stream and before the content stream, in plugin order, and a second assembly for one plugin is `FCMS1525`. A model ahead of its migrations stops the migration with EF's pending model changes error. One assembly holds the set of one provider; per-provider sets are [#2711](https://github.com/fluentcms/FluentCMS/issues/2711)'s. **Endpoints.** The groups are relative to the path base and run their endpoints inside the request bridge's `OperationContext`, with no output cache unless an endpoint adds one. `MapManagement(prefix)` is under `/api/management/v1` with what core's Management groups have: the `management` scope (401 without a principal, 403 without the scope), the Management rate limit and CORS policy, and the OpenAPI document. `MapDelivery(prefix)` is under `/api/delivery/v1` with what core's Delivery group has, its output cache included, and an anonymous caller read as its site's Public role (4.10, "Plugin Delivery groups as built"). `MapGroup(prefix)` is under the CMS group with the Delivery rate limit and CORS policy and the plugin's name as its surface, which keeps it out of the OpenAPI document. "CMS auth by default" means that its endpoints need a caller the `FluentCms` scheme signed in, a bearer token or an API key of any class, and answer any other with a 401 problem (`FCMS3102`); an endpoint that is public on purpose, such as the media plugin's asset bytes at `/media/{id}/{filename}`, opts out with ASP.NET Core's `AllowAnonymous()`, on the endpoint or on the group. It is an endpoint filter after the request bridge's, as the `management` scope is, so no host needs `UseAuthorization` for it, and it runs before the per-site gate, so an anonymous caller learns nothing of where the plugin is disabled. Credentials that are sent and refused are a 401 even where anonymous is allowed, as on Delivery. Being signed in is all the group checks: what the caller may do is the plugin's to check with `IPermissionChecker`. The coarse limit before authentication covers these routes as it covers core's (4.10), so a flood of keys sent to them is turned away before any key is looked up. This answers decision 4 of the review of 7 Nov 2026 ([#2847](https://github.com/fluentcms/FluentCMS/issues/2847), [#2859](https://github.com/fluentcms/FluentCMS/issues/2859)): a plugin's group is not anonymous-capable by default. The startup report does not list the endpoints that allow anonymous, because each is a choice written in the plugin's code rather than a setting an operator could change, and a warning at every start for a public media route would teach operators to ignore the warnings; the Management plugin list names every endpoint. A prefix under a group of core, one whose first segment is a route parameter, and one that does not start with a slash or ends with one are `FCMS1524`. So is a `MapDelivery` prefix whose first segment is a route parameter or names a content type. **Codes.** A plugin's code is upper case letters and four digits, neither core's nor another plugin's, with a status from 400 to 599 and a title (`FCMS1522`). A `PluginException(code, message)`, a `HookRejectedException` and a validation error with the code get its status and title; a code the plugin did not add is 422. **Field types.** A plugin type's name is neither core's nor another plugin's (`FCMS1521`). A setting of kind `FieldSettingKind.FieldReference` names a sibling field of the same type, component or block: schema validation refuses one that names no other field (`FCMS1520`), and schema introspection leaves the setting out for a caller who cannot read that field, as it does a slug's `source`. Such a setting is optional, as `source` is, so that a narrowed snapshot still loads; a plugin type that makes one required stops the host (`FCMS1526`). Core's `IFieldTypeRegistry` in the container has the plugins' types, and a host that builds its own `SchemaModel` builds it with that registry, as the test host does. The sample plugin of the tests (`tests/FluentCMS.Plugins.Sample`) uses every contribution and stands in for the plugin template of M6.

**Permission checks of plugins as built.** A plugin's service enforces its own permissions as core's services do, with `IPermissionChecker` (`FluentCMS.Authorization`, in the SDK): `AllowsAsync(permission, site)` and `RequireAsync(permission, site)`, which throws `PermissionDeniedException`, answered with 403, or 401 without a principal. A null site is the instance as a whole, which only a grant with no site limit reaches. A call outside an operation is refused with `InvalidOperationException` (`FCMS1408`, 2.3), so host code that acts as the system wraps it in `SystemOperation.Begin`. Core implements it with the checks of its Management services, so the endpoint carries the identity and the service decides. The webhooks plugin checks `webhooks.manage` with it.

**Per-site enablement.** `cms_plugin_site (plugin, site_id, enabled)` read through `IPluginState`. A disabled plugin's endpoints return 404 for that site, its hooks, events, jobs and MCP contributions are skipped, and the Management plugin list reports it disabled so the admin hides its UI.

**Per-site enablement as built.** `cms_plugin_site` is keyed on plugin and site and goes with its site. A plugin with no row for a site is enabled there, and a row of a plugin that is no longer registered says nothing. `IPluginState.IsEnabledAsync(plugin, site)` (SDK) reads it. Each instance reads the rows whole and keeps them for 30 seconds. A write through the Management API drops them at once, so the instance that wrote answers by the new state from the next request, and another instance within 30 seconds. In a site where a plugin is disabled:

- its `MapGroup`, `MapManagement` and `MapDelivery` endpoints answer 404 (`FCMS1550`), after the signed-in caller or the `management` scope is checked; a copy of a `MapDelivery` answer in the output cache goes with the outbox delivery of `PluginDisabledEvent`, which evicts the site;
- its hooks are skipped for an operation of that site, and its in-process event handlers for an event of that site;
- its outbox handlers get no row for an event of that site, as an in-process handler is not called, so what happens there while it is off is never delivered to it. A row recorded before it was disabled is owed to it: the drain puts it back without using an attempt, as it does a row with no handler (`OutboxOptions.MissingHandlerDelay`), and delivers it once the plugin is enabled there again, so the row neither fails nor is lost;
- its jobs of that site complete without running, as succeeded. A job of no site is the instance's and runs.

A hook or handler is the plugin's by the tiebreaker's rule above, and a job type or an outbox handler by the `AddJob` or `AddOutboxHandler` that added it. An activity row described with `AddActivity` is core's log and is always written. An event or job of no site is not gated. Contributions of the whole instance stay: field types, model, migrations, permissions and the settings schema, because every site shares the schema. MCP contributions are gated when the MCP plugin (M4) reads `IPluginState`. Enabling or disabling raises `PluginEnabledEvent` or `PluginDisabledEvent` with the site as its source, which the activity log records as `plugin.enabled` or `plugin.disabled` with the target type `plugin` and the plugin's name. A write that changes nothing, such as enabling a plugin with no row, raises nothing. Log event ids 9230 to 9234.

**Provider slots.** Defined in the SDK, defaulted in core, replaceable by plugins or the host: `IFileStorage` (media plugin, local default), `IEmailSender` (core; SMTP through MailKit, plus a log-only sender for Development; Q9), `ISearchIndex` (search plugin), `IDistributedCache` (BCL memory default), `IDistributedLock` (core, `cms_lock` table default), `IImageProcessor` (media plugin), `IChatClient` and `IEmbeddingGenerator` from Microsoft.Extensions.AI (AI assist plugin).

**Provider slots as built.** `IProviderSlots` (`FluentCMS.Providers`, in the SDK) has `Use<TSlot, TImplementation>()`, which fills a slot with a singleton of the implementation, and `cms.Providers` is core's. The slots core fills are `IEmailSender` (SMTP, the log or a refusal, 4.7), `IDistributedLock` (`cms_lock`, 4.15), `IDistributedCache`, whose default is the framework's `MemoryDistributedCache`, and `IOutputCacheStore`, whose default is the framework's in-memory store, the registration `AddOutputCache()` makes, since its type is internal; `IFileStorage` and `IImageProcessor` come with the media plugin, which declares them with its defaults (B-64, B-65) through `IPluginServiceBuilder.AddProviderSlot<TSlot, TDefault>()`: such a slot is filled and replaced like core's, and the report lists its default with the declaring plugin; a slot declared twice, by two plugins or by a plugin and core, stops the host with `FCMS1544`, and `ISearchIndex`, `IChatClient` and `IEmbeddingGenerator` with their plugins. What fills a slot is settled when `AddFluentCms` ends: the host's `Use`, else a plugin's, else a service registered for the slot directly (by the host before `AddFluentCms` or in its callback, or by a plugin's `ConfigureServices`), else the default. A `Use` takes out every other registration of its slot, so it wins whatever the order of the calls and the registrations, and of two `Use` calls of one caller for a slot the last wins. Two plugins that replace one slot stop the host with `FCMS1540`, which names the plugins and their implementations, unless the host chooses with its own `Use`; a `Use` of a type that is not a slot stops it with `FCMS1541`, one whose implementation is abstract or an interface with `FCMS1542` when it is called, and one made after `AddFluentCms` has settled the slots, which would change nothing, with `FCMS1543`. A registration that the framework makes for a slot on core's behalf (Razor Pages adds the memory cache) is not the host's and gives way to the default. A registration after `AddFluentCms` comes last and wins, as for any service, and the report says so. A plugin calls the same `Use` on `IPluginServiceBuilder.Providers`, which core fills with `ProvidersBuilder.For(name)`, the replacements of that plugin. The startup report and `/system/diagnostics` list the slots in the order core declares them under `providerSlots`, each with `slot`, `implementation` (the type in use; for a factory the type it returns, or null when it throws), `source` (`default`, `host`, `plugin` or `services`) and `plugin`. Codes `FCMS1540` to `FCMS1544`; no event ids.

**Settings.** `[Settings("media")] sealed class MediaSettings` is persisted per site in `cms_setting (site_id, key, json, updated_at, updated_by)` with instance defaults and per-site overrides. `ISettings<T>.GetAsync(site)` reads it; `SettingsChangedEvent` follows writes; the Management API exposes get and put per area with the JSON schema of the type so the admin can render it.

**Settings as built.** `cms.Settings.Add<T>()` registers a class that has `[Settings("area")]` and a parameterless constructor, and a bad one stops the host (`FCMS2801`). Its permission `settings.{area}.manage` is in the catalogue. `cms_setting` also has `id` and `version`, and two unique indexes (one over `key` where `site_id` is null, one over `site_id, key` elsewhere); a site's rows go with the site. A read resolves a whole document: the site's row, else the instance row, else the code default, and a stored row the class can no longer read is skipped with a warning in the log, so one bad row never takes a site down, and a key that a stored document lacks, such as one added since or one marked `[Required]`, takes its code default. Reads are cached per scope for `cms.Settings.CacheDuration` (30 seconds): a write on the instance drops the cache of its area at once, on every scope, and another instance sees it when its own reads expire, which is the most it can lag. A write needs `settings.{area}.manage` for the site, or from a grant with no site limit for the instance defaults (`SettingsAccessDeniedException`), goes through a context of its own, so it saves nothing the caller has tracked and drops nothing the caller has tracked, and replaces the whole document with one statement that names the version it read, starting again when another writer got there first, so the last of two writes stands; a caller that names the version it read (0 for no row; the Management endpoint's `If-Match`) gets `FCMS2808` when the row has moved, for a reset too. A document is refused with `FCMS2803` (not a JSON object, too large or not JSON), `FCMS2804` (a key the type does not have, one error per key) or `FCMS2805` (a value of the wrong type or one that breaks a validation attribute, on the class, its nested objects, the items of lists and the values of dictionaries, with the path of the key such as `limits.port` or `many[2].port`), and the error names the key and never the value. After the commit it raises `SettingsChangedEvent`, whose source carries the site (empty for the instance defaults), so the activity row is the site's; the event holds the area and nothing of the values. The row, its event and its activity row are not atomic: the event is raised after the commit, so a failure in between leaves the write without them. A write inside a transaction is refused (`FCMS2807`), and an unknown site is `FCMS2806`. A property of the class itself that is marked `[SettingsSecret]`, or named like a secret, is stored with the rest of the document but appears in no event, activity row, log line, validation message or schema default, and the schema marks it `writeOnly`; `[SettingsSecret]` on a property below the top level stops the host (`FCMS2801`), and so does a nested property named like a secret, because only the top level is kept out of reads, events and schemas (`[SettingsSecret]` is the real control; a name is also taken for a secret as a safety net, by a rule of its own that is narrower than the scrubber of events and logs, which reads free text: the name is read as words, ignoring digits and a version marker at its end, by its C# name as well as its JSON name, and is a secret when its last word is `password`, `passphrase`, `secret`, `token`, `pwd` or `apikey`, or its last two are `api`, `private`, `secret`, `access`, `signing` or `encryption` followed by `key`, `client secret` or `connection string`, plurals included, so `Keywords`, `SortKey` and `AccessKeyId` are not); an error under a secret names the secret's key and nothing below it, since a dictionary key may be as secret as its value; encrypting it at rest is left to the database. A document written without a secret key keeps the stored value, because the admin never sees a secret and must not wipe it by sending its form back, while a key that is sent, even as null, replaces it. The stored value is that of the document the read resolved to, which is what the caller was shown: the row being replaced, or for a site with no row of its own the instance default. A row that the class cannot read was skipped by the read, so it gives nothing, whether it is the row of the site (the site then takes the instance default's secrets) or the instance row. What a site inherits is a copy: rotating the secret on the instance does not reach a site that has saved its own row. A stored secret that the class would no longer accept, written under an older shape, is not kept: it takes its code default and a warning in the log names the key, never the value. The Management endpoint reads through a redacted resolve: the document without its secret keys, its source (site, default or code), the version of the row a write to that site would replace (0 when it has none, even if the document came from the instance default, and the version of the row that is there even if it was skipped because the class cannot read it, so that an unreadable row can be saved over), and the secret keys that hold a value in a stored document. The schema of a type is `System.Text.Json`'s, with the validation attributes of its properties added.

**Manifest exposure.** `GET /api/management/v1/plugins` lists name, version, SDK range, dependencies, contributions (field types, permissions, endpoints, jobs, tools) and enablement per site. As built (4.10), tools are left for the MCP plugin.

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

**Health and observability, as built.** `FluentCmsHealthCheckTags` (`FluentCMS.Diagnostics`, in the SDK, so plugins tag their checks) has two tags, `live` and `ready`, and `/health/live` and `/health/ready` run the checks tagged with each through `Microsoft.Extensions.Diagnostics.HealthChecks`, with no authentication and no rate limit policy of the endpoints. They stay reachable without credentials, because the image's `HEALTHCHECK` and an orchestrator's probes call them, so they are kept cheap and bounded instead: the FluentCMS authentication step skips the `health` surface, so a key sent there is never looked up, and the check before authentication counts them per client address in a partition of their own, apart from the coarse limit, with `FluentCms:RateLimits:Health:PermitLimit` (1200 a minute, twenty a second from one address, far above what an orchestrator sends one instance). A path under `/health` that no endpoint answers counts there too. A refusal is the same 429 problem, and the probes carry no `RateLimit-*` headers. Where the network allows, probes should still be restricted to the orchestrator and the load balancer. This answers decision 16 of the review of 7 Nov 2026 ([#2847](https://github.com/fluentcms/FluentCMS/issues/2847), [#2855](https://github.com/fluentcms/FluentCMS/issues/2855)). They answer 200 for Healthy and Degraded and 503 for Unhealthy, with the status as the only body, never a description or an exception. Core registers `process` (live), `database` (ready, one row of a system table, so it also fails when the system migrations did not run) and `job-runner` (ready); `services.AddHealthChecks().AddFluentCmsReadyCheck<T>("name")` adds a ready check with a five second timeout (it is in Core, because the SDK has no reference to the health check abstractions; a plugin that references only the SDK calls `AddCheck` with the tag). The job runner beats in memory at every turn of its maintenance loop (`JobRunnerOptions.MaintenanceInterval`, 15 seconds), and ready is Unhealthy when the last beat is older than `FluentCms:Health:JobRunnerHeartbeatTimeout` (default 2 minutes; `Timeout.InfiniteTimeSpan` leaves it out for an instance that runs no jobs) or when the runner has not started. `GET /api/management/v1/system/health` needs `system.read`, an instance-level permission: it is in the catalogue and checked with `InstanceAccess`, so a role of one site or a grant limited to sites does not reach it, nor does a key bound to sites or a Delivery or Preview key. It runs every check and answers 200 with `{ status, totalDurationMs, checks: [{ name, status, durationMs, tags, description, error }] }`, whatever the status; descriptions and exception text pass through `SecretScrubber`. The one `ActivitySource` and the one `Meter` are both named `FluentCMS` (`FluentCmsTelemetry.Name`), and nothing is exported: a host subscribes with `AddSource` and `AddMeter` from its own OpenTelemetry setup, and Core takes no dependency on it. Spans are named `content {operation}` (create, update, patch, delete, restore, purge, publish, unpublish, discard-draft, schedule, cancel-schedule, versions, version, restore-version, diff, get, query, query-trash, count, bulk), `hook {kind}` (one per run of the hooks of a kind, with `fluentcms.hooks` their number) and `job {type}` (with `fluentcms.queue`). They carry `fluentcms.operation`, `fluentcms.type` (the name the schema resolves, and not on jobs) and `fluentcms.site`, and never a value of content; a job's span has the Error status when the attempt throws. The instruments are `fluentcms.requests` (counter, by `fluentcms.surface` of delivery, management, connect (with `/.well-known`), account, health or docs (the OpenAPI document and Scalar), and `fluentcms.status_class`, counted with the status the client got, including a refusal an endpoint throws), `fluentcms.query.duration` (histogram, seconds, by the `fluentcms.type` the schema resolved, and not recorded for a query refused as invalid), `fluentcms.job.latency` (histogram, seconds, the length of an attempt by `fluentcms.operation` the job type and `fluentcms.outcome`), `fluentcms.event.handler.duration` (histogram, seconds, the length of a run of an in-process event handler by `fluentcms.operation` the event type, `fluentcms.handler` and `fluentcms.outcome`, 4.11), and `fluentcms.cache.requests` (counter by `fluentcms.cache` and `fluentcms.result`) with `fluentcms.cache.hit_ratio` (observable gauge by `fluentcms.cache`); the Delivery output cache reports its lookups as `delivery`. Every `[LoggerMessage]` in Core has an event id, in a range per area that follows the `FCMS` families: 90xx identity (the authorization server 9001 to 9003, the hosted pages 9004, admin SSO 9040 to 9044), 91xx host (health 9101 to 9103, the problem handler 9110, email 9120, the startup report 9130 to 9132), 92xx plugins (per-site enablement 9230 to 9234), 93xx storage (migrations 9301, schema history 9311), 94xx content (9401, scheduled jobs 9411, trash purge 9421), 95xx jobs, 96xx events, 97xx activity, 98xx settings and 99xx seeding. The event name is the method name, and a message carries the `FCMS` code where one applies.

**Multi-instance.** Stateless API; Data Protection and signing keys persisted to the database by default; jobs lease through the database; `IDistributedLock` defaults to a `cms_lock` table and gets a Redis provider in `FluentCMS.Caching.Redis`; output cache in memory by default or Redis; media on shared storage.

**Multi-instance as built.** `IDistributedLock` (`FluentCMS.Providers`, in the SDK) has one method, `TryAcquireAsync(name, timeout)`, which returns a handle that releases the lock when disposed, or null when the timeout passed first (`TimeSpan.Zero` tries once, `Timeout.InfiniteTimeSpan` waits until the lock is free); the handle's `Lost` token is canceled when the lock is lost while held. Core's default, added with `TryAdd` and with no options, keeps one row per name in `cms_lock (name, owner, acquired_at, lease_until)`. A holder takes a 30 second lease and renews it every 10 seconds while it holds the lock, so a holder that stopped without releasing it blocks the others until its lease runs out, and the next holder takes it over with a warning (event id 9160). Taking the lock is an update conditional on the row being free or its lease over, so of two that try at once one wins, and a waiter only reads the row while the lock is held, polling from every 50 milliseconds up to once a second. A free lock keeps its row with no owner, so only the first use of a name inserts, and lock names are a fixed set, one per task rather than one per item. A renewal that finds another holder, or that fails until the lease has run out, cancels `Lost` (9161); a renewal or a release that fails is logged (9162, 9163), and a release changes nothing once another holder has the lock. Leases are read against each instance's clock, so instances keep their clocks in step, as the job leases already need. The Data Protection ring is in `cms_data_protection_key` (4.7), so instances on one database read each other's cookies and protected payloads, and the keys survive a restart. Data Protection makes a key when its ring has none it can use, and instances that started together on an empty ring each made one; one that had read only its own kept it for up to a day, refusing the others' cookies once the two minutes in which Data Protection reloads the ring for an unknown key were over. The key manager now makes a key under the lock `fluentcms:data-protection-key`, after reading the ring again, and returns a key that another instance made in the last five minutes (the clock skew Data Protection allows) instead of making a second; when the lock is not free within 45 seconds it makes the key without it (9164). The changes that can take Super Admin from its last holder run under `fluentcms:super-admins` (4.7), and fail rather than go on without it. The Delivery output cache is the `IOutputCacheStore` slot, in memory by default, which evicts only the copies of its own instance; a host that registers a shared store, such as `Microsoft.AspNetCore.OutputCaching.StackExchangeRedis`, evicts for every instance, and the key's schema hash keeps instances on different schemas apart (4.10). Each instance keeps its own copy of the sites and their locales, of plugin enablement and of settings, and sees a change another instance made within 30 seconds (4.6, 4.13); role changes are seen on the next request everywhere, since every request reads the role stamps (4.8). The Redis provider in `FluentCMS.Caching.Redis` is still to come.

**Reverse proxy.** Forwarded headers, `PathBase` option for sub-path hosting, HTTPS redirection and HSTS options; documented for Nginx, Caddy, YARP, Azure Front Door and Cloudflare.

**Reverse proxy as built.** The options are `FluentCms:ReverseProxy` (`ReverseProxyOptions`, `cms.ReverseProxy` in code) and `UseFluentCms()` applies them first, in this order: forwarded headers, the path base, HSTS, the HTTPS redirect, then the pipeline of 4.10, so the pre-auth limiter and everything else that reads the client address or scheme sees what the proxy forwarded. A host no longer calls `UsePathBase` or `UseForwardedHeaders`. Forwarded headers are off by default; when `ForwardedHeaders:Enabled` is set, `X-Forwarded-For` and `-Proto` are read and `-Host` only with `ForwardHost` and for a host of `AllowedHosts`, from the `KnownProxies` and `KnownNetworks` listed (loopback only when both are empty), up to `ForwardLimit` hops (1). `PathBase` is a path such as `/cms`; every FluentCMS endpoint group (the ones `MapFluentCms` maps, so also one added later) is served only under it, and the same path without it is a 404 problem, written after the security headers and CORS. The gate holds a request to the path base when routing found a FluentCMS endpoint or its path is under a prefix of a group, which covers the authorization server's own paths. Routes the host maps itself (its own endpoints, `/openapi/v1.json`, `/scalar`, `/media`) are its own concern and are not held to it. The HTTPS redirect is off by default, because behind a proxy that ends TLS it loops unless the scheme is forwarded, and takes `HttpsRedirection:Port` (left out, the framework finds the port from `HTTPS_PORT` or the server's address, and redirects nowhere if it finds none); proxy addresses and networks must be written in full, so `1` is refused rather than read as `0.0.0.1`; HSTS is on outside Development and off in Development, with `Hsts:MaxAge` (30 days), `IncludeSubDomains` and `Preload`. A bad value stops the host with `FCMS1101` and its key. Setup for each proxy is in `docs/reverse-proxy.md`.

**Public origin as built.** `FluentCms:PublicOrigin` (`cms.PublicOrigin` in code; the configuration wins) is the address users reach the instance at, such as `https://cms.example.com`. It is an absolute URL with no user, query or fragment, `https` outside Development, whose path is empty or `FluentCms:ReverseProxy:PathBase`, which is added when it is left out; anything else, a path without a path base included, stops the host with `FCMS1101` and the key. When it is set, the password reset and invitation links, the issuer, the endpoint URLs of the discovery document, and the OpenAPI document's server and with it its `oauth2` URLs are built from it and the path base, so neither `Host` nor `X-Forwarded-Host` reaches them, and tokens carry that issuer, so setting or changing the origin refuses the tokens issued before; the endpoints still answer under any host that host filtering lets through. Without it they follow the request's scheme, host and path base, so Production refuses to start (`FCMS1105`, `PublicOriginCheck`) unless `FluentCms:Security:AllowRequestOrigin` is true, which is for an instance whose `AllowedHosts` or proxy admits only its own names; the startup report then lists `public-origin-unset`, and other environments start and list nothing for it. Host filtering runs before the forwarded headers and sees the proxy's `Host`, so with `ForwardHost` on, `X-Forwarded-Host` is applied only for a host of `AllowedHosts` (copied into `ForwardedHeadersOptions.AllowedHosts`), and for any host when `AllowedHosts` is `*` or unset. Code `FCMS1105`.

**Startup diagnostics.** A `StartupReport` (providers, plugins and versions, pending migrations, schema drift, insecure defaults, environment) logged once at start and served at `/api/management/v1/system/diagnostics`.

**Startup diagnostics as built.** The report is made once per start, after the migrations, by a hosted service that logs it at Information with event id 9130 (the report as JSON in the message, and its counts as properties) and one warning with event id 9131 per insecure default; a report that cannot be made is logged (9132, the exception type and its scrubbed message) and replaced by a reduced one that carries the type of the failure, so the endpoint still answers; before any report exists it is a 503 (`FCMS3106`). The report types are public records in the SDK (`FluentCMS.Diagnostics`). It holds the environment, the FluentCMS version, the data provider with its version and the resolved `MigrateOnStartup` and `SchemaSync`, the FluentCMS assemblies in the process, the plugins (an empty list until the plugin host exists), the pending migrations of every stream, the schema hash in code against the newest row of the history (applied hash, time and version, and what differs), and the insecure defaults. Those come from the production startup checks, which are asked in every environment so the report and the refusal cannot disagree (the public origin and key ring checks refuse in Production only, so that other environments keep their own lines), and from eight more: a generated signing key that `AllowGeneratedSigningKey` accepted, the key ring unencrypted where it is not refused (`data-protection-keys-unencrypted`: in every environment but Production, and in Production with `AllowUnencryptedKeyRing`), the Development email sender, the HTTPS redirect off in Production, `AllowedHosts` of `*` or unset in Production (`allowed-hosts-any`, read from the host filtering options, so any `Host` is answered), `FluentCms:PublicOrigin` unset in Production and accepted by `AllowRequestOrigin` (`public-origin-unset`, so links in emails and the issuer follow the request's host), schema sync outside Development (`schema-sync-outside-development`, 4.3), and forwarded headers trusted from every network. A plugin adds its own with an `IInsecureDefaultSource` (`FluentCMS.Diagnostics`, in the SDK), which the report asks in every environment, so it decides itself where a setting is insecure; the webhooks plugin reports `webhooks-private-networks`. Free text goes through `SecretScrubber`, and the report holds no connection string, key or password. `GET /api/management/v1/system/diagnostics` serves that same report, so a change after the start does not show until the next one. It needs `system.read`, an instance-level permission: a role of one site, a grant limited to sites and a key bound to sites are refused with 403, and so is a key without the management scope. There is no switch to turn CORS or the rate limits off, so neither can be reported as off.

**Backups.** Documented per database and storage provider; `fluentcms backup` bundles an export package and the media files into one archive for small installs.

**Supported platforms.** Linux and Windows containers, Azure App Service, AWS ECS, Kubernetes, bare `dotnet run`. .NET 10 LTS baseline.

### 4.16 Reference host

Rows 10.2, 11.5 (sample data); decision 3.

`src/FluentCMS.Host`: an ASP.NET Core app referencing `FluentCMS.Core`, all three data providers (chosen by configuration), every first-party plugin and, later, the admin at `/admin`. Content types come from a schema file loaded through the fluent builder (`FLUENTCMS_SCHEMA=/config/schema.json`) or from the built-in sample pack (`FLUENTCMS_SAMPLE=blog`) for evaluation; see Q2. SQLite with zero configuration by default. Multi-stage Dockerfile on the chiseled `aspnet:10.0` image, non-root, multi-arch; a compose file with PostgreSQL and MinIO; health probes; migrations on start for SQLite and an init container pattern (`fluentcms migrate`, with the CLI in M6) documented for the others.

**Reference host as built.** `src/FluentCMS.Host` references Core, the three data providers, the Blog sample and the plugins that have code, and adds each with `cms.AddPlugin<T>()`: the media plugin (5.1) and the webhooks plugin (5.2). `ReferenceHost.Build(WebApplicationBuilder)` sets it up from configuration alone, so environment variables are enough, and maps `UseFluentCms`, which includes `UseAuthorization`, `MapFluentCms` and `MapFluentCmsOpenApi`, whose document and Scalar UI stay off outside Development unless `FluentCms:OpenApi:Enabled` is true. `FluentCms:Database:Provider` is `Sqlite` (the default), `PostgreSql` or `SqlServer` (`FCMS3150` otherwise), and `ConnectionStrings:FluentCms` is the connection string: `Data Source={content root}/fluentcms.db` by default on SQLite, and required on the others (`FCMS3151`). `FLUENTCMS_SAMPLE=blog` adds the Blog types and `cms.AddBlogSample()` (`FCMS3152` for another value), and `FLUENTCMS_SCHEMA` loads a schema file through `SchemaBuilder.LoadSchemaFile` (`FCMS3153` when the path does not exist; a file with errors stops the host with the `FCMS12xx` codes of the builder); both may be set. Either one calls `cms.AllowSchemaSyncOutsideDevelopment()` (4.3), so their tables are created and extended at start on SQLite in every environment, and a server provider refuses them (`FCMS3154`) until the M6 CLI generates migrations from a schema file. The Dockerfile, built from the repository root, compiles on the build platform for `TARGETARCH`, so `buildx` builds amd64 and arm64 without emulation, and runs on `aspnet:10.0-noble-chiseled-extra` (the extra variant for the ICU and time zone data that localized content and schedules in a time zone need) as the non-root `app` user. It sets `ConnectionStrings__FluentCms=Data Source=/data/fluentcms.db` on a `/data` volume and lifts no Production refusal, so `docker run` with nothing set stops before it touches the database with `FCMS1102`, `FCMS1105` and `FCMS1106`, each naming its setting and its opt-in; `docs/docker.md` shows an evaluation run that sets the public origin and the two other opt-ins, and the production setup. `FluentCms:DataProtection:Certificate:Path` and `Password` name a PKCS#12 file with an RSA key, which the host passes to `ProtectKeysWithCertificate` before `AddFluentCms`, since the image cannot call it itself (`FCMS3155` when the file cannot be read or holds no RSA private key); Azure Key Vault reaches the image as a certificate the platform mounts, and `ProtectKeysWithAzureKeyVault` needs a host of its own. Its `HEALTHCHECK` runs the host with `--probe`, which asks `/health/ready` on the container's own port and path base, since the image has no shell or `curl`. A new instance gets its first administrator from a confidential client with Super Admin registered from the environment, which invites one. A deployment sets `FluentCms__PublicOrigin`, which links in emails and the issuer are built from (`FCMS1105` otherwise), and `AllowedHosts` (`allowed-hosts-any` otherwise), and email goes through SMTP when `FluentCms:Email:Smtp:Host` is set (4.7). `docs/docker.md` is the guide. The compose file with PostgreSQL and MinIO waits for #2711 and the media plugin, and the admin at `/admin` for its milestone. Codes `FCMS3150` to `FCMS3155`.

**Seeding.** `ISeeder`, `SeedContext` and `ISeedRunner` are in the SDK (`FluentCMS.Seeding`). A seeder writes through the public services, so permissions, versions, events, activity, trash and scheduling behave as in real use. It runs as the system principal with the `Seed` origin and the full service scope, so trust comes from registering it: only seeders added with `cms.Seeding.Add<T>()` run, at boot and through `ISeedRunner` alike, and a seeder registered on the container in any other way is ignored. `cms.Seeding.RequireLocale("fr")` makes the default site allow a locale before the first seeder runs (sites cannot be managed through the API before M2) and writes a `site.locale-added` activity row when it adds one; without a seeder added the host refuses to start (`FCMS2901`). `cms.Seeding.RunOnStart` (default true) and `ClaimTimeout` (default 15 minutes, `FCMS2902` if not positive) are the other options. With no seeder added nothing is registered and boot does no extra work.

**Seeding runs once per version.** The seeders run in `StartAsync` after the migrations, on the default site and in its default locale, in registration order. What was applied is kept in the instance-wide settings area `seeding` (no system table): per seeder `Name` (default the type name) the highest `Version` that ran to the end. A seeder applied at its current version is skipped at boot; raising `Version` runs it again. This keeps purged content, renamed authors or tags and changed settings from coming back or being overwritten.

**One claim per run.** A run that has seeders to apply takes a single claim for all of them (a `ClaimId` and a time in the same document), with a write that names the version of the settings row it read, and renews the time every `ClaimTimeout / 3` while it runs. Only a claim older than `ClaimTimeout` (default 15 minutes) is taken over, whatever the version of the seeders, so a rolling deploy never runs two versions at once. An instance that loses the race (`FCMS2808`) or finds a fresh claim boots without seeding and logs it at Information; the seed is retried at a later boot or with `ISeedRunner`. A run finishes or releases the claim only while the stored `ClaimId` is still its own: a run whose claim was taken over stops, logs a warning and leaves the row alone. Any exception after the claim is taken (a locale that is refused, a failed mark, a seeder that throws) releases the claim if it is still the run's own, and is rethrown. A seeder is marked applied only after it succeeds. A seeder that throws releases the claim, is not marked, and fails host start (as does any exception in a seeder at boot).

**Seeders converge.** A seeder finds each entry by a natural key that a person does not change (a slug, never a name) and brings it to the state it wants step by step: create if missing, publish if still a draft, add the translation if missing, trash if not trashed, schedule if not scheduled. It does not return early because the entry exists, so a run that stopped halfway is finished by the next. A trashed entry counts as found, and a unique conflict (`FCMS2111`) or a version conflict while creating means another run got there first, so the entry is read again. `SeedContext.AppliedVersion` is the version the seeder last ran to the end at, 0 if never. State changes (publish, trash, schedule) are made only to entries the run created, or when `AppliedVersion` is 0, a first run that may have stopped halfway. After a version bump existing entries stay as a person left them (a restored article is not trashed again, a schedule is not moved) and only missing ones are created. If a version-bump run crashes after creating an entry and before applying its state, the rerun leaves that entry as it is, because it can no longer tell the entry from one a person changed. A state step that finds its entry gone or in the trash (an overlapping run or a person got there first) counts as done.

**Blog sample.** `samples/Blog` (references the SDK and Core) holds `Article`, `Author`, `Tag`, `Seo`, `HeroBlock`, `QuoteBlock`, `GalleryBlock`, the typed settings class `BlogSettings` (area `blog`; it replaces the `SiteSettings` singleton of 4.2, per 8.7), `BlogSeeder` and `cms.AddBlogSample()`. The seed set is two authors, three tags, five articles (one published in `en` and `fr`, one published, one draft, one trashed and one scheduled for 30 days after the host clock) and the site's `blog` settings. Authors, tags and articles have a slug, which is the key. The media fields (`Hero`, `Avatar`, `Logo`, the images of the hero and gallery blocks) are left out until the media plugin registers the media field type; they return with it. Acceptance on PostgreSQL and SQL Server follows #2711, because SQLite is the only provider run for real until then.

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

**Media as built (B-64).** `MediaPlugin` (`media`) in `FluentCMS.Plugins.Media` references the SDK and `FluentCMS.Plugins.Abstractions`, and the reference host adds it. Its stream on `PluginDbContext<MediaPlugin>` has the SQLite set (`InitialMedia`): `cms_media_asset` with the columns above (`duration` stays null until audio and video metadata, 5.14) and `cms_media_folder (id, site_id, parent_id, name, path)`, where `path` is the ids from the root down to the folder, `/{id}/{id}/`. The variant and preset tables come with transforms ([#2833](https://github.com/fluentcms/FluentCMS/issues/2833)). The PostgreSQL and SQL Server sets are [#2711](https://github.com/fluentcms/FluentCMS/issues/2711)'s, as for webhooks.

- **Contracts.** The SDK has `IFileStorage` (`OpenReadAsync`, `WriteAsync`, `DeleteAsync`, `ExistsAsync`, `GetMetadataAsync`, and the `Name` an asset records) and `IMediaService`, which the endpoints call and the field type, the MCP plugin and other plugins will. The service checks every permission with `IPermissionChecker`.
- **Storage.** `IFileStorage` is a provider slot the plugin declares, whose default `LocalFileStorage` (`local`) keeps files under `FluentCms:Media:Local:Root` (`App_Data/media` under the content root). The root is the instance's, in configuration, not a site setting: the storage is one for every site, and a root a site administrator could change would write anywhere the process can. A key is segments of letters, digits, dots, hyphens and underscores, checked before it becomes a path, and a write goes to a temporary file that is moved over the target. An asset's key is `{site}/{asset}/original{.ext}`, so variants can sit beside it. The local disk is allowed in Production, and the startup report lists `media-local-storage` there with the warning that every instance needs the directory on one shared volume and that backups must include it. Each asset records `storage_provider` and `storage_key`, so a storage plugin ([#2836](https://github.com/fluentcms/FluentCMS/issues/2836)) that replaces the slot for new uploads leaves older assets where they are, to be read through the storage they name.
- **Upload.** `POST /api/management/v1/media` reads a multipart body of up to `FluentCms:Media:MaxRequestBodySize` (256 MB): every file part is a file, and the field `folderId` names the folder. The rules are the site's, in the settings area `media` (`settings.media.manage`): `MaxFileSize` (50 MB), `MaxRequestSize` (200 MB, cut to the instance limit), `MaxFilesPerRequest` (20), `AllowedTypes` and `Svg`. A file is read once to a temporary file, counted and hashed with SHA-256 on the way, so one over a limit is refused (`FCMS1582`, 413) without reading it to the end. Its type comes from its first bytes, from a built-in table: JPEG, PNG, GIF, WebP, AVIF, PDF, MP4, WebM, MP3 and WAV, which is the default allow-list; a type off the site's list or no known type is `FCMS1581` (415). Executables are always refused, whatever the name or `Content-Type` say (`FCMS1583`, 415): by signature (PE `MZ`, ELF, Mach-O in both byte orders and universal binaries, and a shebang) and by an executable or script extension. Width and height are read from the headers of JPEG, PNG, GIF and WebP with built-in code. A request stores all its files or none: they are written to the storage only once every file has passed, and a file written for a request that then fails is deleted again (event 9260 when that fails).
- **SVG.** `Svg` is `Refuse` (the default), which refuses every SVG (`FCMS1584`, 415), or `Sanitize`. An SVG is XML read with DTD processing prohibited, no resolver, no processing instructions and no comments, whose root is `<svg>`; anything else is no SVG. Sanitising is a conservative pass with `System.Xml`: it removes `script`, `foreignObject`, `iframe`, `embed`, `object`, `handler` and `listener`, elements of other vocabularies such as XHTML, attributes of other namespaces, `on*` attributes, animations whose `attributeName` is a link or a handler, every attribute whose value holds a `javascript:` or `vbscript:` URL or a `data:` URL other than a PNG, JPEG, GIF or WebP image, and CSS with `@import`, `expression(` or such a URL. Size and checksum are those of the sanitised file. Serving SVGs with a restrictive `Content-Security-Policy` is the Delivery route's ([#2834](https://github.com/fluentcms/FluentCMS/issues/2834)).
- **Metadata.** `GET` and `PATCH /media/{id}` read and change `title`, `alt` and `caption` in the locale of the request (`?locale=` or `X-Cms-Locale`, as for content), `credit`, `tags` (at most 50, each up to 64 characters without quotes, backslashes or control characters), `focalPoint` (`x` and `y` from 0 to 1) and `isPrivate`; a member left out stays, and an empty text clears it. `POST /media/{id}/move` moves an asset to a folder or, with a null `folderId`, the root. `DELETE /media/{id}` sets `deleted_at`, after which the asset is not found; its bytes stay until purge and the usage checks of [#2834](https://github.com/fluentcms/FluentCMS/issues/2834). `GET /media` lists the assets newest first, by `folder` (an id, or `root`), `subfolders`, `type` (a MIME type or a family such as `image/*`) and `tag`, with `page` and `pageSize` (up to 100).
- **Folders.** `/media/folders` lists, creates (`name`, `parentId`), renames (`PATCH`), moves (`POST /{id}/move` with `parentId`) and deletes folders. Names are unique within their parent (`FCMS1587`, 409), folders nest up to 20 deep, a folder cannot move into itself or a subfolder (`FCMS1589`, 409), and only an empty folder is deleted (`FCMS1588`, 409). A folder moves with its subfolders, whose paths are rewritten, and its assets, which keep their folder.
- **Permissions.** `media.read`, `media.upload`, `media.update` (metadata and moves, in both folders), `media.delete` and `media.folders.manage`. A grant may be limited to folders with the `resources` constraint (4.8, [ADR 0004](../decisions/0004-folder-limited-media-grants.md)): it reaches those folders and every folder below them, wherever they move, and only a grant with no folder limit reaches the root. An asset or folder outside the reach is not found (`FCMS1585`, `FCMS1586`), and a caller without the permission gets 403. This replaces `media.folder.{id}.read` above, which a catalogue fixed at startup cannot hold.
- **Events.** `MediaAssetUploadedEvent`, `MediaAssetUpdatedEvent`, `MediaAssetMovedEvent` and `MediaAssetDeletedEvent` (SDK, `FluentCMS.Events`) carry the asset's id, folder and file name, and write the activity rows `media.uploaded` (with `mimeType` and `size`), `media.updated`, `media.moved` (with `from` and `to`) and `media.deleted`, with the target type `media`. Webhooks deliver them (5.2).
- **Codes.** `FCMS1580` an invalid field, `FCMS1581` a type that is not allowed, `FCMS1582` an upload over a limit, `FCMS1583` an executable, `FCMS1584` an SVG the settings refuse, `FCMS1585` no such asset, `FCMS1586` no such folder, `FCMS1587` a folder name that is taken, `FCMS1588` a folder that is not empty and `FCMS1589` a folder moved into itself. Event ids 9260 to 9279.

**Decisions as built (B-64).** Settled for [#2832](https://github.com/fluentcms/FluentCMS/issues/2832):

- **Migrations.** Only the SQLite set of the media stream ships. Per-provider plugin migration folders are [#2711](https://github.com/fluentcms/FluentCMS/issues/2711)'s, and since [#2907](https://github.com/fluentcms/FluentCMS/issues/2907) the runner refuses a set scaffolded for another provider, so the PostgreSQL and SQL Server sets wait for it.
- **Folder-limited grants.** A `resources` constraint on the grant, naming folder ids, which `IPermissionChecker.ResourcesAsync` answers and the media service extends to subfolders, keeps the catalogue fixed at startup ([ADR 0004](../decisions/0004-folder-limited-media-grants.md)).
- **File types.** A small built-in table of magic bytes, with executables refused by signature as well as by name. No package.
- **Image size.** Read from the headers with built-in code; ImageSharp comes with transforms ([#2833](https://github.com/fluentcms/FluentCMS/issues/2833)).
- **SVG.** `Refuse` by default, or `Sanitize` with the `System.Xml` pass above, DTD processing off.
- **Local storage.** Its root is in the plugin's configuration (`FluentCms:Media:Local:Root`), since the provider is one per instance; the rules of uploads are the site's media settings. Allowed in Production with the startup report's warning.

### 5.2 Webhooks plugin

Webhooks appear in rows 2.2, 2.20, 6.10, 8.16 and 10.8, and decision 47 names them a first-party plugin.

**Tables.** `cms_webhooks_subscription (id, site_id, name, url, secret (Data Protection encrypted), previous_secret (encrypted the same way), previous_secret_expires_at, events json, types json, headers (json, Data Protection encrypted), enabled, created_*, updated_*)`, `cms_webhooks_delivery (id, delivery_id, webhook_id, event_id, event, attempt, state, status_code, duration_ms, request_body, response_excerpt, error, next_retry_at, manual, created_at)`. A plugin's tables start with `cms_{plugin}_` (4.13).

**Delivery.** Subscribes through the outbox; each delivery is a job. Payload `{ version, id, event, occurredAt, site, origin, actor, data: { type, documentId, locale, status, version }, links }`. Headers `X-FluentCMS-Event`, `X-FluentCMS-Delivery`, `X-FluentCMS-Signature: t=<unix>,v1=<hex HMAC-SHA256(secret, t + "." + body)>`, with a `v1` for each secret that signs. Retries at 1m, 5m, 30m, 2h, 24h by default, then dead; manual redeliver; test ping; log retention 30 days.

**Endpoints.** `/api/management/v1/webhooks` CRUD, `/{id}/deliveries`, `/{id}/deliveries/{d}/redeliver`, `/{id}/ping`.

**Webhooks as built.** `WebhooksPlugin` (`webhooks`) in `FluentCMS.Plugins.Webhooks` references the SDK and `FluentCMS.Plugins.Abstractions`, and the reference host adds it. Its stream on `PluginDbContext<WebhooksPlugin>` has the SQLite set (`InitialWebhooks`, `AddDeliveryClaim` with the claim's `manual` column and in-flight index, and `AddPreviousSecret` with the `previous_secret` columns); the PostgreSQL and SQL Server sets are [#2711](https://github.com/fluentcms/FluentCMS/issues/2711)'s ([ADR 0003](../decisions/0003-plugin-contribution-contracts.md)), and [plugin migrations](../plugin-migrations.md) has the steps.

**Subscriptions.** A webhook belongs to the site of the operation that made it. Every endpoint needs `webhooks.manage` in that site, which `WebhookService` checks with `IPermissionChecker`, and a webhook of another site is not found (`FCMS1564`). A webhook names:

- one or more events, by the names of their activity rows: `content.draft_saved`, `content.published`, `content.unpublished`, `content.draft_discarded`, `content.version_restored`, `content.trashed`, `content.restored`, `content.purged`, `content.scheduled`, `content.schedule_cancelled`, `cache.invalidated`, `media.uploaded`, `media.updated`, `media.moved` and `media.deleted` (another is `FCMS1563`);
- content types of the schema, where none is every type; a cache or media event has no type and matches by name alone;
- up to 20 headers, none of them one that a delivery sets, whose values are secrets (below).

Names are unique per site (`FCMS1566`). The secret is the caller's, of 16 to 256 characters, or generated (`whsec_` and 64 hex digits) and returned by that create call only. It is stored with Data Protection and is in no other response, event, activity row or log line. `PUT /{id}` replaces the webhook, and leaving the secret out keeps it. A new secret keeps the one it replaces signing beside it until `FluentCms:Webhooks:SecretOverlap` (24 hours; `00:00:00` for none) has passed, below. `webhook.created`, `webhook.updated` (with `secretChanged`) and `webhook.deleted` are activity rows of the plugin's events, which carry the id and the name only. The media events are the SDK's (`FluentCMS.Events`), so a webhook subscribes to them whether or not the media plugin is in the host.

**Headers are secrets.** A receiver's credentials, such as `Authorization` or `X-Api-Key`, are what the headers are for, so their values are treated as the secret is ([#2881](https://github.com/fluentcms/FluentCMS/issues/2881)):

- The `headers` column holds the JSON map protected with Data Protection, under the purpose `FluentCMS.Plugins.Webhooks.Headers` beside the secret's `FluentCMS.Plugins.Webhooks.Secret`, so a protected value of one cannot be read as the other.
- No answer holds a value. List, get, create and update give each header's name with the value `***`. The create answer does not echo them either, because the headers are the caller's, like a secret the caller gives, which that answer leaves out too; only a generated secret is shown once.
- A `PUT` that sends `***` for a header keeps the value stored under that name, compared without case, so a webhook read and sent back keeps its headers. `***` for a header that has no stored value is `FCMS1560`.
- Deliveries send the real values, and the delivery log, the job payload, the events, the activity rows and the log lines hold none.
- A row written before the headers were protected holds plain JSON, which a protected value, in base64url, never starts with. It is read as it is, so it is still delivered, and protected by the next write of the webhook. Once the host has started, after the migrations, the plugin also protects every such row; the update names the plain value it replaces, so a write in between, or another instance doing the same, wins. Event 9248 counts the rows it protected, and 9249 reports a failure, after which it tries again every 30 seconds.
- When the key ring can no longer read the headers, an answer lists none and an attempt fails with "The headers of the webhook could not be read; set them again."

**Secret rotation.** A `PUT` with a new secret moves the one it replaces to `previous_secret`, still protected, with `previous_secret_expires_at` set `SecretOverlap` ahead ([#2886](https://github.com/fluentcms/FluentCMS/issues/2886)). Until then a delivery is signed with both, `t=<unix>,v1=<new>,v1=<previous>`, as Stripe and Svix do, so a receiver that still has the previous secret keeps verifying while it is changed over. Then only the new secret signs. Nothing has to clear the previous one: it is ignored once it has expired, and the next write of the webhook drops it. A second rotation within the overlap replaces the previous secret with the one that rotation replaces, so at most two secrets sign. While the previous secret signs, the answers carry `previousSecretExpiresAt`.

**Target URLs.** A URL is absolute `https`, or `http` in Development, with no user information (`FCMS1561`). Delivery refuses addresses that are not public: loopback, link-local (with `169.254.169.254`), private (RFC 1918, `fc00::/7`), carrier-grade NAT, multicast, unspecified and reserved, and the IPv4-mapped, IPv4-compatible and NAT64 forms of these. The check runs on the resolved addresses at connect time in `SocketsHttpHandler.ConnectCallback`, so DNS rebinding cannot get past a check made only at save. Saving checks the host too, for an early error (`FCMS1562`); a host that does not resolve yet is accepted. Redirects are not followed, and a 3xx is a failed attempt. Deliveries do not go through a system proxy. `FluentCms:Webhooks:AllowPrivateNetworks` (default `false`) turns the address check off, and the startup report lists `webhooks-private-networks` while it is on in Production.

**Delivery.** An outbox handler per event (`webhooks.{event}`) matches the event against the enabled webhooks of its site and enqueues the job `webhooks.deliver` for each. The delivery id is derived from the outbox delivery and the webhook, so a handler that runs again changes nothing. The body is `{ version, id, event, occurredAt, site, origin, actor: { kind, id, name }, data, links }`:

- `version` is `1`. It changes only with a change of the body that a receiver of the current version could misread; a new field is not one. A redelivery sends the body as it was first sent;
- `id` is the event's, the same for a redelivery;
- `data` is `{ type, documentId, locale, status, version }` for content, where `status` is what the event leaves the version as (`draft`, `published`, `unpublished`, `trashed`, `purged`, `scheduled` or `unscheduled`), `{ tags }` for `cache.invalidated`, and `{ assetId, folderId, fileName }` for a media event;
- `links` holds the `management` and `delivery` paths of the entry, or the `management` path of a media asset, relative to the root of the instance behind its path base.

The headers are `X-FluentCMS-Event`, `X-FluentCMS-Delivery` (the same for every attempt of a delivery) and `X-FluentCMS-Signature: t=<unix>,v1=<hex HMAC-SHA256(secret, t + "." + body)>`, with a `v1` for each secret that signs, the current one first. A receiver recomputes the HMAC over `t + "." + body`, compares it with each `v1` in constant time and accepts the body when any matches, refuses a `t` more than five minutes from its own clock, and ignores a delivery id it has already handled. A non-2xx answer, no answer within `FluentCms:Webhooks:Timeout` (10 seconds) or a network error fails the attempt. The job records each attempt and schedules the next one itself, 1m, 5m, 30m, 2h and 24h after a failure on the host's `TimeProvider`, so a delivery has six attempts and the last failure is `dead`. The job itself fails only when the database does. A disabled or deleted webhook gets no further attempts.

**Queue and claims.** Every attempt runs on the job queue `webhooks`: the first attempt of a delivery, its retries, a redelivery and a ping. The plugin adds the queue with `AddJobQueue`, with `FluentCms:Webhooks:Workers` workers on each instance (default 2, and 0 for none), so a receiver that hangs holds the workers of that queue for its timeout and no others: scheduled publishing, trash purge and the retention jobs keep the `default` queue's. `FluentCms:Jobs:Queues:webhooks`, or the host's own `Queues` in code, wins over `Workers`. A job claims its attempt before it sends it, by inserting the attempt's row as `pending`. `ux_cms_webhooks_delivery_attempt` makes that insert the claim, and `ux_cms_webhooks_delivery_in_flight`, unique over `webhook_id` where the state is `pending`, lets a webhook have one attempt in flight at a time across every instance. Then the job sends, schedules the next attempt when one follows, and writes the result to the row. The next attempt is scheduled first because a second job for it finds it claimed and sends nothing, while a result written without its next attempt would end the delivery. A job that finds:

- its attempt recorded stops, so a repeat of the job, or a second job for one delivery when the outbox runs a handler again, sends nothing;
- its attempt pending schedules itself again for the moment the claim would be abandoned;
- another attempt of its webhook pending schedules itself again for when that attempt's send must have ended, its claim plus `Timeout`, and at least 2 seconds later, with up to 2 seconds of jitter. It holds no worker meanwhile, and a burst to a receiver that hangs wakes its waiting jobs once per send rather than every few seconds.

A pending row claimed more than `Timeout` plus one minute ago is abandoned: its worker stopped, or could not write the result, before recording it. The minute covers the writes around the send and the clocks of the other instances. The next job to find it, of the same attempt or of another delivery to the webhook, takes it over: the row becomes `failed` (`dead` for the last attempt, a ping or a redelivery) with the error "The worker stopped before it recorded the result of this attempt.", and the next attempt is scheduled on the usual schedule. The attempt is not sent again, so a failed write after a send never sends that attempt twice. Its result is not known, so the delivery goes on, and the receiver may get it once more as the next attempt, which `X-FluentCMS-Delivery` lets it ignore. A row's `created_at` is when it was claimed, and `manual` marks a ping or a redelivery, so that a job that takes over its claim knows it is tried once. This answers decision 17 of the review of 7 Nov 2026 ([#2847](https://github.com/fluentcms/FluentCMS/issues/2847)): a `webhooks` queue, with one attempt in flight per webhook ([#2880](https://github.com/fluentcms/FluentCMS/issues/2880), [#2882](https://github.com/fluentcms/FluentCMS/issues/2882)).

**Body and header contract.** This answers decision 12 of the review of 7 Nov 2026 ([#2847](https://github.com/fluentcms/FluentCMS/issues/2847)). Header values are secrets, as above. Before 1.0 the body keeps its own shape with `version` 1, and the headers keep `X-FluentCMS-*` with the `t=<unix>,v1=<hex>` signature. Neither CloudEvents 1.0 (`specversion`, `type`, `source`, `id`, `time`, `data`) nor the Standard Webhooks headers (`webhook-id`, `webhook-timestamp`, `webhook-signature`) are adopted now. Either can come later as a format a webhook opts into, with the current shape as its default, so version 1 does not break.

**Log.** `cms_webhooks_delivery` holds a row per attempt: the state (`pending` while it is sent, `succeeded`, `failed` with `next_retry_at`, or `dead`), the status code, the duration, the body sent, the first 1024 characters of the answer and the error. `GET /{id}/deliveries` pages them newest first (`page`, and `pageSize` up to 100). `POST /{id}/deliveries/{deliveryId}/redeliver` sends the body of a delivery again, and `POST /{id}/ping` sends a `ping` event. Each is a new delivery that is tried once, even when the webhook is disabled, and answers 202 with its `deliveryId`. The recurring job `webhooks.retention` (daily at 04:15 UTC, stored once the host has started) removes attempts older than `FluentCms:Webhooks:Retention` (30 days) in batches of 500.

**Per-site enablement.** Core gates the plugin by site (4.13) and the plugin checks nothing itself. In a site where it is disabled, an event records no `webhooks.{event}` row, its endpoints answer 404 and a `webhooks.deliver` job of that site completes without sending. `webhooks.retention` has no site and runs. A retry that was already scheduled when the plugin was disabled completes without sending, so the attempt before it keeps the state `failed` with its `next_retry_at`.

**Codes.** `FCMS1560` an invalid field, `FCMS1561` an invalid URL, `FCMS1562` a URL that is not public, `FCMS1563` an unknown event, `FCMS1564` no such webhook, `FCMS1565` no such delivery and `FCMS1566` a name that is taken. Event ids 9240 to 9249.

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

Search (2.23, 8.11): `ISearchIndex` slot in the SDK from the start; the plugin provides PostgreSQL FTS and SQLite FTS5 and the `q=` parameter (Should). The Entra ID, Okta and Keycloak presets of generic OIDC are in core (4.7); other presets and SAML (4.18) are Should. The rendering plugin (Blazor components and Razor tag helpers) is Should and mostly not backend; its server pieces (rich text to HTML, draft preview middleware, sitemap) reuse `RichText/` renderers from core.

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

`FluentCmsTestHost` on `TestServer` (`WebApplicationFactory` needs the entry point of the application under test, and the CMS library has none): `UseSqlite(inMemory or file)`, `WithContentTypes<...>()`, `WithPlugin<T>()`, fixtures `CreateUserAsync(role)`, `CreateApiKeyAsync(class)`, `AsUser(user)` and `AsKey(key)` HTTP clients, `Content<T>()` for in-process calls, `Seed(...)`. `Seed` steps and the registered seeders (4.16) run before the host reports started. The project's own integration tests use it from milestone M1, which is why it ships early; the Blog sample tests are the first to use a seeder.

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
| M2 HTTP and identity | Delivery and Management APIs, authorization server with hosted pages, admin users, roles, keys, 2FA, SSO federation, activity log endpoint, OpenAPI, caching, limits, health, observability, startup diagnostics, reference host image. | An editor can be invited, sign in through the hosted pages, obtain a token with PKCE, create and publish content through the Management API, and read it through the Delivery API with a key, all from the Docker image on SQLite (PostgreSQL and SQL Server deferred, Q19). |
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
| B-36 [#2749](https://github.com/fluentcms/FluentCMS/issues/2749) | Attempt timeout for the job runner: per registration, default in `JobRunnerOptions`, cancels the token and stops renewing | 4.12 | S |
| B-37 [#2750](https://github.com/fluentcms/FluentCMS/issues/2750) | Remove succeeded jobs and their attempts after a retention period | 4.12 | S |
| B-38 [#2751](https://github.com/fluentcms/FluentCMS/issues/2751) | Decide on stable ids for blocks, which changes the stored format in Appendix A | 4.4 | S |

### 9.3 M2 HTTP and identity

Tracking issue: [#2795](https://github.com/fluentcms/FluentCMS/issues/2795).

| Id | Issue | Spec | Size |
| --- | --- | --- | --- |
| B-40 [#2776](https://github.com/fluentcms/FluentCMS/issues/2776) | `AddFluentCms()` and `MapFluentCms()`, options binding and `ValidateOnStart`, Production safety refusals | 10.3, 4.13 | M |
| B-41 [#2777](https://github.com/fluentcms/FluentCMS/issues/2777) | ASP.NET Core Identity stores for admin users and roles; invitations through the `IEmailSender` slot with the log-only sender; password policy; lockout; deactivate; schedule jobs read `cms_user_role` | 4.2 | M |
| B-42 [#2778](https://github.com/fluentcms/FluentCMS/issues/2778) | OpenIddict server: endpoints, flows, scopes, audiences, signing key persistence, session list and revoke | 4.1, 2.13 | L |
| B-43 [#2779](https://github.com/fluentcms/FluentCMS/issues/2779) | Hosted pages: login, logout, consent, 2FA, recovery codes, forgot and reset, invitation, external callback, error; branding settings | 4.1 | L |
| B-44 [#2780](https://github.com/fluentcms/FluentCMS/issues/2780) | Two-factor TOTP with per-role enforcement | 4.16 | M |
| B-45 [#2781](https://github.com/fluentcms/FluentCMS/issues/2781) | Admin SSO federation: generic OIDC with presets, auto-provisioning, group mapping, local-login disable | 4.15 | M |
| B-46 [#2782](https://github.com/fluentcms/FluentCMS/issues/2782) | API keys: table, classes, format, hashing, authentication handler, per-key rate limit | 4.9, 2.12 | M |
| B-47 [#2783](https://github.com/fluentcms/FluentCMS/issues/2783) | Delivery API endpoints with bracket filter parser, status and locale handling, `by/{field}` lookup | 2.1, 2.4 | L |
| B-48 [#2784](https://github.com/fluentcms/FluentCMS/issues/2784) | Management API: content group (CRUD, publishing, versions, schedules, bulk, trash) | 2.2 | L |
| B-49 [#2785](https://github.com/fluentcms/FluentCMS/issues/2785) | Management API: schema introspection with permission filtering | 1.13, 4.17 | M |
| B-50 [#2786](https://github.com/fluentcms/FluentCMS/issues/2786) | Management API: users, invitations, roles, permissions catalogue, API keys, the default site and its locales, settings, jobs, activity, me | 2.2 | L |
| B-51 [#2787](https://github.com/fluentcms/FluentCMS/issues/2787) | Problem details error model and `FCMS` code registry | 2.8, 11.14 | S |
| B-52 [#2788](https://github.com/fluentcms/FluentCMS/issues/2788) | OpenAPI 3.1 document transformers and Scalar UI | 2.9 | M |
| B-53 [#2789](https://github.com/fluentcms/FluentCMS/issues/2789) | Response caching: headers, output cache tags, eviction on events, `CacheInvalidatedEvent` | 2.20, 10.8 | M |
| B-54 [#2790](https://github.com/fluentcms/FluentCMS/issues/2790) | Rate limiting policies, CORS allow-lists, security headers, request limits | 2.17, 2.18, 10.12 | M |
| B-55 [#2791](https://github.com/fluentcms/FluentCMS/issues/2791) | Health checks, OpenTelemetry instrumentation, structured log event ids | 10.5, 10.6 | M |
| B-56 [#2792](https://github.com/fluentcms/FluentCMS/issues/2792) | Multi-instance: Data Protection to database, `IDistributedLock` with `cms_lock` default | 10.7 | S |
| B-57 [#2793](https://github.com/fluentcms/FluentCMS/issues/2793) | Startup report and diagnostics endpoint; reverse proxy options | 10.13, 10.14 | S |
| B-58 [#2794](https://github.com/fluentcms/FluentCMS/issues/2794) | Reference host: project, schema file loading (Q2), sample pack, Dockerfile, compose, probes | 10.2 | M |

### 9.4 M3 Plugin host, media, webhooks

| Id | Issue | Spec | Size |
| --- | --- | --- | --- |
| B-60 | Plugin host: `IPlugin`, manifest attribute, explicit and discovered registration, startup validation, ordering | 8.1, 8.2 | M |
| B-61 | Plugin contributions: endpoints under the CMS group, permissions, settings, model contributor, migrations stream, jobs, event handlers | 8.6, 8.13, 8.14 | L |
| B-62 | Per-site enable and disable with gating of endpoints, hooks, jobs and tools | 8.15 | M |
| B-63 | Provider slot defaults: SMTP `IEmailSender` (Q9; the slot and log-only sender are in B-41), memory cache; slot registration API | 8.11 | S |
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
| Q5 | EF model from property bags (runtime) or from source-generated row classes (compile time)? | Property bags; spike B-10 validates them on all three providers before the content tables are built. The spike confirmed property bags on SQLite, the only provider verified so far, and [ADR 0001](../decisions/0001-storage-spike-outcome.md) records the outcome and the resulting changes to this document. |
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
    video_url       varchar(2048),
    sections        jsonb NOT NULL DEFAULT '[]',   -- [{"type": "hero", "id": "V1StGXR8_Z5j", ...}]
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
    UNIQUE (document_id, locale, state)
);
CREATE UNIQUE INDEX ux_cms_article_i18n_slug
    ON cms_article_i18n (site_id, locale, slug) WHERE state = 1;
CREATE INDEX ix_cms_article_category ON cms_article (site_id, state, category);
CREATE INDEX ix_cms_article_published_on ON cms_article (site_id, state, published_on);

-- author, tags and hero live in cms_reference, with locale '' (never NULL) for non-localized values:
-- (source_type='article', source_document_id, state, locale '', field='author', position 0,
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
      "sections": [{ "type": "hero", "id": "V1StGXR8_Z5j", "heading": "Bienvenue", "image": { "id": "0192f2a4-3333-7000-8000-000000000003", "type": "media" } }],
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
| `/openapi/v1.json`, `/scalar` | document and UI | configurable; mapped in Development, elsewhere with `FluentCms:OpenApi:Enabled` |
| `/admin` | reserved for the Blazor admin | |

A plugin mounts groups under `/api/delivery/v1` and `/api/management/v1` with `MapDelivery` and `MapManagement` (4.13), such as the media plugin's `GET /api/delivery/v1/media/{id}`, and its own groups elsewhere with `MapGroup`, never under a prefix of core. All prefixes are relative to the configured `PathBase`. The version segment allows Delivery and Management to evolve independently; schema changes are never API versions (2.32).

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
| `cms_webhooks_subscription`, `cms_webhooks_delivery` | webhooks plugin |
| `cms_mcp_instruction`, `cms_mcp_prompt` | MCP plugin |
| `cms_ai_usage` | AI assist plugin |
| `cms_site_host` | multi-site plugin |

Content type tables (`cms_{type}`, `cms_{type}_i18n`) are generated from the host's schema and owned by the host's migrations.
