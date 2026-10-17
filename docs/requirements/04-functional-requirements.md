# Functional Requirements

Format: `ID (milestone) requirement`. "Shall" = required; "should" = expected unless costly. Every requirement under a feature applies to agents via MCP tools (P1) and to people via UI where one exists.

## SCH — Schema (content types and entity types)

- SCH-01 (M1) Administrators and agents shall define content types with a name, slug, fields and relations.
- SCH-02 (M1) Field types at minimum: text, rich text/Markdown, number, boolean, date/time, enum, media, reference (to entries of a type), list of any of these, JSON.
- SCH-03 (M1) A content type may be flagged as an **entity type** (fact source). Fields on an entity type carry: value, source (URL, file, person or system), owner (user), valid-from, valid-until, last-verified.
- SCH-04 (M1) Content types may reference entity types; a reference field can target an entity or a specific field of an entity (e.g. `Product.price`).
- SCH-05 (M1) Schema changes are versioned and create a change set like any other change.
- SCH-06 (M1) The schema shall be readable by agents in a machine-readable form with field descriptions and examples.
- SCH-07 (M1) Every type is scoped to a site, even when only one site exists (prepares MST).

## CNT — Content and versioning

- CNT-01 (M1) Entries have states: draft, in review, published, reverted, archived.
- CNT-02 (M1) Every write creates a new version; versions are never overwritten.
- CNT-03 (M1) A field that represents a fact shall be a reference to an entity field; checks reject literal values where a fact reference is required (per rule).
- CNT-04 (M1) Resolved views: an entry can be read with references resolved to current published entity values.
- CNT-05 (M1) "Where used" is available for every entity, entity field and media asset.
- CNT-06 (M1) Entries, entities and media are soft-deleted and restorable within the audit window.

## ENT — Entities (facts)

- ENT-01 (M1) Entities support relations to other entities (one-to-many, many-to-many).
- ENT-02 (M1) An entity field without a source is flagged "unsourced"; a routing rule may treat unsourced facts as risky or blocking.
- ENT-03 (M1) A field past valid-until or not verified within a configurable period is flagged "stale"; stale facts appear on the desk as a work item.
- ENT-04 (M1) Changing an entity field opens a change set that includes every published entry referencing it (resolved diff per page).
- ENT-05 (M2) Owners can confirm a value without changing it (resets last-verified).
- ENT-06 (M2) Import mapping from CSV/JSON into entity types, recording the file as source.

## CHG — Change sets

- CHG-01 (M1) A change set contains one or more operations (create/update/delete on entries, entities, media, schema, rules), a rationale, a list of sources, and the initiator (agent or user).
- CHG-02 (M1) Agents shall open a change set explicitly or let the server group consecutive operations within a session into one.
- CHG-03 (M1) The set shows a field-level diff per item and a resolved diff for pages affected by entity changes.
- CHG-04 (M1) States: open, checks running, checks failed, awaiting review, approved, published, rejected, reverted.
- CHG-05 (M1) Publish is atomic: all items or none.
- CHG-06 (M1) Revert is one action and creates a new change set that restores previous versions of all items.
- CHG-07 (M1) A reviewer can edit items inside a set before approving; the edit is recorded as a human operation in the same set.
- CHG-08 (M1) Rejection requires a reason; the reason is stored and offered as rule feedback (RUL-08).
- CHG-09 (M1) A set may mix entity and content changes (open question in 00-decision-record if this should be restricted).

## RUL — Rule book

