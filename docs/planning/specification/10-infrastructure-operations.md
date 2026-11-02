# 10. Infrastructure & operations — approved

FluentCMS runs as an ordinary ASP.NET Core application: one process, one database, env-var configuration, health endpoints, OpenTelemetry. Nothing else is required for a single instance; Redis and object storage are added only when scaling out. The reference host image is the supported way to run it standalone, and the CLI moves schema and content between environments.

| # | Feature | Tier | What it means |
| --- | --- | --- | --- |
| 1 | EF Core providers | Must | PostgreSQL (primary, tested first), SQLite (development, small sites, tests) and SQL Server; one migration set per provider; provider-specific features (JSONB, FTS) behind abstractions with documented fallbacks. |
| 2 | Reference host image | Must | Multi-arch Docker image with API, admin and first-party plugins; configured entirely by environment variables; runs on SQLite with zero configuration for evaluation. |
| 3 | Configuration | Must | Standard .NET configuration: `appsettings`, environment variables, user secrets, Key Vault and AWS Secrets Manager providers; validated at startup with clear errors; no secrets in files. |
| 4 | Migrations at startup or by CLI | Must | Core and plugin migrations run in dependency order; opt-in automatic apply at startup for small installs, CLI apply for controlled environments; pre-flight check reports pending migrations. |
| 5 | Health and readiness endpoints | Must | `/health/live` and `/health/ready` covering database, storage, job runner and plugins; compatible with Kubernetes probes and Azure App Service. |
| 6 | Observability | Must | OpenTelemetry traces, metrics and structured logs for requests, queries, hooks, jobs, webhooks and MCP calls; exporters configured by the host; no telemetry sent to the project. |
| 7 | Multi-instance safety | Must | Stateless API; job runner uses database locks; caches and locks use `IDistributedCache` and a distributed lock abstraction with Redis provider; media served from shared storage. |
| 8 | Response caching | Must | `ETag`, `Cache-Control` and `Last-Modified` on Delivery and media responses; in-memory or Redis output cache with tag-based invalidation on publish, unpublish and media change; CDN purge hook via webhooks. |
| 9 | Content export and import | Must | CLI and Management API export of content, media references and settings per site to a portable JSON package; import with upsert by ID, dry run and a report; schema version checked on import. |
| 10 | Schema snapshot and diff | Must | Deterministic schema snapshot from code; CLI diff against a running instance to catch drift before deploy. |
| 11 | Backups guidance | Must | Documented backup and restore for each database provider and storage provider; a CLI command that bundles database export and media into one archive for small installs. |
| 12 | Rate limiting and request limits | Must | Per-IP and per-key limits, body size caps, upload caps, query complexity caps; defaults safe for public exposure. |
| 13 | Reverse proxy and TLS readiness | Must | Forwarded headers, path base support for hosting under a sub-path, HTTPS redirection and HSTS configurable; documented for Nginx, Caddy, YARP, Azure Front Door and Cloudflare. |
| 14 | Startup diagnostics | Must | One startup report listing providers, plugins, pending migrations, insecure defaults and misconfiguration, logged and visible in admin. |
| 15 | Supported platforms | Must | Linux and Windows containers, Azure App Service, AWS ECS, Kubernetes, bare `dotnet run`; .NET 10 LTS baseline. |
| 16 | Environment promotion | Should | Push and pull content between environments with transfer tokens, selective by type and site, secrets stripped, idempotent. |
| 17 | Redis provider pack | Should | Distributed cache, output cache, distributed locks and SignalR backplane configured with one connection string. |
| 18 | Read replicas for Delivery | Should | Route Delivery API reads to a replica connection string. |
| 19 | Metrics dashboards | Should | Grafana dashboard and alert examples for the exported metrics. |
| 20 | Database maintenance jobs | Should | Version pruning, trash purge, activity log retention, orphaned media cleanup as scheduled jobs with reports. |
| 21 | Helm chart and compose files | Should | Reference Kubernetes chart and Docker Compose with PostgreSQL, Redis and MinIO. |
| 22 | Secret rotation support | Should | Rotate signing keys, API key hashing salts and provider credentials without downtime. |
| 23 | Single-file executable mode | Could | Self-contained publish with embedded admin and SQLite for one-binary deployments. |
| 24 | Multi-tenancy with isolated data | Could | Tenant resolution by host or header, schema or database per tenant, tenant-scoped admin and keys; separate plugin. |
| 25 | MySQL and MariaDB provider | Could | Via Pomelo, community-maintained. |
| 26 | Serverless hosting | Could | Azure Functions and AWS Lambda guidance for the Delivery API; job runner replaced by platform schedulers. |
| 27 | SBOM and signed releases | Could | SPDX SBOM per release, signed NuGet packages and container images, published security policy. |
| 28 | MongoDB or document-store provider | Won't | Relational via EF Core only. |
| 29 | Hosted SaaS or managed cloud | Won't | Not offered by the project. |
| 30 | Usage telemetry to the project | Won't | None; no phone-home. |
| 31 | Built-in CDN or edge network | Won't | Cache headers and purge hooks only. |

## Decisions this category forces

- [ ] Migration strategy across three providers: one migration set with provider-conditional code, or one set per provider generated from the same model. Recommendation: per provider, generated by CI, because JSON and FTS differ too much for conditionals.
- [ ] Automatic migrations at startup: default on (small installs) or default off (production safety). Recommendation: on for SQLite, off otherwise, overridable.
- [ ] Output cache store: ASP.NET Core output caching with tag eviction (recommended) or a custom cache.
- [ ] Minimum supported database versions: PostgreSQL 15+, SQL Server 2019+ (JSON support), SQLite 3.45+. Confirm against JSON feature needs.
