# FluentCMS.Plugins.Mcp

The Model Context Protocol server of FluentCMS: an agent or an IDE connects to `/mcp` and works as the user or key it signed in with. It has tools for content and media, and serves the tools, resources and prompts that plugins add through the contracts of `FluentCMS.Agents`. Section 5.4 of the [backend technical specification](../../../docs/planning/backend-technical-spec.md) has the full design.

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

A client registered, approved, rejected and removed are the events `McpClientRegisteredEvent`, `McpClientApprovedEvent`, `McpClientRejectedEvent` and `McpClientRevokedEvent`, and the activity rows `mcp.client_registered`, `mcp.client_approved`, `mcp.client_rejected` and `mcp.client_revoked`. A client that is not approved within 7 days, a rejected one included, is removed by the daily job `mcp.client-cleanup`.

## Content and media tools

The plugin's own tools reach content and media through the services, as the caller. A tool list holds a tool only where a grant of the caller's role gives its permission, so the list never offers more than a call would allow. A call is checked by the service again, whatever the list held, so a tool called by name without its permission is refused with the code of the REST APIs (`FCMS3103`). The list needs no `schema.read`: it shows the types and fields the caller may read, which every entry it reads shows too.

Each exposed content type the caller may read has tools of its own:

| Tool | Permission | What it does |
| --- | --- | --- |
| `{type}_list` | `content.{type}.read` | Lists entries, with `filter`, `sort`, `page` and `pageSize` or `cursor`, `withTotal`, `fields`, `populate`, `depth`, `status` and `locale`. |
| `{type}_get` | `content.{type}.read` | Reads one entry by `id`. |
| `{type}_search` | `content.{type}.read` | Lists the entries that match the text `q`. It is listed only while the `ISearchIndex` slot is filled, since core refuses a search without an index (`FCMS2211`). |
| `{type}_create_draft` | `content.{type}.create` | Creates a draft from `fields`. |
| `{type}_update_draft` | `content.{type}.update` | Changes the `fields` it is sent on the draft of `id` and keeps the others, as a merge patch does; `null` clears a field. It needs `expectedVersion`, and a draft changed since is a conflict (`FCMS3105`). |
| `{type}_discard_draft` | `content.{type}.update` | Goes back to what is published. |
| `{type}_delete` | `content.{type}.delete` | Moves the entry to the trash, at once. |

A singleton has `{type}_get`, `{type}_update_draft` and `{type}_discard_draft` only, without an `id`. Its `{type}_update_draft` creates the entry when it has none and `expectedVersion` is 0, and `FCMS1613` says that it has none.

The shared tools take a `type`:

- `publish`, `unpublish` and `schedule`, listed for a caller that holds `content.{type}.publish` or `content.{type}.unpublish` for some exposed type. They are apart from the draft tools, so an agent role with `create` and `update` and without `publish` drafts an entry and cannot publish it. `schedule` takes `action`, `at` and `timeZone` as the schedule route of the Management API does.
- `query`, which lists the entries of any exposed type the caller may read, with the arguments of `{type}_list`.

`filter` is the filter of the REST APIs as JSON, and builds the same query: `{ "category": { "eq": "news" }, "or": [{ "title": { "startsWith": "A" } }, { "rating": { "in": [4, 5] } }] }` reads what `filter[category][eq]=news&filter[or][0][title][startsWith]=A&filter[or][1][rating][in]=4,5` reads. `sort`, `fields` and `populate` are arrays. A read answers the draft by default for a caller that may read drafts, as the Management API does, and what is published for any other, such as a Delivery key. A write runs in the locale of the connection (`X-Cms-Locale` or `?locale=`).

The input schema of a write tool is generated from the schema as the caller may read it. `fields` holds the fields the caller may write, each as its field type describes it for the OpenAPI document, so the lengths, ranges and patterns, the options of a select, the ids of a relation, and the components and blocks (under `$defs`) follow the schema. A field the caller may not read or write, a hidden one and a read-only one are left out. A required field is marked in its description and not in `required`, since a draft may leave it out. The server does not check the arguments against the schema: the service validates them.

