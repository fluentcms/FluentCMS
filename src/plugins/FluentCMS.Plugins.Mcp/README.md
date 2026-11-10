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