- RUL-01 (M1) Rules are content: created, versioned, reviewed and published through change sets.
- RUL-02 (M1) Rule kinds: voice & style, factual, structural/schema, permission & routing.
- RUL-03 (M1) A rule has prose text, scope (site, types, fields, locales), severity (block, warn), and status (draft, active, retired).
- RUL-04 (M1) On save, the system proposes one or more checks implementing the rule (CHK) with positive and negative examples; the author confirms, edits or rejects each.
- RUL-05 (M1) Rules that produce no check are marked **advisory** and are shown to agents as guidance only; the desk shows which rules are enforced.
- RUL-06 (M1) Agents can read the active rule book (full text and compiled checks) for their scope before acting.
- RUL-07 (M1) Rule text is treated as untrusted content for prompt-injection purposes (see NFR-SEC).
- RUL-08 (M1) From a rejection, edit or revert, the desk offers "propose a rule"; the proposal enters the rule book as a draft with the triggering case as an example.

## CHK — Checks

- CHK-01 (M1) Check types: deterministic (regex/term lists, schema, length, link validity, required alt text, fact-reference required, value range, source present, staleness) and LLM-judged (tone, reading level, claim detection, contradiction with entities).
- CHK-02 (M1) Every check has test examples; a check cannot be activated until its examples pass.
- CHK-03 (M1) Checks run on every change set before routing; results (pass/warn/fail, evidence, cost) are stored with the set.
- CHK-04 (M1) Agents can run checks on a draft without opening a set (pre-flight tool).
- CHK-05 (M1) LLM-judged checks record model, prompt and response for the audit log.
- CHK-06 (M1) Check execution is an extension point (EXT).

## RTE — Risk routing and sampling

- RTE-01 (M1) Routing rules are permission & routing rules that map conditions (type, field, section, change kind, agent, check results, risk score) to a disposition: auto-publish, require N approvals, block.
- RTE-02 (M1) Explicit routing rules are evaluated first; unmatched sets receive an AI risk score (0–100) with reasons; thresholds per disposition are configurable.
- RTE-03 (M1) Risk scoring considers at least: factual claims, prices/legal/health/finance content, tone drift from rules, size of blast radius (pages affected), agent trust level.
- RTE-04 (M1) Sampling: per risk level, a sample size or percentage; the desk shows the sample first; one rejected sample item escalates the set to full review.
- RTE-05 (M1) Required approvals per risk level default to 1 and are configurable (N).
- RTE-06 (M1) Every routing decision is logged with the matched rule or score and reasons.
- RTE-07 (M2) Workflow steps (ordered approvals by role/area) attach to routing rules.
- RTE-08 (M2) The system may suggest routing rules from approve/reject history; suggestions are never auto-applied.

## DSK — Review desk

- DSK-01 (M1) The desk is the default landing screen after sign-in.
- DSK-02 (M1) Queue views: awaiting me, awaiting anyone, checks failed, stale facts, recently published, reverted.
- DSK-03 (M1) Set view: rationale, sources (openable), check results, routing decision, diff per item, sample selector, approve/reject/edit/revert actions.
- DSK-04 (M1) Keyboard-first review for batches.
- DSK-05 (M1) In-app notification counts only (no email in M1).
- DSK-06 (M2) Email and webhook notifications per event type and user preference.

## MCP — Agent interface

- MCP-01 (M1) The server exposes tools for every capability in SCH, CNT, ENT, CHG, RUL, CHK, DSK (read-only for review decisions), MED, and search.
- MCP-02 (M1) Tool descriptions, input schemas and error messages are written for LLM consumption and include rule-book hints relevant to the call.
- MCP-03 (M1) Resources expose the schema, active rules, and an entry/entity by URI.
- MCP-04 (M1) Transports: local (stdio) and remote (HTTP) from one binary.
- MCP-05 (M1) Each agent has its own credential, display name, scopes (types, fields, operations, sites), and caps (calls per period, LLM spend per period, max items per change set).
- MCP-06 (M1) Agents can never approve or publish; the only exception is a routing rule that auto-publishes on checks.
- MCP-07 (M1) Every tool call is logged with agent identity and, where provided, the agent's declared model and task id.
- MCP-08 (M1) Long operations (bulk writes) return a change set id and progress, not a blocking call.

## AGT — Built-in operator agent (M2)

