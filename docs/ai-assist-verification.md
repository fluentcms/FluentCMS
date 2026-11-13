# Verifying AI assist against real endpoints

CI tests the [AI assist plugin](../src/plugins/FluentCMS.Plugins.AiAssist/README.md) against a scripted chat client and against local stand-ins that answer in the chat completions format of OpenAI, so no test calls a provider. This page records what has been run against what, and holds the walk-through for a run against a real endpoint. For M7 the plan verifies one OpenAI-compatible endpoint with a key and a local Ollama through its `/v1` endpoint ([spec](planning/backend-technical-spec.md) 10.4, H3).

| Endpoint | Status |
| --- | --- |
| Stand-ins in the tests | Every CI run; see [On the wire](#on-the-wire). |
| Reference host against a stand-in | Run on 12 Nov 2026; see [Dry run](#dry-run-of-the-reference-host-12-nov-2026). |
| Local Ollama through `/v1` | Not run yet. |
| A paid OpenAI-compatible endpoint with a key | Not run yet: it waits for a key from the maintainer. |
| Azure OpenAI | Not verified, and named as such in the notes of the preview. It is verified before 1.0.0. |

## On the wire

`AiWireTests`, `AiProviderTests` and `AiClientFactoryTests` run the plugin's own client factory against `StubProvider`, a local server on a loopback port:

- The usage of a stream in a chunk of its own after the end of the answer, as OpenAI sends it when it is asked to (the client asks with `stream_options.include_usage` on every stream); on the chunk that ends the answer; and late, after a pause, which `done` waits for.
- No usage at all, streamed or not: the suggestion is answered, with 0 prompt and 0 completion tokens in the answer and in a row that succeeded.
- `altText`: the reduced copy of the image goes as the one `image_url` part of the user message, a `data:` URI.
- A slow provider: a call past `requestTimeout` is `FCMS1688` (504, or the `error` event of a stream) with one failed row, and a caller that goes away ends the call as cancelled with one failed row. Neither is logged as a failure of the provider.
- With the host logging at `Trace`, no log line and no usage row holds the text or the suggestion of an answer, a stream, `seo`, or a provider that refuses with what it was sent.
- The answer is capped with `max_completion_tokens`, not the older `max_tokens`.

## Dry run of the reference host, 12 Nov 2026

The [walk-through](#walk-through) against a local stand-in in the chat completions format, which answers 21 prompt and 6 completion tokens, to check the walk-through and the reference host's own wiring before a real endpoint: SQLite, Development, the Blog sample, `Logging__LogLevel__Default=Debug`, `openAiCompatible` with no key.

| Step | Answer | Usage row |
| --- | --- | --- |
| `rewrite` | 200, `{ "suggestion", "usage" }` | succeeded, 21 and 6 tokens |
| `rewrite` streamed | 200, `delta`, `delta`, `done` with the body of the plain answer | succeeded, 21 and 6 |
| `translate` to `fr` | 200 | succeeded, 21 and 6 |
| `seo`, plain and streamed | 200, `metaTitle` and `metaDescription`; the stream sends `done` only | succeeded, 21 and 6 each |
| `altText` of a 1600 by 1200 PNG | 200; the stand-in got one `image_url` part, a PNG `data:` URI | succeeded, 21 and 6 |
| `expand` past `requestTimeout` 1 | 504, `FCMS1688` | failed, 0 and 0, about 1010 ms |
| `expand` streamed past `requestTimeout` 1 | 200, `delta`, then `error` with `FCMS1688` | failed, 0 and 0 |
| `expand`, the client gives up after 2 s | the connection is closed | failed, 0 and 0, about 2000 ms; event 9360 says `cancelled` |
| `expand` streamed, closed after the first event | the connection is closed | failed, 0 and 0; event 9360 says `cancelled` |

The log at `Debug`, over 5,000 debug lines of the framework, EF Core and the plugin, held neither the texts sent nor the suggestions. Event 9360 named the action, the field, the caller, the provider, the model, the outcome, the duration and the tokens of each call.

The first run found a bug. A call past `requestTimeout` was answered now as `FCMS1688` and now as `FCMS1689` (502), with a 9361 warning for a `TaskCanceledException`: the client of the package had a network timeout of the same length as `requestTimeout`, and whichever fired first decided the answer. The factory now gives the client no timeout of its own, so the plugin's timeout alone ends a call, and `AiClientFactoryTests.TheClientEndsASlowCallOnlyWhenItsCallerCancelsIt` holds it.

A stand-in cannot tell what a real endpoint does, so a real run checks in particular:

- whether the endpoint reads `max_completion_tokens`. One that reads only `max_tokens` does not cap the answer at `maxOutputTokens`;
- whether a stream carries the usage when it is asked for, and in which chunk;
- whether the model answers `seo` with the JSON object it is asked for;
- whether a vision model reads the image as a `data:` URI.

## Walk-through

### The host

Set the configuration in the environment of the host, and no key in a file of the repository:

| Variable | Value |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Development` |
| `ASPNETCORE_URLS` | `http://127.0.0.1:5199` |
| `FLUENTCMS_SAMPLE` | `blog`, whose articles have a text `title`, a rich text `body` and the second locale `fr` |
| `ConnectionStrings__FluentCms` | `Data Source=<run>/verify.db`, in a directory of the run |
| `FluentCms__Media__Local__Root` | `<run>/media` |
| `FluentCms__Ai__AllowedEndpoints__0` | the endpoint, such as `http://127.0.0.1:11434/v1`; not needed for `openAi` at its own endpoint |
| `FluentCms__Ai__OpenAi__ApiKey` or `FluentCms__Ai__OpenAiCompatible__ApiKey` | the key of a paid run, from a secret store; unset for Ollama, since a keyless `openAiCompatible` site gets this key too |
| `Logging__LogLevel__Default` | `Debug` |

Build the host, apply the migrations, create a management key and start the host from the directory of the run:

```bash
dotnet build src/FluentCMS.Host -c Release
cd <run>
dotnet <repo>/src/FluentCMS.Host/bin/Release/net10.0/FluentCMS.Host.dll fluentcms migrate
Logging__Console__LogToStandardErrorThreshold=Trace dotnet <repo>/src/FluentCMS.Host/bin/Release/net10.0/FluentCMS.Host.dll fluentcms apikey create --name ai-verify --class management --role "Super Admin" > key.txt
export FLUENTCMS_TOKEN="$(cat key.txt)"
dotnet <repo>/src/FluentCMS.Host/bin/Release/net10.0/FluentCMS.Host.dll > host.log 2>&1 &
```

### Ollama

The official image, with one small text model and one small vision model:

```bash
docker run -d --name fluentcms-ollama -p 127.0.0.1:11434:11434 -v fluentcms-ollama:/root/.ollama ollama/ollama
docker exec fluentcms-ollama ollama pull qwen2.5:0.5b
docker exec fluentcms-ollama ollama pull moondream
```

The settings of the site name `openAiCompatible`, the model, the endpoint and `"apiKey": null`. The text model is `qwen2.5:0.5b`, or the smallest that answers `seo` with the JSON object, and `altText` uses `moondream` or another vision model of at most about 3 GB.

### A paid endpoint

OpenAI itself is `openAi` with `FluentCms__Ai__OpenAi__ApiKey` and no endpoint. Another OpenAI-compatible endpoint is `openAiCompatible` with its endpoint in `FluentCms__Ai__AllowedEndpoints__0` and its key in `FluentCms__Ai__OpenAiCompatible__ApiKey`. Either way the settings of the site hold no key, so the key stays in the environment, and the model is a small one of that provider that reads images, so that one model serves every step.

### The steps

```bash
H=http://127.0.0.1:5199
A=(-H "X-Api-Key: $FLUENTCMS_TOKEN" -H "Content-Type: application/json")
TEXT='Ships is big boats what carry many things across the sea, and they goes slow but steady.'
```

1. The settings of the default site. Read the version, then write them with it:

   ```bash
   curl -s "${A[@]}" $H/api/management/v1/settings/ai
   curl -s "${A[@]}" -X PUT -H 'If-Match: "<version>"' $H/api/management/v1/settings/ai \
     -d '{"provider":"openAiCompatible","model":"qwen2.5:0.5b","endpoint":"http://127.0.0.1:11434/v1","apiKey":null,"requestTimeout":120,"fields":{"article":["title","body"]}}'
   ```

   Each later change of the model or of `requestTimeout` is the same `PUT` with the new version.
2. `rewrite`, plain and streamed. Both answer the suggestion and a usage with token counts:

   ```bash
   curl -s "${A[@]}" $H/api/management/v1/ai/actions/rewrite -d "{\"type\":\"article\",\"field\":\"body\",\"text\":\"$TEXT\"}"
   curl -sN "${A[@]}" -H "Accept: text/event-stream" $H/api/management/v1/ai/actions/rewrite -d "{\"type\":\"article\",\"field\":\"body\",\"text\":\"$TEXT\"}"
   ```

3. `translate`, with `{"type":"article","field":"title","text":"Ships at sea","options":{"targetLocale":"fr"}}`.
4. `seo`, plain and streamed, with the body of step 2. The suggestion is `{ "metaTitle", "metaDescription" }`, and the stream sends `done` only.
5. `altText`. Upload a photograph, set the vision model, and ask for the alternative text:

   ```bash
   curl -s -H "X-Api-Key: $FLUENTCMS_TOKEN" -F "files=@photo.jpg" $H/api/management/v1/media
   curl -s "${A[@]}" $H/api/management/v1/ai/actions/altText -d '{"type":"article","field":"title","options":{"mediaId":"<id of the upload>"}}'
   ```

6. The timeout. Set `requestTimeout` to 1 and ask for `expand` of the body, plain and streamed, with a text whose answer takes more than a second; on Ollama the first call after a change of model loads it, which takes longer. The plain call is 504 with `FCMS1688`, and the stream ends with an `error` event of `FCMS1688`.
7. A caller that goes away. Set `requestTimeout` back to 120 and give up on `expand` before it ends, plain and streamed:

   ```bash
   curl -s --max-time 2 "${A[@]}" $H/api/management/v1/ai/actions/expand -d "{\"type\":\"article\",\"field\":\"body\",\"text\":\"$TEXT\"}"
   curl -sN --max-time 2 "${A[@]}" -H "Accept: text/event-stream" $H/api/management/v1/ai/actions/expand -d "{\"type\":\"article\",\"field\":\"body\",\"text\":\"$TEXT\"}"
   ```

8. The usage rows, the newest first: `curl -s "${A[@]}" "$H/api/management/v1/ai/usage?pageSize=100"`. An answered call has prompt and completion tokens, and steps 6 and 7 write exactly one failed row per call, which event 9360 in `host.log` names as `timeout` or `cancelled`.
9. The log. `grep -c` in `host.log` for the text of step 2 and for words of each suggestion finds nothing.

Record each run as a section of this page, as the dry run above: the date, the endpoint and the models, and for each step what was sent, the status and the shape of the answer, and the usage row. Where the endpoint differs from `StubProvider`, correct the stand-in and the tests.

### Clean up

Stop the host, revoke the key or delete the directory of the run, and remove what the run pulled:

```bash
docker rm -f fluentcms-ollama
docker volume rm fluentcms-ollama
docker image rm ollama/ollama
```
