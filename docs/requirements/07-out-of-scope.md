# Out of Scope

Explicitly not part of this version. "Later" items are plausible after M3; "Never" items conflict with the vision.

## Later

| Item | Why not now |
|------|-------------|
| Built-in site rendering, themes, page builder | Headless first (D02); frontends render from the delivery API. |
| Hosted / multi-tenant SaaS | Self-hosted OSS only (D14); multi-site in M3 is one team's sites, not tenants. |
| Learned routing (auto-applied) | Suggestions only (RTE-08); auto-application risks silent drift. |
| Fact propagation on revert | F-PRP, M3 should. |
| Visual/in-context editing | Depends on a renderer. |
| Marketplace for plugins | After the SDK (M3). |
| Enterprise SSO/SCIM, separation-of-duties certifications | Beyond the small-team persona. |
| Real-time collaboration in forms | Forms are the fallback, not the primary workflow. |
| Full workflow engine (parallel branches, SLAs, escalations by time) | M2 ships ordered steps only. |
| Analytics / content performance | Not a governance concern. |

## Never (for this product)

| Item | Why |
|------|-----|
| v1 FluentCMS migration or compatibility | D01. |
| Git-backed content storage | D06; the CMS owns versions and change sets. |
| Agents approving or publishing by judgment | Only checks auto-publish; a person approves risky sets (P4, SEC-02). |
| Storing a copy of a fact in content | P3. |
| Dependence on a single model vendor or assistant | P6, MDL. |
| General-purpose agent framework / orchestration product | The built-in agent runs CMS tasks only; orchestration belongs to external agents. |
| E-commerce transactions, comments, forums, forms-as-a-service | Not a CMS governance problem. |
| Generating content at scale with no human gate as a selling point | Conflicts with the vision and with search/AI-Act guidance the idea is built on. |