- AGT-01 The agent runs with the owner's model API key or OpenAI/Anthropic-compatible gateway; multiple models configurable per task.
- AGT-02 Task types: manual run, schedule (cron-like), event trigger (entity changed, stale fact, external webhook).
- AGT-03 The agent acts through the same MCP tools and under the same identity/scope/cap model as external agents.
- AGT-04 Built-in task library per F-TSK; tasks are editable prompts plus tool allow-lists.
- AGT-05 Every run is logged with inputs, tool calls, model, tokens, cost and resulting change sets.
- AGT-06 A run can be stopped; partial work remains as an open change set.

## AUD — Provenance and audit

- AUD-01 (M1) Every operation records: timestamp, initiator (agent/user), agent credential, model and prompt hash where applicable, sources, change set id, previous version id.
- AUD-02 (M1) Every published item exposes a provenance summary: AI-involved (yes/no/partly), last human reviewer, review date, sources list.
- AUD-03 (M1) The log is append-only and exportable (JSON/CSV) by date range.
- AUD-04 (M1) Prompts used by LLM checks and (M2) agent runs are stored verbatim with retention settings.

## API — Delivery API

- API-01 (M1) Read-only HTTP API for published entries and entities with filtering, pagination and reference resolution.
- API-02 (M1) Each item carries its provenance summary (AUD-02).
- API-03 (M1) Each entry has a Markdown representation at a stable URL; a site index lists all published entries for agents.
- API-04 (M1) Preview tokens allow reading an unpublished change set's resolved content.
- API-05 (M1) Outbound webhook on publish and revert (C in M1, M in M2).

## FRM — Generated forms (fallback editor)

- FRM-01 (M1) Every content and entity type gets an auto-generated create/edit form from its schema.
- FRM-02 (M1) Human edits open change sets and pass through checks and routing like agent edits.
- FRM-03 (M1) Forms show the active rules for the field being edited.

## ADM — Full admin UI (M2)

- ADM-01 Screens for content, entities, media, users/roles, agents and credentials, rule book, routing, audit log, settings.
- ADM-02 Every admin action is also available as a tool (P1) and creates change sets where content, schema or rules change.
- ADM-03 Agent and credential management: create, scope, cap, revoke, view usage and spend.

## MED — Media

- MED-01 (M1) Upload, folders/tags, metadata, usage tracking.
- MED-02 (M1) Alt text and caption fields; "alt text required" is a built-in structural check.
- MED-03 (M1) Agents can upload via tool and attach to fields.
- MED-04 (M1) Storage provider is an extension point (local disk first).

## USR — Users and access

- USR-01 (M1) Email/password sign-in and at least one external identity option; all users are administrators.
- USR-02 (M2) Roles: administrator, rule author, approver (scoped by types/sections/locales), fact owner; custom roles.
- USR-03 (M2) Approval eligibility is role- and scope-based; a user cannot approve a set they initiated via forms.

## EXT — Extension points

- EXT-01 (M1) Internal interfaces for: check types, MCP tools, field types, storage providers (content, media, search), risk scorers, notification channels.
- EXT-02 (M1) Core features are implemented through these interfaces (dogfooding).
- EXT-03 (M3) Public plugin SDK, packaging, install/update/disable, permission declaration per plugin.

## I18 — Multi-language (M3)

- I18-01 Locales per site; entries have per-locale versions sharing one entity reference set.
- I18-02 Translation change sets per locale, reviewable by locale-scoped approvers; rules can be locale-scoped.
- I18-03 Source-locale change opens "needs retranslation" work items.

## MST — Multi-site (M3)

- MST-01 Many sites per instance; each with its own types, rule book, agents, users and delivery API namespace.
- MST-02 Entity types may be shared across sites by explicit opt-in.

## SRC — Search

- SRC-01 (M1) Full-text and structured search over entries and entities, available as a tool and in the desk.
