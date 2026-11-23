# FluentCMS.Plugins.AiAssist

AI actions on the fields of an entry: rewrite, shorten, expand, fix the grammar, write a title, a summary, SEO texts, a translation or the alternative text of an image. The answer is a suggestion for the editor, and the plugin writes nothing to content. Each site chooses its provider, and the usage is logged per caller and action. Section 5.5 of the [backend technical specification](../../../docs/planning/backend-technical-spec.md) has the full design.

The plugin is named `ai`. It is off until a site names a provider and opts fields in.

## Settings of a site

The settings area `ai` (`GET` and `PUT /api/management/v1/settings/ai`, permission `settings.ai.manage`), per site, with instance defaults under `/settings/ai/defaults`:

| Key | Default | Meaning |
| --- | --- | --- |
| `provider` | `none` | `openAi`, `azureOpenAi`, `openAiCompatible`, or `none`, which leaves AI assist off. |
| `model` | unset | The model, or for Azure OpenAI the deployment. |
| `endpoint` | unset | The endpoint of the provider, one of `FluentCms:Ai:AllowedEndpoints`. OpenAI uses its own when it is unset; the other providers need one. |
| `apiKey` | unset | The key, a secret: it is written and never returned. Unset, the key is `FluentCms:Ai:{Provider}:ApiKey`, for the endpoint it is configured for. `openAiCompatible` also works with no key at all. |
| `maxOutputTokens` | `1024` | The most tokens an answer has, from 1 to 32000. |
| `legacyMaxTokens` | `false` | Sends `maxOutputTokens` as the older `max_tokens` instead of `max_completion_tokens`, for an endpoint that reads only that one, such as Ollama's. |
| `requestTimeout` | `60` | How long a call may take, in seconds, from 1 to 600. |
| `callsPerMinute` | `20` | The calls a principal may make in a minute in the site, counted on each instance. |
| `dailyTokenBudget` | `0` | The prompt and completion tokens the site may use in a day, from midnight UTC. 0 sets no budget. A call that does not go with a credential of the site's own is also held by `FluentCms:Ai:DailyTokenBudget`, which this may lower and not raise. |
| `assetAltText` | `false` | Lets `altText` run for an asset alone, which sends any image of the media library that the caller may update to the provider, with no field opted in. Off, such a call is refused with `FCMS1683`. |
| `fields` | empty | The opt-in: a map of content type to the fields that take actions, such as `{ "article": ["title", "summary", "body"] }`. |

A field that takes actions is a text, long text or rich text field of a content type. A rich text field is sent and answered as Markdown.

## Providers

The plugin speaks the chat completions API of OpenAI, through `Microsoft.Extensions.AI.OpenAI`, and nothing else:

