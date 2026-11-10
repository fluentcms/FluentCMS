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

## Schema, guidelines and instructions

The server offers these resources itself:

| URI | Holds |
| --- | --- |
| `fluentcms://schema` | The content types, components and blocks, as the Management API serves them at `/schema`. |
| `fluentcms://schema/{type}` | One content type. |
| `fluentcms://sites/{site}/guidelines` | The guidelines of the site, named by its id: how the site wants its content written. |
| `fluentcms://types/{type}/instructions` | The instructions of the site for one content type. |

The schema is what the caller may read: a type it may not read is left out and answers as an unknown resource (`FCMS1601`), and a field it may not read is left out. A caller without `schema.read` is refused the schema (`FCMS3103`) and is not listed it. The guidelines are for every agent of the site, and a request reads those of its own site only. The instructions of a type are served to a caller that reads the type. The guidelines and the instructions that are saved are listed as resources, and the two forms with `{type}` as resource templates.

The guidelines are also the `instructions` of the server's answer to `initialize`. The text is data shown to an agent and is never run.

An administrator with `mcp.manage` in the site keeps them through the Management API:

| Request | Does |
| --- | --- |
| `GET /api/management/v1/mcp/instructions` | Lists the guidelines (`type` is null) and the instructions by type. `orphaned` marks a type that is no longer in the schema. |
| `PUT /api/management/v1/mcp/instructions` | Saves the guidelines, from `{ "text": "..." }`. |
| `PUT /api/management/v1/mcp/instructions/{type}` | Saves the instructions of a content type. |
| `DELETE` of either | Removes them. |

A text has at most 4000 characters, with no control characters other than line breaks and tabs (`FCMS1620`). A type that is not in the schema cannot be written to (`FCMS1621`), and a delete of what is not there is `FCMS1622`. Instructions of a type that has left the schema are served to no agent, stay in the list and can be deleted.

## Stored prompts

A site stores prompts for its agents, which `prompts/list` and `prompts/get` serve beside the prompts of the plugins' providers, to every agent of the site.

```json
{
  "name": "summarize",
  "title": "Summarize an entry",
  "description": "Sums an entry up for a reader.",
  "arguments": [{ "name": "entry", "description": "The id of the entry.", "required": true }, { "name": "tone" }],
  "template": "Summarize the entry {{entry}} in a {{tone}} tone."
}
```

`GET` and `POST /api/management/v1/mcp/prompts` list and create, and `GET`, `PUT` and `DELETE /api/management/v1/mcp/prompts/{id}` read, replace and remove one, with `mcp.manage` in the site.

- A name is 1 to 64 lower case letters, digits, underscores, hyphens and dots. It is unique in its site (`FCMS1625`) and is not the name of a prompt that a provider of an enabled plugin lists (`FCMS1626`).
- A template names an argument as `{{name}}`, and that is all it can say: there are no expressions. An argument it names must be declared (`FCMS1627`), and `{{` that opens no argument is refused (`FCMS1623`).
- `prompts/get` puts each value in as it is, once, as one user message. A required argument that is missing or empty is refused (`FCMS1628`), an optional one is left empty, and an argument the prompt does not declare is ignored.
- A site has at most 100 stored prompts, a prompt at most 20 arguments and a template at most 16000 characters (`FCMS1623`). A prompt that is not there is `FCMS1624`.

Each change of the instructions or of a stored prompt writes an activity row: `mcp.instruction_saved`, `mcp.instruction_deleted`, `mcp.prompt_created`, `mcp.prompt_updated` or `mcp.prompt_deleted`. No row holds a text. The change is logged too, with event id 9291 for instructions and 9292 for a prompt, and 9293 warns of a stored prompt that is left out because its arguments cannot be read.

The tables `cms_mcp_instruction` and `cms_mcp_prompt` are in the plugin's migration stream, `cms_mcp__migrations`, with a set for SQLite, PostgreSQL and SQL Server ([plugin migrations](../../../docs/plugin-migrations.md)).

## Telemetry

A tool call is a span of the `FluentCMS` activity source named `mcp {tool}`, with `fluentcms.operation`, `fluentcms.site` and, for a failure, `fluentcms.code`. Requests count in `fluentcms.requests` with the surface `mcp`. Each tool call is logged with event id 9280: the tool, the principal, the client, the site, the outcome and the duration, never an argument or a result.
