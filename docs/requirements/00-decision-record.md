# FluentCMS (AI-first) — Decision Record

Decisions taken in the requirements discussion, 2026-10-16. Later chats should treat these as settled unless Amir revises them here.

| # | Topic | Decision |
|---|-------|----------|
| D01 | Positioning | Fresh product under the FluentCMS name. No v1 compatibility or migration obligation. |
| D02 | Delivery | Headless first. No built-in page rendering in v1. |
| D03 | Primary persona | Small content/marketing team (3–15 people), one instance per team. |
| D04 | Reference scenario | Product catalog site (specs and prices from a source system). Secondary: company marketing site, documentation site. |
| D05 | Agent runtime | Ships a built-in operator agent (owner's model key/gateway, schedules, triggers) **and** an MCP server for external agents. Built-in agent is M2; M1 is MCP-only. |
| D06 | Storage | Content lives inside the CMS with its own versioning and change proposals. Git is not part of the model. |
| D07 | Content model | Structured content types with typed fields. |
| D08 | Facts | Typed entities with relations. Entity types are content types flagged as fact sources; one schema mechanism. |
| D09 | Risk routing | Explicit routing rules first; AI risk scoring for the remainder. No learned routing in v1. |
| D10 | Legacy features kept | Multi-language, media library, multi-site, plugin/extension system. |
| D11 | Human UI | Full admin like v1 (M2). M1 ships governance UI + auto-generated forms from content types. |
| D12 | Phasing | M1 governance core → M2 built-in agent + full admin → M3 i18n, multi-site, public plugin SDK. Internal extension points exist from M1. |
| D13 | Hard NFRs | Agent security (OWASP agentic), full provenance/audit, AI-disclosure records, model/provider independence. |
| D14 | Commercial intent | Open source, self-hosted, personal-branding goal. No hosted offering. MIT license (assumed). |
| D15 | Unit of review | Change set: related edits grouped with one rationale; reviewed, published and reverted atomically. |
| D16 | Rule kinds (M1) | Voice & style, factual constraints, structural/schema, permission & routing. |
| D17 | Rule → check | Owner writes prose; AI proposes a check (deterministic where possible, LLM-judged otherwise) with test examples; owner confirms before activation. |
| D18 | Agent identity | Per-agent credentials, scopes, rate/cost caps; agents named in the audit log. |
| D19 | Delivery API (M1) | Published content, entities, per-item provenance, Markdown per entry, site index for agents. |
| D20 | Sampling | Configurable sample per risk level; one rejected sample escalates the whole set to full review. |
| D21 | Plugin extension points | Checks, MCP tools, field types/storage providers, review desk & workflow (scorers, steps, notifications). |
| D22 | Rollback | One-click revert of a change set; correction captured as feedback proposing a rule/check update. Fact propagation deferred. |
| D23 | Roles | Flat in M1 (all users are administrators). Rule author, per-area approver, fact owner roles in M2. |
| D24 | Approvals | Routing rules may require N approvals per risk level or area; default 1. |
| D25 | Notifications | In-app only in M1. Email/webhooks/Slack in M2. |
| D26 | Scale target | Thousands of entries, hundreds of entities, tens of change sets/day per instance. |
| D27 | Relation to other ideas | Not the same effort as the small personal-website CMS idea. |

## Open questions (not yet decided)

- Which LLM-judged checks count as "enforced" for AI Act / Google purposes, versus advisory.
- Whether a change set can mix entities and content, or fact changes always travel in their own set.
- Default sample sizes per risk level.
- Where the line between routine and risky sits for the reference scenario (price change is risky; typo is routine; what about a spec field?).
