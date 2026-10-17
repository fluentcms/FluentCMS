# Personas and Scenarios

## Team shape (primary persona)

A small content/marketing team, 3–15 people, running one instance. In M1 everyone is an administrator; the roles below describe responsibilities, and become enforceable roles in M2.

| Role | Responsibility | Typical day |
|------|----------------|-------------|
| Administrator | Sites, content types, agents, integrations, users | Connects an agent, defines types, sets caps |
| Rule author (editor-in-chief) | Owns the rule book, voice, routing thresholds | Writes a rule, reviews the compiled check and its examples, activates it |
| Reviewer / approver | Approves change sets in an area | Works the review desk; approves, rejects with reason, or edits |
| Fact owner | Owns specific entities | Confirms fact changes, clears stale-fact alerts |
| Developer (occasional) | Integrates the delivery API, writes plugins (M3) | Wires the frontend, adds a custom check |

## Agent personas

| Agent | How it connects | Trust |
|-------|-----------------|-------|
| Coding/desktop agent (Claude Code, Cursor, etc.) | MCP over stdio/HTTP with its own credential | Scoped to specific types; cannot approve |
| Hosted assistant (ChatGPT, Claude.ai connectors) | Remote MCP with its own credential | Same |
| Custom pipeline agent (catalog sync, nightly audit) | Remote MCP, headless credential | Narrow scopes, strict caps |
| Built-in operator agent (M2) | In-process, owner's model key | Runs scheduled/triggered tasks under the same rule book |

## Reference scenario: product catalog site

**Context.** A company sells a few hundred products. Specs and prices come from a source system (PIM/ERP export). The site has product pages, category pages, comparison pages and a blog. A frontend team renders it from the delivery API.

**Entities.** `Product` (SKU, name, price, currency, specs…), `Category`, `Spec` definitions, `Region` pricing. Each entity field has a source (the export, a URL, a person) and an owner.

**Content types.** `ProductPage`, `CategoryPage`, `ComparisonPage`, `BlogPost`. Pages reference entities; they never store a price.

### S1 — Catalog sync (M1, external agent)
1. A pipeline agent reads the nightly export and calls MCP tools to update `Product` entities.
2. The CMS groups the entity updates and every page that references them into one change set with a diff and the export file as source.
3. Routing rule: price changes are risky → needs 1 approval. Spec typo fixes are routine → pass on checks.
4. Reviewer samples 10% of 180 price changes; all pass; set publishes atomically. One mismatch would have escalated the whole set.

### S2 — New product launch (M1)
1. Marketer asks a desktop agent: "Create a page for SKU 4411 in our voice."
2. Agent creates the `Product` entity (from the spec sheet it was given, recorded as source) and a `ProductPage` referencing it.
3. Checks: banned claims ("best in class") fail → agent revises. Missing alt text fails → agent adds. Price without source fails → agent attaches source.
4. Change set lands on the desk with reasons and sources; approved; published.

### S3 — Wrong price shipped (M1)
1. A reviewer notices a wrong regional price on the live site.
2. One click reverts the change set that introduced it.
3. The desk asks "what rule would have caught this?"; reviewer writes "regional prices must not differ from base price by more than 30% without a note"; AI compiles a check; rule author activates it.

### S4 — Nightly audit (M2, built-in agent)
A scheduled task re-checks all published pages against the current rule book and entity values, and opens change sets for drift.

### S5 — Translation (M3)
A locale is added; the agent proposes translations as change sets per locale; a reviewer with that locale samples them.

## Secondary scenarios (covered by the same requirements)

- **Company marketing site.** Facts = pricing plans, customer logos and names, feature availability, dates. Risky = pricing page, legal pages, customer names.
- **Documentation site.** Facts = product versions, limits, API names. Agent updates pages when a version entity changes.
