# FluentCMS.Plugins.Mcp

The Model Context Protocol server of FluentCMS: an agent or an IDE connects to `/mcp` and works as the user or key it signed in with. The server has no tools of its own yet. It serves the tools, resources and prompts that plugins add through the contracts of `FluentCMS.Agents`. Section 5.4 of the [backend technical specification](../../../docs/planning/backend-technical-spec.md) has the full design.

## Connecting

The endpoint is `POST /mcp`, Streamable HTTP through [`ModelContextProtocol.AspNetCore`](https://www.nuget.org/packages/ModelContextProtocol.AspNetCore). It is stateless: every request is answered on its own, so any instance answers any request and nothing is kept between requests. There is no `GET` stream, no session and no legacy SSE endpoint. A client of a revision up to `2025-11-25` connects with `initialize`, and a newer one without it.

A caller is one of:

- a bearer token of the authorization server with the `mcp` scope. A client registered under `FluentCms:Clients:Applications` lists `mcp` in its `Scopes` to ask for it;
- an API key, sent as `Authorization: ApiKey <key>` or `X-Api-Key`. A Management key works as it does on the Management API, and a Delivery or Preview key reads what its class reads.

A request without credentials is answered 401 with `WWW-Authenticate: Bearer resource_metadata="..."`, and a token without the scope 403 with `error="insufficient_scope"`. The metadata (RFC 9728) is at `/.well-known/oauth-protected-resource/mcp` and `/.well-known/oauth-protected-resource`, open to anyone. It names the public URL of `/mcp`, the authorization server, the `mcp` scope and the `header` bearer method. The authorization server takes that URL as the `resource` parameter of an authorization or token request (RFC 8707) and refuses any other with `invalid_target`.

The URLs are built from `FluentCms:PublicOrigin` and the path base, and from the request when no public origin is set. Under a path base such as `/cms` the metadata is at `/cms/.well-known/oauth-protected-resource/mcp`, which the 401 names.

The site and the locale of a request are the ones the REST APIs resolve: `X-Cms-Site` or `?site=`, `X-Cms-Locale` or `?locale=`, the host, or the binding of a key. Where the plugin is disabled for a site, `/mcp` answers 404 (`FCMS1550`) to a signed-in caller.

## Settings

The options are in `FluentCms:Mcp`:

| Key | Default | Meaning |
| --- | --- | --- |
| `Name` | `fluentcms` | What the server calls itself to a client: 1 to 64 letters, digits, hyphens, underscores and dots. |
| `Title` | `FluentCMS` | Its name for people, which the resource metadata carries too. |

The permission `mcp.manage` is for managing the MCP clients, instructions and prompts of a site.

## Clients that register themselves

An MCP client such as an IDE registers itself with dynamic client registration (RFC 7591) and then sends the user through the authorization code flow. It finds the endpoint, `POST /connect/register`, as `registration_endpoint` in `/.well-known/openid-configuration`, which the resource metadata of `/mcp` leads it to. The authorization server of core serves the endpoint while this plugin is registered.

```http
POST /connect/register
Content-Type: application/json

{
  "client_name": "Desk agent",
  "redirect_uris": ["http://127.0.0.1:33418/callback"],
  "grant_types": ["authorization_code", "refresh_token"],
  "response_types": ["code"],
  "token_endpoint_auth_method": "none",
  "scope": "openid mcp offline_access"
}
```

The answer is a 201 with `client_id`, `client_id_issued_at` and the metadata as registered. A registered client is public and gets no secret. It uses the code flow with PKCE (`S256`) and refresh tokens, and the user is asked for consent. What it may send:

- `redirect_uris`, 1 to 10: `https`, or `http` on `localhost`, `127.0.0.1` or `[::1]`, with no wildcard, fragment or user information. The authorize endpoint compares a redirect URI exactly, the port included.
- `token_endpoint_auth_method`: `none`, which is also what a registration without it gets.
- `grant_types`: `authorization_code`, and `refresh_token` when the client refreshes.
- `response_types`: `code`.
- `scope`: any of `mcp`, `openid`, `profile`, `email` and `offline_access`, and all of them when it is left out. Never `management`.
- `client_name`: up to 128 characters, shown on the consent page and in the client list.

Anything else it asks for is refused with a 400 and the error body of RFC 7591, `invalid_redirect_uri` or `invalid_client_metadata`. Members the server has no use for, such as `client_uri`, are ignored.

### Policy

The policy is the settings area `mcp`, changed with `PUT /api/management/v1/settings/mcp/defaults` by a caller with `settings.mcp.manage` for the instance. The instance defaults are what counts, since the instance has one authorization server: what a site sets for itself in this area changes nothing of it.

| Key | Default | Meaning |
| --- | --- | --- |
| `clientRegistration` | `approval` | `approval`, `open` or `off`. |
| `defaultClientRole` | unset | The role a client gets under `open`: the id of a role, or the name of an instance-wide role. |
| `maxPendingClients` | `100` | How many clients may wait for approval at once. A registration beyond it is answered 503 with `temporarily_unavailable`. |
| `registrationsPerHour` | `10` | How many clients one client address registers in an hour, on each instance. One beyond it is answered 429 (`FCMS3110`). |

- **`approval`**: a registration creates a pending client. The authorize endpoint refuses it with a hosted error page that says it waits for approval, and no token is issued until an administrator approves it with a role.
- **`open`**: a registration is approved at once with `defaultClientRole`. While that is unset or names no role, the client waits for approval instead. The startup report lists `mcp-open-client-registration` in Production. Use it only where every client that reaches the instance is trusted.
- **`off`**: `/connect/register` answers 404 and the discovery document does not name it.

### The role of a client

A call through a client is allowed what both the user's roles and the client's role allow. A Super Admin who signs in through a client approved with a reading role reads and nothing more, and a client approved with Super Admin gives an author nothing an author does not have. Core's evaluator applies it, so it holds for every tool and for every other surface that takes the token. A client whose role was deleted reaches nothing.

### Managing clients

The endpoints need `mcp.manage` for the instance, from a grant with no site limit, since clients belong to no site.

| Endpoint | Does |
| --- | --- |
| `GET /api/management/v1/mcp/clients` | Lists the clients: `id`, `clientId`, `name`, `status` (`pending`, `approved` or `rejected`), `roleId`, `roleName`, `redirectUris`, `scopes`, `registeredAt`, `approvedBy`, `approvedAt` and `lastActivityAt`, the time of the newest token issued to the client. |
| `POST /api/management/v1/mcp/clients/{id}/approve` | Approves a pending client with `{ "role" }`: the id of a role, or the name of an instance-wide role. |
| `POST /api/management/v1/mcp/clients/{id}/reject` | Rejects a pending client, which is then refused for good. |
| `DELETE /api/management/v1/mcp/clients/{id}` | Removes a client with its sessions and tokens. Its access tokens are refused from the next request on. |

`{id}` is the `id` of the list, not the OAuth client id. The codes are `FCMS1603` for an unknown client, `FCMS1604` for a client that is not pending, `FCMS1605` for an approval without a role and `FCMS1606` for an unknown role.

A client registered, approved, rejected and removed are the events `McpClientRegisteredEvent`, `McpClientApprovedEvent`, `McpClientRejectedEvent` and `McpClientRevokedEvent`, and the activity rows `mcp_client.registered`, `mcp_client.approved`, `mcp_client.rejected` and `mcp_client.revoked`. A client that is not approved within 7 days, a rejected one included, is removed by the daily job `mcp.client-cleanup`.

## Adding tools, resources and prompts

A plugin implements `IMcpToolProvider`, `IMcpResourceProvider` or `IMcpPromptProvider` and adds it in `ConfigureServices`:

```csharp
plugin.AddMcpTools<NoteTools>();
plugin.AddMcpResources<NoteResources>();
plugin.AddMcpPrompts<NotePrompts>();
```

A provider is a scoped service. Every call runs inside the operation of its request, with the `Mcp` origin, the client and the principal, which every version and activity row it writes records. The provider reaches content through the services, such as `IContentService`, which enforce what the caller may do. It lists only what the caller may use, asking `IPermissionChecker` where a tool needs a permission of the plugin's own.

- A tool has a name, a description, the JSON Schema of its arguments and hints: read-only, destructive, idempotent, open world. A tool that says nothing is listed as destructive.
- A call answers `McpToolResult.FromText`, `McpToolResult.FromJson` or `McpToolResult.Error(code, message)`, or `null` when the name is not the provider's, so that the server asks the next one.
- A resource with an expression in its URI, such as `sample://notes/{id}`, is listed as a resource template. The provider matches the URI of a read itself.
- A prompt has arguments, which arrive as text.

A failure is an error result that names its code first, such as `FCMS3103: ...`, and carries it under `_meta` as `fluentcms/code`. An exception a provider lets through is answered with the code the REST APIs give it, with each validation error on a line of its own. For a resource or a prompt it is a protocol error with the code in its message. A name no provider answers for is a protocol error too: `FCMS1600` for a tool, `FCMS1601` for a resource and `FCMS1602` for a prompt.

The providers of a plugin that is disabled for the site of a request are not asked. When two providers list one name, the one of the earlier plugin is served.

## Telemetry

A tool call is a span of the `FluentCMS` activity source named `mcp {tool}`, with `fluentcms.operation`, `fluentcms.site` and, for a failure, `fluentcms.code`. Requests count in `fluentcms.requests` with the surface `mcp`. Each tool call is logged with event id 9280: the tool, the principal, the client, the site, the outcome and the duration, never an argument or a result.
