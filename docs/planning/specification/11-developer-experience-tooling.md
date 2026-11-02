# 11. Developer experience & tooling — approved

A developer should go from `dotnet new` to a running admin with a typed client in under ten minutes, and from a schema change to regenerated C# and TypeScript types in one command. The second audience is AI coding agents: the repo, docs and SDK ship with agent instructions, because most developers evaluating a CMS in 2026 do so with Copilot or Claude open.

| # | Feature | Tier | What it means |
| --- | --- | --- | --- |
| 1 | Project templates | Must | `dotnet new fluentcms` (API host with admin), `fluentcms-admin` (standalone admin), `fluentcms-plugin`; options for database provider and sample content types. |
| 2 | CLI | Must | `fluentcms` global tool: new, migrate, schema snapshot and diff, generate types (C#, TypeScript, OpenAPI), export and import, create API key and admin user, run jobs, mcp proxy. |
| 3 | Type generation | Must | C# models and typed client, TypeScript types and client, from the running schema or from code without a running instance; MSBuild target to regenerate on build. |
| 4 | Fast local loop | Must | `dotnet watch` with hot reload for API and admin; SQLite default needing no services; schema changes apply without restart in development. |
| 5 | Seed data and sample project | Must | Seed API and CLI for sample content; a sample site with a few types, media and users that templates can include. |
| 6 | Test host | Must | `FluentCmsTestHost` that boots the CMS in-process with SQLite for integration tests; fixtures for users, keys and content; used by the project's own tests and by plugin authors. |
| 7 | Documentation site | Must | Getting started, concepts, guides per audience (frontend developer, .NET developer, editor, administrator), API reference generated from OpenAPI and SDK XML docs, upgrade guides; versioned per release. |
| 8 | `llms.txt` and agent-readable docs | Must | Full documentation as Markdown with `llms.txt` and `llms-full.txt`; docs served to agents through an MCP docs resource. |
| 9 | Agent skills and repository instructions | Must | `AGENTS.md`, Copilot and Claude instructions in the repo and templates; skills for scaffolding content types, plugins and frontends against the SDK. |
| 10 | Frontend starter kits | Must | Next.js, Astro and Blazor starters using the TypeScript or C# SDK with preview and webhooks wired. |
| 11 | Semantic versioning and compatibility policy | Must | Published policy: SDK breaking changes only in majors, deprecations one minor ahead, supported versions table, LTS line aligned with .NET LTS. |
| 12 | Upgrade tooling | Must | Pre-upgrade check command reporting breaking changes, obsolete usage and plugin compatibility; migration notes per release. |
| 13 | Analyzers | Must | Roslyn analyzers for schema attributes (invalid combinations, missing indexes on filtered fields), SDK obsoletion and plugin misuse. |
| 14 | Error messages with links | Must | Startup, validation and API errors carry stable codes and documentation links. |
| 15 | Playground | Should | Hosted or local sandbox with sample data for trying the API, GraphQL and MCP without installing. |
| 16 | Schema designer in dev mode | Should | Admin UI that writes C# schema files in development (category 1, item 20). |
| 17 | VS Code and Rider snippets | Should | Snippets and file templates for content types, components, hooks and plugins. |
| 18 | Deployment recipes | Should | Step-by-step guides for Azure App Service, Azure Container Apps, AWS ECS, Fly.io, Hetzner with Docker Compose. |
| 19 | Migration guides from other CMSs | Should | Import tooling and guides for Strapi, Umbraco, Squidex and WordPress content. |
| 20 | Public roadmap and RFC process | Should | GitHub Discussions RFCs for SDK changes; roadmap page per release. |
| 21 | Benchmarks | Could | Published, reproducible API and admin benchmarks per release. |
| 22 | Browser devtools extension | Could | Inspect Delivery API calls and preview state from a frontend. |
| 23 | More SDK languages | Could | Python and Go clients generated from OpenAPI. |
| 24 | Interactive tutorial in the admin | Could | Guided first-run that creates a type, an entry and a key. |
| 25 | Low-code or no-code builder as the primary path | Won't | Code-first is the primary path. |
| 26 | GUI installer | Won't | Templates, CLI and container only. |
| 27 | Support for .NET versions out of Microsoft support | Won't | Current LTS and STS only. |

## Decisions this category forces

- [ ] Documentation stack: Docusaurus, Starlight (Astro) or a Blazor static site. Recommendation: Starlight for Markdown-first docs with `llms.txt` support; dogfooding a Blazor docs site is secondary to docs quality.
- [ ] Type generation source: from the running instance's OpenAPI only, or also from code via a source generator at build time. Recommendation: both; source generator for C# in the host, OpenAPI for everything else.
- [ ] CLI distribution: `dotnet tool` only, or also standalone binaries for frontend developers without the .NET SDK. Recommendation: both; the TypeScript audience should not need the .NET SDK to generate types.
- [ ] Repository layout: monorepo for core, plugins, admin, SDKs and templates (recommended) or separate repositories per package family.