The answers are the JSON of the Management API: an entry, `{ "items", "total", "page", "pageSize", "nextCursor" }` for a list, `{ "entry", "warnings" }` for a publish, the schedule for `schedule`, and `{ "type", "id", "deleted" }` for a delete. A validation failure is an error result with its code and each error by field path.

Which types the tools reach is set in `FluentCms:Mcp`:

| Key | Default | Meaning |
| --- | --- | --- |
| `ExposedTypes` | empty | The content types the tools reach, by name. Empty is every type the caller may read. A name the schema does not have stops the host. |
| `MaxExposedTypes` | `10` | How many of those types get tools of their own, in the order of the schema, which is by name. A type over it is reached through `query`, and a warning (event 9288) names it once. |
| `UploadTimeout` | `00:00:30` | The longest `media_upload` waits for a URL, connection and body together. |

A type that is not exposed, that does not exist or that the caller may not read is answered alike: its own tools are unknown (`FCMS1600`), and a shared tool answers `FCMS1612`.

The media tools work through `IMediaService`. They are listed only in a host that has the media plugin, for a site where it is enabled, to a caller that holds the permission, a grant limited to folders included:

| Tool | Permission | What it does |
| --- | --- | --- |
| `media_list` | `media.read` | Lists assets by `folder`, `subfolders`, `type` and `tag`, with `page` and `pageSize`. |
| `media_search` | `media.read` | Lists the assets whose title, file name or tags contain `q`, with the filters of `media_list`. |
| `media_get` | `media.read` | Reads an asset by `id`. |
| `media_upload` | `media.upload` | Stores a file from `url` or from `data` in base64, under `fileName`, in `folderId` or the root. |
| `media_update` | `media.update` | Changes `title`, `alt`, `caption`, `credit`, `tags`, `focalPoint` and `isPrivate`. |

An upload from a URL never reaches the host's own network. The URL must use `https`, carry no user name or password, and resolve to public addresses only: every address the name resolves to is checked on the connection itself, which is then made to one of them. A redirect is followed up to three times and held to the same rules. No proxy, cookie or credential is used. A URL that breaks a rule is refused with `FCMS1614` before anything is sent to it, and one that does not answer with a file fails with `FCMS1615`. The media service checks the caller's permission before it reads the file, and the URL is fetched only then. The service reads the type from the bytes and stops at the upload limits of the site's media settings, so a file over them is refused with the media plugin's code (`FCMS1582`). The log names the host of a refused URL and never the URL (event 9289).

Every tool carries its hints. The read tools are read-only. `{type}_create_draft` and `media_upload` add and are not destructive, and `media_upload` is open world. The others change or remove what is there and are destructive.

The Management plugin list (`GET /api/management/v1/plugins`) names, under `contributions.tools`, the tools each plugin offers its caller in the site of the request.

## Adding tools, resources and prompts

A plugin implements `IMcpToolProvider`, `IMcpResourceProvider` or `IMcpPromptProvider` and adds it in `ConfigureServices`:

```csharp
plugin.AddMcpTools<NoteTools>();
plugin.AddMcpResources<NoteResources>();
plugin.AddMcpPrompts<NotePrompts>();
```

A provider is a scoped service. Every call runs inside the operation of its request, with the `Mcp` origin, the client and the principal, which every version and activity row it writes records. The provider reaches content through the services, such as `IContentService`, which enforce what the caller may do. It lists only what the caller may use, asking `IPermissionChecker` where a tool needs a permission of the plugin's own. A tool of one content type sets `ContentType` on its descriptor.

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

The guidelines are also the `instructions` of the server's answer to `initialize`. The instructions of a type also follow the description of each tool of that type in the tool list, such as `article_list` and `article_create_draft`, and of a plugin's tool whose descriptor names the type in `ContentType`; a tool that is not listed for a caller, a tool of another type and a shared tool such as `publish` carry none. The text is data shown to an agent and is never run.

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
