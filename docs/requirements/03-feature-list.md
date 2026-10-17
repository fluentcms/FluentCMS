# Feature List (MoSCoW by milestone)

M = Must, S = Should, C = Could, W = Won't (this version). Milestone shows where the feature first ships. Detailed requirements are in 04-functional-requirements; feature IDs map to FR groups.

## Milestone 1 — Governance core

Operated by external agents over MCP. Humans govern through the review desk, rule book and generated forms.

| ID | Feature | Pri |
|----|---------|-----|
| F-SCH | Content types with typed fields; entity flag (fact source) with source/owner/validity on fields; relations between types | M |
| F-CNT | Entries with versions, draft/published state, references to entities; no copied facts | M |
| F-ENT | Entity registry: typed entities, relations, source and owner per field, validity windows, stale-fact detection, "where used" | M |
| F-CHG | Change sets: grouped edits with rationale and sources; diff view; atomic publish; one-click revert | M |
| F-RUL | Rule book as content: prose rules in four kinds (voice/style, factual, structural, permission/routing); versioned | M |
| F-CHK | Rule compilation: AI proposes a check per rule with test examples; owner confirms; deterministic vs LLM-judged labelled; enforced vs advisory labelled | M |
| F-RTE | Risk routing: explicit routing rules + AI risk score; N approvals per risk level; sampling with escalation | M |
| F-DSK | Review desk as home screen: queue, diff, reasons, sources, check results, approve/reject/edit, feedback-to-rule | M |
| F-MCP | MCP server exposing every capability as tools; per-agent credentials, scopes, rate and cost caps | M |
| F-AUD | Provenance and audit log: agent, model, prompt, sources, reviewer per change; immutable; exportable | M |
| F-API | Delivery API: published entries and entities, per-item provenance, Markdown per entry, site index for agents | M |
| F-FRM | Auto-generated forms from content types (fallback editor); human edits also create change sets | M |
| F-MED | Media library (upload, organize, reference from fields, alt text required by check) | M |
| F-USR | Users and sign-in; flat administrator role | M |
| F-EXT | Internal extension points for checks, tools, field types, storage, scorers (no public SDK yet) | M |
| F-SRC | Full-text search across entries and entities for agents and the desk | S |
| F-WHK | Outbound webhooks on publish/revert | C |

## Milestone 2 — Built-in agent and full admin

| ID | Feature | Pri |
|----|---------|-----|
| F-AGT | Built-in operator agent using the owner's model key/gateway; manual tasks, schedules, event triggers; runs under the same rule book and caps | M |
| F-TSK | Task library: audit published content, refresh stale facts, generate alt text, fix structural failures | M |
| F-ADM | Full admin UI: content, entities, media, users, settings, agents, rule book, logs | M |
| F-RBC | Roles: administrator, rule author, approver per area, fact owner; custom roles | M |
| F-NTF | Notifications: email and webhooks for pending review, failed checks, stale facts | M |
| F-WRK | Workflow steps per routing rule (e.g. legal approval for pricing) | S |
| F-LRN | Routing suggestions learned from approve/reject history (proposed, never auto-applied) | C |
| F-IMP | Import from external sources into entities (CSV/JSON mapping) | S |

## Milestone 3 — Scale out

| ID | Feature | Pri |
|----|---------|-----|
| F-I18 | Multi-language: locales per entry, translation change sets, locale-scoped rules and reviewers | M |
| F-MST | Multi-site: several sites in one instance, each with its own rule book, agents and users | M |
| F-SDK | Public plugin SDK for the M1 extension points; plugin install/update/disable | M |
| F-PRP | Fact propagation on revert: proposes a change set fixing all references to a corrected entity | S |
| F-DSC | AI-disclosure helpers: ready-made labels/snippets for frontends based on provenance | S |

## Won't (this version)

Site rendering/themes/page builder, hosted multi-tenant service, git-backed storage, v1 migration tools, visual editing, e-commerce checkout, comments. See 07-out-of-scope.
