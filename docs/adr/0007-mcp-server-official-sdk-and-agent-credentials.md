# ADR-0007: MCP server on the official C# SDK with per-agent API keys

- Status: proposed
- Date: 2026-10-16
- Related: MCP-01 to MCP-08, SEC-01, SEC-02, SEC-04, SEC-06, UX-03, D05, D18, P1, P8

## Context

MCP is the agent-facing interface of the product. The server must expose every capability as tools (MCP-01), offer resources for schema, rules and items (MCP-03), run over stdio and HTTP from one binary (MCP-04), identify each agent by its own credential with scopes and caps (MCP-05, SEC-01, SEC-04), and log every call (MCP-07). Agents can never approve or publish (MCP-06, SEC-02). Bulk operations must return a change set id and progress instead of blocking (MCP-08).

Agent personas differ in how they authenticate. Desktop agents (Claude Code, Cursor and similar) and pipeline agents work with a bearer token. Hosted assistants (Claude.ai and ChatGPT connectors) require OAuth 2.1 against the server, with the server advertising its authorization server through protected-resource metadata.

## Decision

- Use the official C# MCP SDK: `ModelContextProtocol` for hosting, dependency injection and the stdio transport, and `ModelContextProtocol.AspNetCore` for the streamable HTTP transport. Tools are declared from Core application services; the SDK handles the protocol, transports and input schema generation.
- Transports: streamable HTTP mapped at `/mcp` inside the host in stateless session mode (the server never needs sampling or elicitation from the client), and stdio through `fluentcms mcp` against the same database (ADR-0004).
- Authentication in M1: per-agent API keys presented as bearer tokens, validated by a custom ASP.NET Core authentication handler in front of the MCP endpoint. Keys are random, shown once, stored hashed, and carry an expiry, scopes (sites, types, fields, operations) and caps (calls per period, LLM spend per period, items per change set, change sets per day). They are revocable and never appear in logs or URLs (SEC-06).
- Authentication in M2: an OAuth 2.1 authorization server inside the host (OpenIddict is the candidate) with dynamic client registration. The MCP endpoint validates its JWTs and publishes protected-resource metadata through the SDK's authentication extension, so hosted assistants can discover and connect. API keys remain for headless agents. Both credential kinds resolve to the same agent identity.
- Authorization lives in Core, not in the tool layer: scope and cap checks happen in the application services, so forms and tools cannot diverge. Exceeding a cap fails closed (SEC-04). The rule that agents never approve or publish is enforced in Core as well.
- Tool and resource descriptions are written for an LLM reader and include the active rule hints relevant to the call (MCP-02). Expected failures return structured results that say what to change (UX-03).
- Bulk operations return a change set id immediately and expose progress through a status tool and the job queue (ADR-0010). The SDK's Tasks extension (`ModelContextProtocol.Extensions.Tasks`), which standardises long-running tool calls with polling, is evaluated once clients commonly support it.
- Every call is logged with the agent identity and, where the client provides them, its declared model and task id (MCP-07).

## Consequences

- Protocol changes are absorbed by the SDK; the project tracks its releases.
- Hosted assistants are an M2 capability, and the roadmap says so.
- Two credential kinds in M2 map to one agent identity model, so scopes, caps and audit do not fork.
- stdio mode runs with the local user's database access. It is for development and single-user setups, and the documentation says so.
- Stateless HTTP sessions keep the server horizontally simple but rule out server-initiated requests to the client. Nothing in the requirements needs them.

## Alternatives considered

- OAuth 2.1 in M1: hosted assistants from day one, at the cost of an authorization server, client registration and consent screens inside M1's already large scope.
- Hand-rolled JSON-RPC server: full control, and full responsibility for a moving protocol.
- Semantic Kernel or another agent framework as the MCP layer: extra dependency and abstraction for a server that only needs to expose tools.
