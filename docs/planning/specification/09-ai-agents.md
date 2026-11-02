# 9. AI & agents — approved

Two first-party plugins. The MCP plugin makes FluentCMS a first-class tool for agents: in-process, authenticated by the same authorization server, bounded by the same RBAC, drafting by default and tagged in every log. The AI assist plugin gives editors opt-in help inside the editor through Microsoft.Extensions.AI with the host's own model provider and keys. Neither runs the site; humans publish. Every vendor shipped an MCP server by late 2026, so the differentiators are the safety pattern, the agent-context layer and shipping the audit tagging free.

| # | Feature | Tier | What it means |
| --- | --- | --- | --- |
| 1 | In-process MCP server | Must | Streamable HTTP endpoint at `/mcp` on the API host, built on the official C# MCP SDK; enabled per site; no sidecar process. |
| 2 | OAuth 2.1 for MCP clients | Must | Authorization Code + PKCE and dynamic client registration against the built-in authorization server; protected resource metadata so Claude, ChatGPT, Copilot and IDE clients connect without manual tokens; API keys as an alternative for scripts. |
| 3 | Agent acts as a principal | Must | Every MCP call runs as the authenticated user or service account; the same RBAC, row-level rules, locale and site scoping apply; tool lists are filtered to what the principal may do. |
| 4 | Content tools | Must | `list`, `get`, `search`, `create_draft`, `update_draft`, `discard_draft`, `delete` (trash) per content type, generated from the schema with typed input schemas, filters, locale and site parameters. |
| 5 | Publish as a separate tool and permission | Must | `publish` and `unpublish` exist but require the publish permission; by default agent principals draft only. Scheduling available through the same tool. |
| 6 | Media tools | Must | List, search, get metadata, upload from URL or base64, update alt and caption; delete requires confirmation. |
| 7 | Schema resources | Must | Content types, fields, validation rules and relations exposed as MCP resources in a compact form, filtered by permissions, so agents model content correctly without trial and error. |
| 8 | Safety pattern on destructive tools | Must | `dryRun` default on delete and bulk operations, returning what would change; confirmation token required to execute; no bulk delete by filter without an explicit cap. |
| 9 | Origin tagging | Must | Every write made through MCP carries origin `Mcp`, the client ID and the principal, into versions, the activity log, hooks and webhooks. |
| 10 | Per-type agent instructions | Must | Authoring guidelines per content type and site (tone, required sections, do-not-touch fields) stored in the CMS and served as MCP resources and tool descriptions. |
| 11 | Prompts | Must | Reusable MCP prompts for common tasks (draft an article from notes, summarize for SEO, translate a draft) defined by plugins and editors. |
| 12 | Rate and size limits for agents | Must | Per-client call limits, payload caps and tool-result truncation with paging, to protect the host from runaway loops. |
| 13 | MCP client management in admin | Must | Register clients, assign service accounts and roles, see last activity, revoke; view agent writes in the activity log with one filter. |
| 14 | AI assist in the editor | Must | Opt-in actions on fields: rewrite, shorten, expand, fix grammar, generate title or summary, suggest SEO metadata, translate to another locale, alt text for images. Results are suggestions inserted into the draft, never auto-published. |
| 15 | Provider-agnostic AI layer | Must | Microsoft.Extensions.AI abstractions; providers for OpenAI, Azure OpenAI, Anthropic, Ollama and any OpenAI-compatible endpoint; keys and model per site; usage logged per user and action. |
| 16 | Semantic search | Should | Embeddings per entry (pgvector on PostgreSQL, in-process vector index on SQLite and SQL Server) exposed as a Delivery API parameter and an MCP `semantic_search` tool. |
| 17 | Agent-context resource | Should | One compact resource bundling schema, site guidelines, locale rules and example entries so an agent gets oriented in a single read. |
| 18 | Content provenance | Should | AI-assisted flag per version and per field (which action, which model) visible in history and filterable in lists. |
| 19 | Workflow stage for agent drafts | Should | Agent-created drafts land in a review stage when approval workflows are enabled; reviewers see agent origin and the prompt used. |
| 20 | AI-generated alt text and tags on upload | Should | Suggested, not applied, in the media library. |
| 21 | MCP tools from plugins | Should | Any plugin registers tools, resources and prompts through the SDK; same RBAC and tagging. |
| 22 | Change sets for agent work | Could | Group an agent's related edits into one reviewable set approved or rejected together. |
| 23 | Admin chat assistant | Could | In-admin assistant that uses the same MCP tools under the signed-in user, with per-tool approval. |
| 24 | Content evaluation checks | Could | Configurable pre-publish checks (readability, brand terms, broken links, policy) run by a model and shown as warnings. |
| 25 | Delivery-side agent outputs | Could | `llms.txt`, Markdown rendering of entries and a compact JSON view for agent consumers of the public site. |
| 26 | Remote MCP hosting guidance | Could | Reference configuration for exposing `/mcp` behind a gateway with rate limiting and WAF rules. |
| 27 | Autonomous agent runs | Won't | No scheduled or event-triggered agents that edit content without a human request. |
| 28 | Agents publishing by default | Won't | Publish is always a separate permission. |
| 29 | Bundled model or hosted AI credits | Won't | Bring your own provider; no metered AI service. |
| 30 | MCP as a Node sidecar | Won't | In-process only. |

## Decisions this category forces

- [ ] Tool granularity: per-type tools (Strapi; typed schemas, large tool lists) or meta-tools (`describe`, `query`, `mutate`; Storyblok; small list, weaker typing). Recommendation: per-type for the common types with a `query` meta-tool for the long tail; cap the generated list and let admins choose which types are exposed.
- [ ] Transport: Streamable HTTP only, or also stdio for local development. Recommendation: Streamable HTTP, plus a `fluentcms mcp` CLI verb that proxies stdio to HTTP for IDE clients.
- [ ] Vector storage for semantic search: pgvector on PostgreSQL with a fallback in-process index, or an external vector store slot only. Recommendation: pgvector plus fallback; external store as a provider slot.
- [ ] AI usage accounting: log per action with token counts for the host's cost tracking, or no accounting. Recommendation: log with token counts; no billing.
