# ADR-0002: Target .NET 10 LTS

- Status: accepted
- Date: 2026-10-16
- Related: OPS-01, OPS-02, LIC-01, P10

## Context

v2 is a fresh codebase (D01) delivered as a single self-hostable unit plus a database and file storage (OPS-01). FluentCMS has always been a .NET project, the maintainer's expertise is .NET, and the libraries the architecture depends on (EF Core, Blazor, the C# MCP SDK, Microsoft.Extensions.AI) are .NET first.

At the time of writing, .NET 10 is the current long-term-support release, supported until November 2028. .NET 9 left support in May 2026. .NET 11, a short-term release, ships in November 2026.

## Decision

Target `net10.0` and C# 14 in every project. Pin the SDK with `global.json` (`10.0.x`, `rollForward: latestFeature`). Stay on long-term-support releases: evaluate .NET 12 when it ships in November 2027 and move within its first months. Skip short-term releases unless one carries a blocking feature.

Ship as a container image and as a framework-dependent build. A self-contained build can be added if people ask for it.

## Consequences

- Patch support through M1 to M3 without a framework migration.
- Blazor render modes, EF Core 10 JSON column support and Microsoft.Extensions.AI are available without preview packages.
- Contributors need the .NET 10 SDK. `global.json` turns a version mismatch into an immediate error instead of a subtle one.

## Alternatives considered

- .NET 9: out of support.
- .NET 11 previews: a moving target, and a short-term release would force another move within a year.
- Another runtime (Node, Go): discards the maintainer's expertise, the existing community and the project's identity.