- `openAi` is OpenAI, at `https://api.openai.com/v1` unless the site names another endpoint. It needs a key.
- `azureOpenAi` is Azure OpenAI through its OpenAI-compatible endpoint, `https://{resource}.openai.azure.com/openai/v1`, with the deployment as the model and an API key. Signing in to Azure with an identity instead of a key is a [factory of the host's own](#a-provider-of-the-hosts-own).
- `openAiCompatible` is any other endpoint that speaks that API. It works with a key or without one, and without one the request carries no `Authorization` header.

### Ollama

Ollama serves the OpenAI API under `/v1`, without a key. Allow the endpoint in the host:

```text
FluentCms__Ai__AllowedEndpoints__0=http://ollama:11434/v1
```

and name it in the settings of the site:

```json
{
  "provider": "openAiCompatible",
  "model": "llama3.2",
  "endpoint": "http://ollama:11434/v1",
  "legacyMaxTokens": true,
  "fields": { "article": ["title", "summary"] }
}
```

When the instance defaults hold a key, a site that saves its settings without `apiKey` inherits that key (see below), and the call is refused with `FCMS1685` because the key belongs to the endpoint of the defaults. Send `"apiKey": null` to save the site with no key and inherit none.

A site with no key of its own gets `FluentCms:Ai:OpenAiCompatible:ApiKey` from the configuration when it names `FluentCms:Ai:OpenAiCompatible:Endpoint`, the endpoint that key is for. An `openAiCompatible` site at another endpoint, such as a keyless Ollama, is called with no key.

### Other models

A model that has no OpenAI-compatible endpoint is reached in one of two ways:

- through a gateway that gives it one, such as a proxy in front of several providers, named as `openAiCompatible` with the gateway's endpoint and key. Many providers also serve an OpenAI-compatible endpoint of their own, which is used the same way;
- through a [factory of the host's own](#a-provider-of-the-hosts-own), which brings any `IChatClient`, such as the client of the provider's own package that the host references.

`anthropic` and `ollama` were providers of the plugin until M7. Settings that name one are refused when they are saved (`FCMS2805` on `provider`), and a site that still holds them is refused at every call (`FCMS1692`), each time with what to use instead. A site that moves off `anthropic` sends a new key with its new provider, or `"apiKey": null`, since a save without `apiKey` keeps its stored Anthropic key, which would then go to the new endpoint. A section `FluentCms:Ai:Anthropic` or `FluentCms:Ai:Ollama` in the configuration stops the host.

## Settings of the host

The options are in `FluentCms:Ai`:

| Key | Default | Meaning |
| --- | --- | --- |
| `AllowedEndpoints` | empty | The endpoints a site may name in its settings, each an absolute `http` or `https` URL with no user, query or fragment. Empty leaves a site OpenAI at its own endpoint. |
| `{Provider}:ApiKey` | unset | The key of a provider for the sites whose settings hold none, such as `FluentCms:Ai:OpenAi:ApiKey`. Keep it in a secret store, not in a file of the repository. |
| `{Provider}:Endpoint` | OpenAI's own for `OpenAi` | The endpoint the key of the provider is sent to, and to no other. `AzureOpenAi` and `OpenAiCompatible` need it beside a key, or the host does not start. |
| `SiteBilledEndpoints` | empty | The endpoints of `AllowedEndpoints` that bill the holder of the key, such as a public provider's. There a key a site sets is its own account, and its calls are held by its own `dailyTokenBudget` only. OpenAI's own endpoint is one without being named. Leave out every endpoint you run yourself. |
| `DailyTokenBudget` | `0` | The tokens a day of each site for its calls with the operator's credential: every call the factory does not say goes with the site's own. A site's `dailyTokenBudget` may lower it and cannot raise it or turn it off. 0 sets none. |
| `MaxInputLength` | `20000` | The most characters of text an action takes. |
| `UsageRetention` | `90.00:00:00` | How long the usage log keeps a row. |

The allow-list is a security control. A site administrator who could name any endpoint could make the server call an address inside the network, so an endpoint is used only when it is on the list: the same scheme, host, port and path, whatever the case of the host and with or without a slash at the end. The list is read when the host starts, so a change to it applies after a restart. An endpoint is checked against it at every call and not only when the settings are saved, so an endpoint that was saved while it was on the list is refused once the host runs with a list that no longer holds it. Redirects are not followed, and a refusal is `FCMS1684`.

The key of a provider in the configuration is the operator's account at one endpoint, so it is sent to `{Provider}:Endpoint` only: OpenAI's own endpoint for `OpenAi` unless the configuration names another. A site that names another endpoint, even an allowed one, holds its own key; `openAi` and `azureOpenAi` are refused without one (`FCMS1685`), and `openAiCompatible` is called with no key.

A key in the instance defaults of the settings is the operator's as well. A site with no settings of its own follows the defaults whole, and a site that saves its settings without a key inherits the key of the defaults, as for every secret of every settings area: the site's settings do not hold it, and each call reads it from the defaults. The settings read shows this as `secretsInherited`. The plugin sends an inherited key only to the provider and the endpoint the defaults name: a site that names another provider or endpoint sets a key of its own, and is refused with `FCMS1685` until it does. A key replaced in the defaults reaches every site that inherits it at its next call, and a site that inherits it is held to the defaults just the same. A site that has set a key of its own, or `"apiKey": null`, keeps it when it saves without `apiKey`; it inherits again once its `ai` settings are reset (`DELETE /api/management/v1/settings/ai` with the version read as `If-Match`, or `ISettings<AiSettings>.ResetAsync` in the host's code).

## Actions

`POST /api/management/v1/ai/actions/{action}`, where the action is `rewrite`, `shorten`, `expand`, `fixGrammar`, `title`, `summary`, `seo`, `translate` or `altText`:

```json
{
  "type": "article",
  "field": "summary",
  "locale": "en",
  "text": "Ships are big boats.",
  "options": { "targetLocale": "fr" }
}
```

- `type` and `field` name the field the suggestion is for.
- `text` is the text to work on. `documentId` in its place names an entry, and the plugin reads that field of its draft, as the caller.
- `locale` is the locale the suggestion is for, an enabled locale of the site: the draft is read in it and the right to update is asked in it. Left out, it is the locale of the request.
- `translate` takes `options.targetLocale`, an enabled locale of the site.
- `altText` takes `options.mediaId`, an image of the media library, and needs no text. The plugin sends a copy of the image that fits 1024 by 1024 pixels and carries no metadata, read through the media service as the caller.
- `altText` with neither `type` nor `field` is for the asset alone: its own alt text in `locale`, as the media field and the library edit it with `PATCH /media/{id}`. It takes no `documentId`, and runs only where the site's `assetAltText` is on. The plugin never writes the asset; the caller saves the suggestion if it keeps it.

The answer:

```json
{
  "suggestion": "Ships are large vessels.",
  "usage": { "provider": "openAi", "model": "gpt-5", "promptTokens": 212, "completionTokens": 9, "durationMs": 840 }
}
```

`suggestion` is text, and for `seo` an object with `metaTitle` and `metaDescription`. Every answer is `no-store`.

### Streaming

With `Accept: text/event-stream` the answer is server-sent events:

```text
event: delta
data: {"text":"Ships are "}

event: delta
data: {"text":"large vessels."}

event: done
data: {"suggestion":"Ships are large vessels.","usage":{"provider":"openAi","model":"gpt-5","promptTokens":212,"completionTokens":9,"durationMs":840}}
```

`delta` carries each piece of text as it arrives, and `done` the body of the plain answer. A call that fails once the provider was asked ends with `error`, whose data is the problem: `type`, `title`, `status`, `detail` and `code`. A request that is refused before the provider is asked is answered with the problem itself and its status, as without the header. `seo` sends no `delta`, since its suggestion is an object that exists only at the end.

### Who may call

The checks run in this order:

1. the permission `ai.use` in the site;
2. the type and field opted in under `fields` (`FCMS1683`);
3. the caller's right to update that field of the type in the locale of the suggestion, since a suggestion is for a field the caller edits (`FCMS3103`). A grant limited to locales gives suggestions for those locales only.

With `documentId` the content service decides whether the caller may read the draft, and with `mediaId` the media service whether it may read the image.

For `altText` on an asset alone, step 2 gives way to the site's `assetAltText` (`FCMS1683` when it is off, whatever the asset), and step 3 to the check of `PATCH /media/{id}`, made by the media service itself after the body is read: without `media.update` in the site the call is refused with `FCMS3103`, and an asset outside the folders of a limited grant is not found (`FCMS1585`), as an asset that does not exist is.

The first time the plugin starts on an instance, the built-in Editor and Author get `ai.use` with no constraints. The default is given once, so a grant you take away later is not given back at the next start, and a role that already holds a limited grant of an AI permission does not get it. `ai.usage.read` goes to no role by default.

### Which fields take actions

`GET /api/management/v1/ai/fields`, with the permission `ai.use`, tells an editor where to offer the actions, without the settings area and its `settings.ai.manage`:

```json
{
  "enabled": true,
  "locale": "en",
  "fields": { "article": ["title", "summary"] },
  "assets": true
}
```

- `enabled` is whether the site names a provider the plugin has. A site whose `provider` is `none`, or a removed one (`anthropic`, `ollama`), is off and lists no field, and its actions are refused (`FCMS1682`, `FCMS1692`).
- `locale` is the locale of the request, which `?locale=` or `X-Cms-Locale` names.
- `fields` maps each content type to the fields, in the order of the schema, that pass the checks above for the caller in that locale: opted in, a field that takes actions, and one the caller may update. A type the caller may not read is left out, and so is a field it may set on a new entry only, which an action refuses too.
- `assets` is whether the caller may ask for the alt text of an asset alone: the site is on and so is its `assetAltText`, the media plugin is enabled for it, and the caller holds `media.read` and `media.update` there, for every folder or some. Whether a given asset is in those folders is learnt at the call.

The answer holds nothing of the provider's configuration: not the provider, the model, the endpoint or the key, nor whether a key is set. A provider that is not configured well enough to answer is learnt at the call (`FCMS1684`, `FCMS1685`).

### Limits

- The text has at most `MaxInputLength` characters (`FCMS1686`).
- The answer has at most `maxOutputTokens` tokens, sent as `max_completion_tokens`, or with `legacyMaxTokens` as `max_tokens` alone. Ollama reads only `max_tokens`, so an Ollama site turns it on; it stays off elsewhere, since OpenAI's reasoning models refuse `max_tokens`, also through a gateway.
- A call ends at `requestTimeout` (`FCMS1688`) or when the caller goes away.
- A principal makes at most `callsPerMinute` calls in a site (`FCMS1687`). The count is kept in memory on each instance. A call is counted before its draft is read or its image decoded, so calls sent together pass only as often as the limit allows, and a call refused before it is sent is not counted.
- A site's daily token budget is the lower of its `dailyTokenBudget` and, for a call with the operator's credential, `FluentCms:Ai:DailyTokenBudget`. The default factory says a call goes with the site's own credential only for a key the site set itself at an endpoint that bills the holder of the key: OpenAI's own, or one of `FluentCms:Ai:SiteBilledEndpoints`. At an endpoint the operator runs, such as a local Ollama, any key the site types stays under the ceiling. A factory of the host's own counts as the operator's unless it sets `SiteCredential`. It is counted from the site's usage rows since midnight UTC and the calls in flight (`FCMS1693`, whose detail says when it resets, logged as event 9369 once a day for the site on each instance). The `dailyTokenBudget` of the instance defaults holds the sites that follow the defaults, not a site that saved settings of its own.
- A budgeted call reserves an estimate while it is in flight: a token for each UTF-8 byte of its instructions and text (until a draft named by `documentId` is read, `MaxInputLength` characters of three bytes; a call that reads none, such as `altText` with no text, reckons only the text it was given), 25,600 for an image (gpt-4o-mini's count of the 1024 by 1024 copy at high detail, the most of the models whose formula is published: an estimate, not a bound for any model) and `maxOutputTokens`. A call with the operator's credential that ends before the provider reports its usage, whose provider reports none, or that is reported at 0 for a prompt or for an answer with text, is written to the usage log at that estimate; the answer gives what the provider reported. So a site passes its budget by at most one call on each instance, of at most that estimate, as long as the provider counts no more than that. A row is written within five seconds; one that fails or takes longer is not written (event 9365) and still counts for its day on the instance. A call with the site's own credential is written as its provider reported it. The Management rate limit holds beside it.
- A call is one request to the provider: the retries of the client package are off.

### The text is content

The instructions of an action are the system message, and the text is the user message. The model gets no tools, so a text that holds instructions can change the suggestion and nothing else. The plugin keeps neither the text nor the suggestion: they are in no row, log line or span. A provider's failure is logged by its type and status, without its message, which may repeat what the provider was sent.

## Usage

Every call that is sent to a provider writes one row to `cms_ai_usage`: the site, the principal by kind and id with the name it had at the call (a later rename leaves the row as it was; the name is stored as a version stores its author's, with a value shaped like a secret masked and cut to 128 characters), the action, the type and field, the provider and model, the prompt and completion tokens, the duration and whether it succeeded. A call that failed, timed out or was cancelled writes its row too. A request that was refused before the provider was asked writes none. A provider that reports no usage leaves 0 prompt and 0 completion tokens in the row and in the answer, and the call still succeeds.

`GET /api/management/v1/ai/usage` pages the rows of the site, the newest first, with the permission `ai.usage.read`:

| Parameter | Meaning |
| --- | --- |
| `principalId` | The rows of one principal. |
| `action` | The rows of one action. |
| `from`, `to` | The rows from a time and before a time, each with an offset, such as `2026-11-01T00:00:00Z`. |
| `page`, `pageSize` | The page, from 1, and its size, 50 and at most 100. |

It answers `{ "items", "total", "page", "pageSize", "totals" }`, where `totals` is `{ "calls", "promptTokens", "completionTokens", "groups", "groupsTruncated" }` over every row the filter matches, and `groups` gives the same three for each principal and action, with `principalKind`, `principalId` and `action`, ordered by kind, id and action. `groups` holds at most 100 of them, those that used the most tokens, and `groupsTruncated` is true when the filter matched more; the three totals still count every row. To see every group, narrow the filter by `principalId`, `action`, `from` or `to` until `groupsTruncated` is false. Each row names its principal with `principalKind`, `principalId` and `principalName`, so a caller with `ai.usage.read` alone, who cannot read users or keys, sees who made each call. A row of `altText` for an asset alone has an empty `type` and `field`. The rows also count against the daily token budget (see "Limits"); there is no billing.

The recurring job `ai.usage-retention` (daily at 04:30 UTC) removes the rows older than `UsageRetention`.

## A provider of the host's own

The plugin declares the provider slot `IAiClientFactory`, which gives the `IChatClient` and the model of a site. A host fills it to reach a model that has no OpenAI-compatible endpoint, or to sign in another way than with a key. The host references the package of that client itself, and the plugin depends on none. Here every site that names a provider gets the one client the host registers, under the model its settings name:

```csharp
public sealed class HostClientFactory(IChatClient client) : IAiClientFactory
{
    public ValueTask<AiClient?> CreateAsync(SiteId site, AiSettings settings, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<AiClient?>(settings.Provider == AiProvider.None
            ? null
            : new AiClient(client, "host", settings.Model ?? "default"));
}
```

```csharp
// Any IChatClient, such as the one of a provider's own package.
builder.Services.AddSingleton<IChatClient>(services => CreateChatClient(builder.Configuration));

builder.Services.AddFluentCms(cms =>
{
    cms.AddPlugin<AiAssistPlugin>();
    cms.Providers.Use<IAiClientFactory, HostClientFactory>();
});
```

The factory is a singleton and owns its clients: the plugin calls a client and does not dispose it. Returning `null` leaves AI assist off for that site (`FCMS1682`). The plugin does not ask the factory for a site whose `provider` is `none`, or a removed one, and refuses the call itself, so such a site is off whatever the factory would give. The allow-list is the default factory's, so a factory of the host's own decides itself which endpoints it calls. The permission, the opt-in, the limits and the usage log stay the plugin's. A client the factory gives is counted as the operator's for the budget of `FluentCms:Ai:DailyTokenBudget` unless the factory sets `SiteCredential = true` on the `AiClient`, which it does only when the client calls with a credential the site holds and pays for.

The plugin ends a call at the site's `requestTimeout`, and when the caller goes away, through the cancellation token it passes to the client, so the host's client must honour that token: one that ignores it can run past `requestTimeout`. The client must also have no network timeout of its own that is shorter than `requestTimeout`, which goes up to 600 seconds. The clients built on `System.ClientModel`, such as OpenAI's, stop a request after 100 seconds unless told otherwise, and a call that such a timeout ends is answered as a failure of the provider (`FCMS1689`), not as a timeout (`FCMS1688`). The default factory gives its client no timeout of its own.

## Telemetry

A call is a span of the `FluentCMS` activity source named `ai {action}`, with `fluentcms.operation`, `fluentcms.site`, `fluentcms.type`, `fluentcms.provider` and `fluentcms.outcome` (`succeeded`, `failed`, `timeout` or `cancelled`), and for a failure the Error status and `fluentcms.code`. The counter `fluentcms.ai.calls` of the `FluentCMS` meter counts the calls by `fluentcms.operation`, `fluentcms.provider` and `fluentcms.outcome`. Log event ids 9360 to 9369; 9369, a site over its daily budget, is written once a day for the site on each instance.

## Verification

CI calls no provider. [Verifying AI assist against real endpoints](../../../docs/ai-assist-verification.md) records what the plugin has been run against and holds the walk-through for a run against Ollama or a paid endpoint. Both were run on 15 Nov 2026, against OpenRouter and a local Ollama, and Azure OpenAI is not verified.

## Codes

| Code | Status | Meaning |
| --- | --- | --- |
| `FCMS1680` | 422 | A request that cannot be read: a missing type, field, text, locale or option, or a parameter of the usage list. |
| `FCMS1681` | 404 | No such action. |
| `FCMS1682` | 409 | The site has no provider. |
| `FCMS1683` | 403 | The field is not opted in, or `assetAltText` is off for the alt text of an asset alone. |
| `FCMS1684` | 409 | The endpoint of the site is not on the allow-list. |
| `FCMS1685` | 409 | The provider of the site has no model, key or endpoint. |
| `FCMS1686` | 413 | The text is over the input limit. |
| `FCMS1687` | 429 | The caller is over its calls a minute. |
| `FCMS1688` | 504 | The provider did not answer within the timeout. |
| `FCMS1689` | 502 | The provider failed the call. |
| `FCMS1690` | 502 | The provider answered with no text, or for `seo` with no title and description. |
| `FCMS1691` | 422 | The media asset is not an image, or the site has no media library. |
| `FCMS1692` | 409 | The settings of the site name `anthropic` or `ollama`, which are no longer providers of the plugin. |
| `FCMS1693` | 429 | The site has used its daily token budget. |

Where the plugin is disabled for a site, its endpoints answer 404 (`FCMS1550`).
