# Glossary

| Term | Meaning |
|------|---------|
| Advisory rule | A rule that cannot be compiled into a check; shown to agents and reviewers as guidance, never blocks. |
| Agent | Any software acting on the CMS through MCP under its own credential: external (Claude Code, ChatGPT connector, custom pipeline) or built-in (M2). |
| Approver | A person who can approve a change set within a scope (everyone, in M1). |
| Auto-publish | Disposition where a change set publishes once all checks pass, with no human approval; always marked not human-reviewed. |
| Blast radius | Number of published items affected by a change set (counts resolved references). |
| Cap | Per-agent limit: tool calls, LLM spend, items per set, sets per day. |
| Change set | The unit of review: grouped operations with rationale, sources, initiator, check results and routing decision; published and reverted atomically. |
| Check | An executable test derived from a rule; deterministic or LLM-judged; has test examples; blocks or warns. |
| Content type | Schema for entries: fields and relations. |
| Delivery API | Read-only API serving published entries, entities, provenance and Markdown to frontends and agents. |
| Disposition | Outcome of routing: auto-publish, require N approvals, block. |
| Entity | A record of an entity type; the single home of a fact (e.g. a Product). |
| Entity type | A content type flagged as a fact source; its fields carry source, owner and validity. |
| Enforced rule | A rule with at least one active check. |
| Escalation | Sampling outcome when any sampled item is rejected: the whole set goes to full review. |
| Fact | A value that must be true in the world (price, date, name, limit); lives once in an entity field and is referenced, never copied. |
| Fact owner | Person responsible for an entity's values and sources (role in M2). |
| Generated form | Auto-built create/edit UI from a content type; the human fallback to tools. |
| Human reviewed | Provenance flag set only when a person approved the exact published version. |
| LLM-judged check | A check evaluated by a model (tone, claims, contradictions); records model, prompt and response. |
| MCP | Model Context Protocol; the agent-facing interface of the CMS. |
| Provenance | Record of who/what produced a change: agent, model, prompt, sources, reviewer, timestamps. |
| Reference | A field value that points to an entity or entity field instead of storing a copy. |
| Resolved view | An entry read with its references replaced by current published entity values. |
| Review desk | The home screen: queues of change sets, stale facts and failed checks, with approve/reject/edit/revert. |
| Risk score | AI-assigned 0–100 with reasons for sets not matched by explicit routing rules. |
| Routine / risky | Routing outcome: routine passes on checks (or light sampling); risky waits for N approvals. |
| Routing rule | A permission & routing rule mapping conditions to a disposition. |
| Rule book | All rules of a site, stored and versioned as content. |
| Rule kinds | Voice & style; factual; structural/schema; permission & routing. |
| Sampling | Reviewing a configurable subset of a large set, with escalation on any rejection. |
| Scope (agent) | Types, fields, operations and sites an agent credential may touch. |
| Site | A governance boundary: its own types, rule book, agents, users and API namespace (one per instance until M3). |
| Source | Where a fact came from: URL, file, system export, or a named person. |
| Stale fact | Entity field past valid-until or not verified within the configured period. |
| Unsourced fact | Entity field with no source; may be routed as risky or blocked by rule. |
| Where used | List of entries and sets referencing an entity, entity field or media asset. |
