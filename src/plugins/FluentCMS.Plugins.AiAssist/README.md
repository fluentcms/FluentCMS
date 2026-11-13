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
| `apiKey` | unset | The key, a secret: it is written and never returned. Unset, the key is `FluentCms:Ai:{Provider}:ApiKey`. `openAiCompatible` also works with no key at all. |
| `maxOutputTokens` | `1024` | The most tokens an answer has, from 1 to 32000. |
| `requestTimeout` | `60` | How long a call may take, in seconds, from 1 to 600. |
| `callsPerMinute` | `20` | The calls a principal may make in a minute in the site, counted on each instance. |
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
  "fields": { "article": ["title", "summary"] }
}
```

When the instance defaults hold a key, a site that saves its settings without `apiKey` inherits that key (see below), and the call is refused with `FCMS1685` because the key belongs to the endpoint of the defaults. Send `"apiKey": null` to save the site with no key and inherit none.

A site with no key of its own gets `FluentCms:Ai:OpenAiCompatible:ApiKey` from the configuration, and so does every `openAiCompatible` site, a keyless Ollama site included: a site cannot decline it. An operator who serves Ollama sites therefore sets no configuration key for `OpenAiCompatible`, and gives each site that needs a key its own.

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
| `MaxInputLength` | `20000` | The most characters of text an action takes. |
| `UsageRetention` | `90.00:00:00` | How long the usage log keeps a row. |

The allow-list is a security control. A site administrator who could name any endpoint could make the server call an address inside the network, so an endpoint is used only when it is on the list: the same scheme, host, port and path, whatever the case of the host and with or without a slash at the end. The list is read when the host starts, so a change to it applies after a restart. An endpoint is checked against it at every call and not only when the settings are saved, so an endpoint that was saved while it was on the list is refused once the host runs with a list that no longer holds it. Redirects are not followed, and a refusal is `FCMS1684`.

The key of the configuration for `OpenAi` is the operator's account with OpenAI, so it is sent to OpenAI's own endpoint only. A site that names another endpoint for it holds its own key. The key of the configuration for `AzureOpenAi` or `OpenAiCompatible` goes to whichever allowed endpoint a site names for that provider, so give each site its own key when the list holds endpoints of more than one party.

A key in the instance defaults of the settings is the operator's as well. A site with no settings of its own follows the defaults whole, and a site that saves its settings without a key inherits the key of the defaults, as for every secret of every settings area: the site's settings do not hold it, and each call reads it from the defaults. The settings read shows this as `secretsInherited`. The plugin sends an inherited key only to the provider and the endpoint the defaults name: a site that names another provider or endpoint sets a key of its own, and is refused with `FCMS1685` until it does. A key replaced in the defaults reaches every site that inherits it at its next call, and a site that inherits it is held to the defaults just the same. A site that has set a key of its own, or `"apiKey": null`, keeps it when it saves without `apiKey`; it inherits again once its `ai` settings are reset (`ISettings<AiSettings>.ResetAsync` in the host's code).

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

### Limits

- The text has at most `MaxInputLength` characters (`FCMS1686`).
- The answer has at most `maxOutputTokens` tokens.
- A call ends at `requestTimeout` (`FCMS1688`) or when the caller goes away.
- A principal makes at most `callsPerMinute` calls in a site (`FCMS1687`). The count is kept in memory on each instance, and a refused call takes nothing from it. The Management rate limit holds beside it.
- A call is one request to the provider: the retries of the client package are off.

### The text is content

The instructions of an action are the system message, and the text is the user message. The model gets no tools, so a text that holds instructions can change the suggestion and nothing else. The plugin keeps neither the text nor the suggestion: they are in no row, log line or span. A provider's failure is logged by its type and status, without its message, which may repeat what the provider was sent.

## Usage

Every call that is sent to a provider writes one row to `cms_ai_usage`: the site, the principal by kind and id, the action, the type and field, the provider and model, the prompt and completion tokens, the duration and whether it succeeded. A call that failed, timed out or was cancelled writes its row too. A request that was refused before the provider was asked writes none.

`GET /api/management/v1/ai/usage` pages the rows of the site, the newest first, with the permission `ai.usage.read`:

| Parameter | Meaning |
| --- | --- |
| `principalId` | The rows of one principal. |
| `action` | The rows of one action. |
| `from`, `to` | The rows from a time and before a time, each with an offset, such as `2026-11-01T00:00:00Z`. |
| `page`, `pageSize` | The page, from 1, and its size, 50 and at most 100. |

It answers `{ "items", "total", "page", "pageSize", "totals" }`, where `totals` is `{ "calls", "promptTokens", "completionTokens" }` over every row the filter matches. There are no budgets and no billing.

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

The factory is a singleton and owns its clients: the plugin calls a client and does not dispose it. Returning `null` leaves AI assist off for that site (`FCMS1682`). The allow-list is the default factory's, so a factory of the host's own decides itself which endpoints it calls. The permission, the opt-in, the limits and the usage log stay the plugin's.

## Telemetry

A call is a span of the `FluentCMS` activity source named `ai {action}`, with `fluentcms.operation`, `fluentcms.site`, `fluentcms.type`, `fluentcms.provider` and `fluentcms.outcome` (`succeeded`, `failed`, `timeout` or `cancelled`), and for a failure the Error status and `fluentcms.code`. The counter `fluentcms.ai.calls` of the `FluentCMS` meter counts the calls by `fluentcms.operation`, `fluentcms.provider` and `fluentcms.outcome`. Log event ids 9360 to 9368.

## Codes

| Code | Status | Meaning |
| --- | --- | --- |
| `FCMS1680` | 422 | A request that cannot be read: a missing type, field, text, locale or option, or a parameter of the usage list. |
| `FCMS1681` | 404 | No such action. |
| `FCMS1682` | 409 | The site has no provider. |
| `FCMS1683` | 403 | The field is not opted in. |
| `FCMS1684` | 409 | The endpoint of the site is not on the allow-list. |
| `FCMS1685` | 409 | The provider of the site has no model, key or endpoint. |
| `FCMS1686` | 413 | The text is over the input limit. |
| `FCMS1687` | 429 | The caller is over its calls a minute. |
| `FCMS1688` | 504 | The provider did not answer within the timeout. |
| `FCMS1689` | 502 | The provider failed the call. |
| `FCMS1690` | 502 | The provider answered with no text, or for `seo` with no title and description. |
| `FCMS1691` | 422 | The media asset is not an image, or the site has no media library. |
| `FCMS1692` | 409 | The settings of the site name `anthropic` or `ollama`, which are no longer providers of the plugin. |

Where the plugin is disabled for a site, its endpoints answer 404 (`FCMS1550`).
