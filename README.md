<p align="center">
  <picture>
    <source srcset="docs/resources/logo-dark.svg" media="(prefers-color-scheme: dark)">
    <img src="docs/resources/logo.svg" alt="FluentCMS" width="120">
  </picture>
</p>

<h3 align="center">FluentCMS v2: a headless CMS built for AI agents to operate and for a team to govern</h3>
<p align="center">Created by <a href="https://momentaj.com/">Momentaj</a>, a Toronto AI engineering firm.</p>

> **Status: pre-alpha.** v2 is a rewrite from scratch, in progress on this branch. Nothing runs yet.
> The previous version lives on the [`dev` branch](https://github.com/FluentCMS/fluentcms/tree/dev) and receives security fixes only. There is no migration path from v1 to v2.

## What v2 is

The agent does the work by default. People write the rules and review what matters before it goes live.

- **Agents operate the CMS through [MCP](https://modelcontextprotocol.io).** Every capability is a tool before it is a screen. Forms are the fallback for people.
- **The rule book is enforced.** Rules are written in plain language and compiled into checks that block violations. A rule that cannot be checked is labelled advisory and shown as such.
- **Facts live once.** Prices, dates, names and limits are typed entities with a source and an owner. Content references them and never stores a copy.
- **Every change is a change set.** Grouped edits with a diff, the agent's rationale and its sources. Routine sets pass on checks, risky sets wait for a person, large sets are sampled. A set is published and reverted atomically.
- **Every published item carries provenance.** Was AI involved, who reviewed it, and where the facts came from.

Agents never approve or publish by judgment. Only checks can auto-publish, and only where a routing rule allows it.

Delivery is headless: frontends read a delivery API. There is no built-in renderer, theme or page builder.

## Roadmap

| Milestone | Scope |
| --- | --- |
| [M1 Governance core](https://github.com/FluentCMS/fluentcms/milestone/2) | Content types, entities, change sets, rule book and checks, risk routing, review desk, MCP server, audit log, delivery API, generated forms, media, sign-in. Operated by external agents only. |
| [M2 Built-in agent and full admin](https://github.com/FluentCMS/fluentcms/milestone/3) | Scheduled and triggered tasks on the owner's model key, full admin UI, roles, email and webhook notifications, OAuth for hosted assistants. |
| [M3 Scale out](https://github.com/FluentCMS/fluentcms/milestone/4) | Multi-language, multi-site, public plugin SDK. |

## Documentation

- [Requirements](docs/requirements/): decision record, vision and principles, personas and scenarios, feature list, functional and non-functional requirements, glossary, out of scope.
- [Architecture decision records](docs/adr/README.md): .NET 10, branching, solution structure, persistence, admin UI, MCP server, model access, identity, search and jobs, component set.
- [Contributing](CONTRIBUTING.md) and [Security policy](SECURITY.md).

## Stack

.NET 10, EF Core with PostgreSQL for production and SQLite for development, Blazor for the admin UI, the official C# MCP SDK, and Microsoft.Extensions.AI for model access. Self-hosted, MIT licensed, no required external service other than a model provider.

## Community

- [Discord](https://discord.gg/WyqYuC6YbY)
- [Announcement](https://github.com/FluentCMS/fluentcms/issues/2414): what v2 is, why the restart, and how to get involved.

## License

[MIT](LICENSE)
