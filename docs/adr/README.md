# Architecture Decision Records

Technical decisions for FluentCMS v2. Product decisions live in `docs/requirements/00-decision-record.md`. The process is described in ADR-0001.

| ADR | Title | Status |
| --- | --- | --- |
| [0001](0001-record-architecture-decisions.md) | Record architecture decisions | accepted |
| [0002](0002-target-dotnet-10-lts.md) | Target .NET 10 LTS | accepted |
| [0003](0003-repository-layout-and-branching.md) | Repository layout and branching | accepted |
| [0004](0004-solution-structure.md) | Solution structure | accepted |
| [0005](0005-persistence-ef-core-postgresql-sqlite.md) | Persistence with EF Core on PostgreSQL and SQLite | accepted |
| [0006](0006-admin-ui-blazor-interactive-server.md) | Admin UI with Blazor interactive server rendering | accepted |
| [0007](0007-mcp-server-official-sdk-and-agent-credentials.md) | MCP server on the official C# SDK with per-agent API keys | accepted |
| [0008](0008-llm-access-via-microsoft-extensions-ai.md) | Model access through Microsoft.Extensions.AI behind one internal gateway | accepted |
| [0009](0009-identity-aspnet-core-identity-agents-not-users.md) | Human sign-in with ASP.NET Core Identity; agents are not users | accepted |
| [0010](0010-in-database-search-and-job-queue.md) | In-database full-text search and a durable job queue | accepted |
| [0011](0011-admin-component-set-and-styling.md) | Admin component set and styling | accepted |
| [0012](0012-system-initiator-for-engine-driven-actions.md) | A system initiator for actions the product takes on its own | accepted |

Deferred to their own ADR when the work starts: CI and release pipeline, container image layout.
