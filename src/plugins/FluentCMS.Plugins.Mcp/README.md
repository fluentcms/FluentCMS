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
| `{type}_delete` | `content.{type}.delete` | Moves the entry to the trash, after a [dry run](#dry-runs). |

A singleton has `{type}_get`, `{type}_update_draft` and `{type}_discard_draft` only, without an `id`. Its `{type}_update_draft` creates the entry when it has none and `expectedVersion` is 0, and `FCMS1613` says that it has none.

The shared tools take a `type`:

- `publish`, `unpublish` and `schedule`, listed for a caller that holds `content.{type}.publish` or `content.{type}.unpublish` for some exposed type. They are apart from the draft tools, so an agent role with `create` and `update` and without `publish` drafts an entry and cannot publish it. `schedule` takes `action`, `at` and `timeZone` as the schedule route of the Management API does.
- `query`, which lists the entries of any exposed type the caller may read, with the arguments of `{type}_list`.
- `bulk`, which publishes, unpublishes or deletes several entries at once, after a [dry run](#dry-runs). It is listed for a caller that holds `content.{type}.publish`, `content.{type}.unpublish` or `content.{type}.delete` for some exposed type, with the actions it holds ([Bulk](#bulk)).

`filter` is the filter of the REST APIs as JSON, and builds the same query: `{ "category": { "eq": "news" }, "or": [{ "title": { "startsWith": "A" } }, { "rating": { "in": [4, 5] } }] }` reads what `filter[category][eq]=news&filter[or][0][title][startsWith]=A&filter[or][1][rating][in]=4,5` reads. `sort`, `fields` and `populate` are arrays. A read answers the draft by default for a caller that may read drafts, as the Management API does, and what is published for any other, such as a Delivery key. A write runs in the locale of the connection (`X-Cms-Locale` or `?locale=`).

The input schema of a write tool is generated from the schema as the caller may read it. `fields` holds the fields the caller may write, each as its field type describes it for the OpenAPI document, so the lengths, ranges and patterns, the options of a select, the ids of a relation, and the components and blocks (under `$defs`) follow the schema. A field the caller may not read or write, a hidden one and a read-only one are left out. A required field is marked in its description and not in `required`, since a draft may leave it out. The server does not check the arguments against the schema: the service validates them.

The answers are the JSON of the Management API: an entry, `{ "items", "total", "page", "pageSize", "nextCursor" }` for a list, `{ "entry", "warnings" }` for a publish, the schedule for `schedule`, and `{ "type", "id", "deleted" }` for a delete that ran. A validation failure is an error result with its code and each error by field path.

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

Every tool carries its hints. The read tools are read-only. `{type}_create_draft` and `media_upload` add and are not destructive, and `media_upload` is open world. The others change or remove what is there and are destructive, the two with a dry run among them, whose descriptions say that a confirmation is needed.

The Management plugin list (`GET /api/management/v1/plugins`) names, under `contributions.tools`, the tools each plugin offers its caller in the site of the request.

## Dry runs, bulk and limits

An agent acts fast and can be wrong. The tools that remove an entry or change many run in two steps, a client may make only so many calls a minute, and a request and a list have a size they stay within. The settings are in `FluentCms:Mcp`:

| Key | Default | Meaning |
| --- | --- | --- |
| `MaxBulkItems` | `100` | The most entries one `bulk` call names or selects, from 1 to 1000. |
| `Limits:CallsPerMinute` | `120` | The tool calls a client may make in a minute. |
| `Limits:WritesPerMinute` | `30` | The calls of tools that change data a client may make in a minute, at most `CallsPerMinute`. |
| `MaxRequestBytes` | `1048576` | The largest body of a request to `/mcp`, 4096 or more. |
| `MaxResultBytes` | `262144` | The largest list a content tool answers, in bytes of its JSON, 4096 or more. |
| `MaxListTextLength` | `500` | The characters of a text a list shows before it shortens it, 16 or more. |

### Dry runs

`{type}_delete` and `bulk` take `dryRun`, which is true unless the call says false. A dry run changes nothing and answers the plan:

```json
{
  "dryRun": true,
  "type": "writer",
  "action": "delete",
  "count": 1,
  "entries": [{ "id": "0192f2a4-7b1c-7d2e-9f10-2a3b4c5d6e7f", "title": "Ada", "status": "published", "version": 3 }],
  "references": [{ "type": "post", "id": "0192f2a5-1c0d-7e3f-8a21-3b4c5d6e7f80", "title": "By Ada", "field": "author" }],
  "confirmationToken": "v1.1794312300.…",
  "expiresAt": "2026-11-10T12:05:00Z"
}
```

- `entries` are the entries the call would touch, as the caller reads them: the draft where it may read drafts, in the locale of the connection. The schema names no title, so `title` is the text field called `title` or `name`, or else the first text field.
- `references` are the entries whose relation would break: for a delete the entries that point to one of the plan, for an unpublish the published ones, and none for a publish. They are entries of the exposed types that the caller may read, found by the relation fields of a type itself, in the locale of the connection; a relation inside a component or a block is not looked for. A plan lists 50 at most and says `moreReferences` when there may be more. An entry of the plan is not a reference of it.
- The same call with `dryRun: false` and the `confirmationToken` runs it. A delete then answers `{ "type", "id", "deleted" }`.

The token is an HMAC-SHA256 over the plan: the tool, its arguments (all but `dryRun` and `confirmationToken`, in any order of their keys), the principal, the client, the site and the locale of the connection, the time the token ends, and the id, version and status of each entry. It lasts five minutes. Before a run the server reads the plan again, and refuses the call, with nothing run, when:

| Code | The token |
| --- | --- |
| `FCMS1630` | is missing. |
| `FCMS1631` | was not made for this call: it is of another tool, other arguments, another principal, client or site, or it is no token of this server. |
| `FCMS1632` | has expired. |
| `FCMS1633` | is of a plan that has changed: an entry has a new version or status, or the call reaches other entries. |

The key is 256 random bits of the instance. It travels in the token wrapped by Data Protection, so an instance that shares the key ring accepts the token of another, and nothing is stored. A run does not spend its token: the token stays good for its five minutes while the plan stands, which a run that changed the entries ends. The plan is checked just before the run and not in the same transaction, so a change in that instant is not seen; a delete moves the entry to the trash, where it can be restored, and a bulk publish names the draft version of the plan, so a draft changed since is a conflict. The service checks every run as the caller, whatever the plan showed: a dry run of a call the caller has no permission for answers a plan, and its run is refused (`FCMS3103`). Each refused confirmation is logged with event id 9296.

### Bulk

`bulk` runs one action on the entries of a type through `IContentService.BulkAsync`:

| Argument | Meaning |
| --- | --- |
| `type` | The content type. |
| `action` | `publish`, `unpublish` or `delete`. |
| `ids` or `filter` | The entries, by id or by a filter as `{type}_list` takes it, read as the plan reads its entries. One of the two, and a filter holds a condition at least. |
| `limit` | Required: the most entries the call may touch, at most `MaxBulkItems`. |
| `locale` | The locale to publish or unpublish; not for a delete. |
| `dryRun`, `confirmationToken` | As above. |

A `limit` over `MaxBulkItems`, more `ids` than the limit, and a filter that matches more entries than the limit are refused (`FCMS1635`), and nothing runs: a filter is never cut short. The filter is read for the plan and again for the run, and the run then names the entries of the plan by id, so it touches the entries the caller confirmed and no other. The answer is the one of the bulk route of the Management API, `{ "items": [{ "index", "id", "status", "version", "warnings", "problem" }], "cancelled" }`, where `status` is `succeeded`, `failed`, `duplicate` or `notRun` and `problem` holds the code the single call would have answered. An id that names no entry the caller reads is under `notFound`, in the plan and in the answer, and is not run.

### Call limits

The server counts the tool calls of each client: the client id of a token, or the key for an API key. A client may make `Limits:CallsPerMinute` calls a minute, and `Limits:WritesPerMinute` calls of tools that are not listed as read-only, which count as calls too; a dry run counts as a call of its tool, and a name that is no tool counts as one that changes data. A bucket holds the limit and fills again over a minute, as core's rate limits do. A call over a limit is not an HTTP 429: it is a tool error, `FCMS1634`, whose text says in how many seconds to retry, with the seconds under `_meta` as `fluentcms/retryAfter`. No tool runs for it.

The limits are counted per instance, as core's are, so a client spread over three instances may make three times the limit. They come on top of core's Management rate limit, which counts a principal across its clients, so a principal with several clients is still held to that. A client id that many users share, such as one registered for an IDE, is one client here. A client over a limit is logged once a minute (event id 9295).

### Sizes

- A request to `/mcp` whose body is over `MaxRequestBytes` is refused before it is read, with 413 and `FCMS3111`, the code of core's body cap. Core's cap (`FluentCms:Limits:MaxBodySize`) applies as well, so the lower of the two holds.
- `{type}_list`, `{type}_search` and `query` page by `page` and `pageSize` or by `cursor`. A page has the engine's default size and at most its largest (`FluentCms:Query:DefaultPageSize` and `MaxPageSize`, 20 and 100), which the input schema names.
- A list whose JSON is over `MaxResultBytes` ends at an entry, never inside one, with `"truncated": true`, the number of entries it kept as `pageSize` and the `nextCursor` of the rest; the next call sends that cursor and goes on from there. An entry that is over the limit alone is answered alone.
- In a list, a text longer than `MaxListTextLength` is shortened and ends with a marker that says how long it is, such as `… [shortened: 4210 characters in all; post_get reads it whole]`. `{type}_get` answers it whole, and so does `query` for a type over `MaxExposedTypes`, which has no `{type}_get`.
- The media lists are pages of at most 100 assets and are not cut.

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
