# Non-Functional Requirements

Hard requirements (D13): agent security, provenance/audit, AI-disclosure, model independence. Others are expected but negotiable.

## SEC — Agent and platform security (hard)

Mapped loosely to OWASP Top 10 for Agentic Applications (2026).

- SEC-01 Each agent has a distinct credential; credentials are revocable, expiring and scoped (MCP-05). No shared "agent" account.
- SEC-02 Agents cannot approve, publish or change routing/permission rules, regardless of scope.
- SEC-03 Rule text, entry content, entity sources and media metadata are untrusted input to every LLM call; the system separates instructions from data (structured prompts, delimiting, and a check that flags instruction-like text inside content or rules).
- SEC-04 Per-agent caps: tool calls per period, LLM spend per period, items per change set, change sets per day. Exceeding a cap fails closed.
- SEC-05 Outbound network access from checks, tools and the built-in agent is allow-listed by the administrator.
- SEC-06 MCP remote transport requires authentication; tokens never appear in logs or URLs.
- SEC-07 Admin and delivery APIs follow standard web hardening (CSRF, rate limits, input validation, CSP); secrets stored encrypted at rest.
- SEC-08 Security-relevant events (credential created/revoked, cap exceeded, injection flagged, routing rule changed) are in the audit log and in a dedicated desk view.
- SEC-09 Plugins (M3) declare required permissions; the host enforces them.

## AUD — Provenance and auditability (hard)

- AUD-N1 Provenance is written in the same transaction as the change; a change without provenance cannot exist.
- AUD-N2 Audit log is append-only; deletions are retention-policy driven and themselves logged.
- AUD-N3 Any published item can be traced back through its change set to the prompt, model, agent, sources and approver within the UI and API.
- AUD-N4 Export of the full log and of a single item's history in JSON.

## DSC — AI-disclosure support (hard)

- DSC-01 Every published item carries a machine-readable provenance summary (AI involvement level, human review status, reviewer, date, sources) in the delivery API.
- DSC-02 "Human reviewed" is only set when a person approved the exact published version (not an earlier version of the set).
- DSC-03 Auto-published items (checks only) are explicitly marked as not human-reviewed.
- DSC-04 Reports: items published without human review, by type and period.

## MDL — Model and provider independence (hard)

- MDL-01 All LLM calls go through one internal abstraction; providers are configured by the owner (API key or gateway URL).
- MDL-02 Works with at least two provider families and one local/self-hosted option.
- MDL-03 Deterministic checks and the whole governance loop function with no model configured; only LLM-judged checks and risk scoring degrade (to "needs review").
- MDL-04 Model and version are recorded per call for reproducibility.

## PERF — Performance and scale (D26)

- PERF-01 Target per instance: 10k entries, 1k entities, 50 change sets/day, 15 concurrent users, 10 concurrent agents.
- PERF-02 Deterministic checks on a 200-item change set complete in under 10 s; LLM checks run asynchronously with progress.
- PERF-03 Delivery API reads under 100 ms p95 for a single resolved entry on target hardware.
- PERF-04 Change set diff for 200 items renders progressively.

## REL — Reliability and data

- REL-01 Publish and revert are transactional.
- REL-02 Backups: one-command export/import of a whole site (schema, content, entities, rules, media, log).
- REL-03 Soft delete with configurable retention before purge.

## OPS — Deployment and operations

- OPS-01 Single self-hostable deployment unit plus a database and file storage; container image provided.
- OPS-02 Configuration via environment/config file; no required external SaaS other than the model provider.
- OPS-03 Health, metrics and structured logs exposed for standard monitoring.
- OPS-04 Upgrades run schema migrations automatically and are reversible one version back.

## UX — Usability

- UX-01 A non-developer can write a rule and activate a check without reading documentation.
- UX-02 Review of a routine 10-item set takes under 2 minutes; keyboard-only review is possible.
- UX-03 Every check failure tells the agent or person exactly what to change.
- UX-04 Admin UI is responsive and accessible (WCAG 2.1 AA target).

## EXT — Extensibility

- EXT-N1 Core features use the public extension interfaces (no privileged internal paths).
- EXT-N2 Extension interfaces are versioned; breaking changes only at major versions.

## I18 — Internationalization (M3 content; UI from M1)

- I18-N1 Admin UI strings are localizable from M1; content localization per 04-functional-requirements I18.
- I18-N2 RTL layouts supported in the admin UI.

## LIC — Licensing and community

- LIC-01 MIT license; no proprietary dependencies required to run.
- LIC-02 Documentation in English; contribution guide, code of conduct, security policy from the first public release.
