# ADR-0004: Solution structure

- Status: accepted
- Date: 2026-10-16
- Related: P1, P9, OPS-01, EXT-01, EXT-02, EXT-N1, MCP-04, UX-03

## Context

v1 grew to more than twenty projects: a repository project per database, a provider project per concern, a plugin project per feature. That spread made the build slow and the dependency direction unclear. v2 has a few hard rules the structure must make easy to follow: every capability is a tool before it is a screen (P1), core features go through the same extension interfaces a plugin would use (EXT-02, EXT-N1), and the product ships as one deployable (OPS-01) that can also run as a local MCP process (MCP-04).

## Decision

A modular monolith in one solution with seven projects:

| Project | Holds | May reference |
| --- | --- | --- |
| `FluentCMS.Core` | Domain model, application services (the use cases tools and screens call), extension interfaces (check types, tool providers, field types, storage, risk scorers, notification channels), and the built-in implementations of those interfaces | nothing in the solution |
| `FluentCMS.Infrastructure` | EF Core persistence and migrations, local-disk media storage, search index, job queue, LLM provider wiring | Core |
| `FluentCMS.Mcp` | MCP tools and resources mapped onto Core application services; transports | Core |
| `FluentCMS.Api` | Delivery API, Markdown and site index endpoints, preview tokens, outbound webhooks | Core |
| `FluentCMS.Admin` | Blazor components: review desk, rule book, generated forms, media, users, agent credentials | Core |
| `FluentCMS.Host` | ASP.NET Core host composing everything; the deployable. `fluentcms` runs the web host; `fluentcms mcp` runs the stdio transport against the same database | all of the above |
| `tests/FluentCMS.Tests`, `tests/FluentCMS.IntegrationTests` | Unit tests against Core; integration tests against SQLite in-process and a PostgreSQL container | as needed |

Rules:

- Mcp, Api and Admin depend only on Core. They receive Infrastructure through dependency injection in Host. A screen or tool that needs something Core does not offer is a Core change first.
- Built-in checks, field types and tools live in Core under a `Builtin` namespace and register through the same interfaces plugins will use in M3. There is no privileged path.
- Application services return results, not exceptions, for expected failures (check failed, cap exceeded, scope denied), so tools can turn them into messages an LLM can act on (UX-03, MCP-02).
- No NuGet packages are published in M1. The public SDK (M3) decides what becomes a package.

## Consequences

- Fast build, obvious dependency direction, one place that defines what the product can do.
- Adding a database or storage backend is a new implementation inside Infrastructure, not a new project, until there is a reason to split.
- Host carries two entry modes; the stdio mode must stay free of web-only assumptions.

## Alternatives considered

- One project: simplest, but nothing stops Admin reaching into persistence, and P1 becomes a convention rather than a compile error.
- v1-style project per provider and per plugin: the problem being solved.
- Services split by process: wrong scale for 3 to 15 users and one instance (P9).
