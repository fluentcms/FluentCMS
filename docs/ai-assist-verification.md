# Verifying AI assist against real endpoints

CI tests the [AI assist plugin](../src/plugins/FluentCMS.Plugins.AiAssist/README.md) against a scripted chat client and against local stand-ins that answer in the chat completions format of OpenAI, so no test calls a provider. This page records what has been run against what, and holds the walk-through for a run against a real endpoint. For M7 the plan verifies one OpenAI-compatible endpoint with a key and a local Ollama through its `/v1` endpoint ([spec](planning/backend-technical-spec.md) 10.4, H3).

| Endpoint | Status |
| --- | --- |
| Stand-ins in the tests | Every CI run; see [On the wire](#on-the-wire). |
| Reference host against a stand-in | Run on 12 Nov 2026; see [Dry run](#dry-run-of-the-reference-host-12-nov-2026). |
| Local Ollama through `/v1` | Run on 15 Nov 2026; see [Ollama](#ollama-through-v1-15-nov-2026). It found that Ollama does not read `max_completion_tokens`, for which a site turns `legacyMaxTokens` on. |
| A paid OpenAI-compatible endpoint with a key | Run on 15 Nov 2026 against OpenRouter; see [OpenRouter](#openrouter-15-nov-2026). |
| Azure OpenAI | Not verified, and named as such in the notes of the preview. It is verified before 1.0.0. |

## On the wire

`AiWireTests`, `AiProviderTests` and `AiClientFactoryTests` run the plugin's own client factory against `StubProvider`, a local server on a loopback port:

- The usage of a stream in a chunk of its own after the end of the answer, as OpenAI and Ollama send it when they are asked to (the client asks with `stream_options.include_usage` on every stream); on the chunk that ends the answer; and late, after a pause, which `done` waits for.
- As OpenRouter answers: blank lines before the body of a plain answer, a comment line in a stream, and after the text a stream that ends twice with no text, the second time with the usage, which it sends only there. The suggestion is the answer, and the usage that of the second end.
- No usage at all, streamed or not: the suggestion is answered, with 0 prompt and 0 completion tokens in the answer and in a row that succeeded.
- `altText`: the reduced copy of the image goes as the one `image_url` part of the user message, a `data:` URI.
- A slow provider: a call past `requestTimeout` is `FCMS1688` (504, or the `error` event of a stream) with one failed row, and a caller that goes away ends the call as cancelled with one failed row. Neither is logged as a failure of the provider.
- With the host logging at `Trace`, no log line and no usage row holds the text or the suggestion of an answer, any piece of a stream, `seo`, or a provider that refuses with what it was sent. The lines are those of ASP.NET Core, EF Core and the plugin: the OpenAI client has no logger factory, and its `HttpClient` is not one of `IHttpClientFactory`, so it writes none.
- The answer is capped with `max_completion_tokens` alone, for every provider. A site that turns `legacyMaxTokens` on sends the older `max_tokens` alone instead, plain and streamed, and never both.

## Dry run of the reference host, 12 Nov 2026

The [walk-through](#walk-through), step by step, against `eng/ai-stand-in.cs`, to check the walk-through and the reference host's own wiring before a real endpoint: the Release build of the host, SQLite, Development, the Blog sample, `Logging__LogLevel__Default=Debug`, and `openAiCompatible` with no key at `http://127.0.0.1:18434/v1`. The stand-in is a file-based app of the repository that answers in the chat completions format with 21 prompt and 6 completion tokens, and it waits 30 seconds for a model whose name holds `slow`, before a plain answer or after the first chunk of a stream. To repeat the run, start it with `dotnet run eng/ai-stand-in.cs -- 18434`, allow its endpoint, and name `stand-in-model` in the settings, `stand-in-vision` for `altText` and `stand-in-slow` for steps 6 and 7.

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
| `expand` streamed, the client gives up after 2 s | the first `delta`, then the connection is closed | failed, 0 and 0, about 2000 ms; event 9360 says `cancelled` |

The stand-in printed the shape of each request: no `Authorization` header, `max_completion_tokens` 1024 and no `max_tokens`, `stream_options` `{"include_usage":true}` on each stream, the system message and the text as the user message, and for `altText` a user message of one `image_url` part, a PNG `data:` URI of the reduced copy. The log at `Debug`, over 26,000 debug lines of ASP.NET Core, EF Core and the plugin, held neither the texts sent nor the suggestions; the OpenAI client writes no log line. Event 9360 named the action, the field, the caller, the provider, the model, the outcome, the duration and the tokens of each call, and there was no event 9361 or 9367.

An earlier run that day, against a stand-in of the lane's own, found a bug. A call past `requestTimeout` was answered now as `FCMS1688` and now as `FCMS1689` (502), with a 9361 warning for a `TaskCanceledException`: the client of the package had a network timeout of the same length as `requestTimeout`, and whichever fired first decided the answer. The factory now gives the client no timeout of its own, so the plugin's timeout alone ends a call, and `AiClientFactoryTests.TheClientEndsASlowCallOnlyWhenItsCallerCancelsIt` holds it.

A stand-in cannot tell what a real endpoint does, so a real run checks in particular:

- whether the endpoint reads `max_completion_tokens`. One that reads only `max_tokens` does not cap the answer at `maxOutputTokens`;
- whether a stream carries the usage when it is asked for, and in which chunk;
- whether the model answers `seo` with the JSON object it is asked for;
- whether a vision model reads the image as a `data:` URI.

The runs of 15 Nov 2026 below answer them for OpenRouter and Ollama.

## OpenRouter, 15 Nov 2026

The [walk-through](#walk-through) on the Release build of the host as in the dry run, with `openAiCompatible` at `https://openrouter.ai/api/v1` and the maintainer's key in `FluentCms__Ai__OpenAiCompatible__ApiKey`. One model served every step: `google/gemma-3-4b-it`, the cheapest small model on OpenRouter that reads images and is not a reasoning model, which OpenRouter routed to DeepInfra. The texts were those of the walk-through, a longer text of four sentences for `expand`, and for `altText` a JPEG of 1600 by 1200 pixels of a stop sign on a post against the sky. The whole run, with the requests sent to the endpoint directly and the check of the fix below, cost about 0.00016 USD.

| Step | Answer | Usage row |
| --- | --- | --- |
| `rewrite` | 200, `{ "suggestion", "usage" }`; the model put the text in a fenced block and added an image link of its own | succeeded, 153 and 89 tokens, 1680 ms |
| `rewrite` streamed | 200, `delta` events, then `done` with the body of the plain answer | succeeded, 153 and 93 |
| `translate` to `fr` | 200, `Navires en mer` | succeeded, 132 and 6 |
| `seo` | 200, `metaTitle` and `metaDescription` | succeeded, 147 and 57 |
| `seo` streamed | 200, `done` only, with both members | succeeded, 147 and 61 |
| `altText` of the photograph | 200, a red stop sign with the word "STOP" on a pole | succeeded, 434 and 17 |
| `expand` past `requestTimeout` 1 | 504, `FCMS1688`, after 1.06 s | failed, 0 and 0, 1010 ms; event 9360 says `timeout` |
| `expand` streamed past `requestTimeout` 1 | 200, 55 `delta` events, then `error` with `FCMS1688` | failed, 0 and 0, 1003 ms; `timeout` |
| `expand`, the client gives up after 1 s | the connection is closed | failed, 0 and 0, 1022 ms; `cancelled` |
| `expand` streamed, the client gives up after 1 s | 11 `delta` events, then the connection is closed | failed, 0 and 0, 997 ms; `cancelled` |

OpenRouter answers within a second or two, so the client of step 7 gave up after 1 s, not 2. The usage listed ten rows, the four of steps 6 and 7 failed, and the totals were 10 calls, 1166 prompt and 323 completion tokens. The log at `Debug`, 87,974 lines of which 29,533 debug lines, held none of the texts sent, no suggestion, no `data:` URI, no key and no `Bearer`, and there was no event 9361 to 9367. Each plain call past `requestTimeout` writes one error line of core's problem handler (event 9110, "An unhandled exception became FCMS1688"), as on the stand-in; it holds no text.

The four points, from requests sent to the endpoint as the client sends them:

- `max_completion_tokens` is read: a cap of 5 ended the answer with `finish_reason` `length` and 5 completion tokens.
- A stream carries the usage whether it is asked for or not, on a last chunk that ends the answer a second time: a chunk with `finish_reason` `stop` and an empty delta, then the same again with `usage`, then `[DONE]`.
- `seo` came back as the JSON object asked for, plain and streamed.
- The model read the image as a `data:` URI.

What is particular to the router: each chunk and answer has members of its own (`provider`, `native_finish_reason`, `service_tier`, `reasoning`, and `cost`, `is_byok` and `cost_details` in `usage`), which the client ignores; a plain answer opens with blank lines that OpenRouter writes while the model works; the model in the answer is the one asked for. OpenRouter documents comment lines (`: OPENROUTER PROCESSING`) in a stream while a model is slow to start; none came in this run, whose answers started at once. `StubProvider` and `eng/ai-stand-in.cs` now answer with the blank lines and the comment line, and the stub, with `Router` set, ends a stream as OpenRouter did: two chunks with an empty delta and `finish_reason` `stop`, the second with the usage, which it sent only there. `AiWireTests.ARoutersKeepAlivesAndTheUsageOnTheSecondEndOfAStreamAreRead` holds that both are read.

A row of a call that ended at `requestTimeout` or because its caller went away counts 0 tokens, although a provider may bill what the model generated before the call ended.

## Ollama through `/v1`, 15 Nov 2026

The walk-through as above with Ollama 0.40.16 in Docker Desktop (`ollama/ollama`, CPU only), `openAiCompatible` at `http://127.0.0.1:11434/v1`, no key in the host's environment and `"apiKey": null` in the settings. The text model was `qwen2.5:0.5b`, and `altText` used `moondream`.

| Step | Answer | Usage row |
| --- | --- | --- |
| `rewrite` | 200; the model made a Markdown list of the text | succeeded, 158 and 26 tokens, 3461 ms with the load of the model |
| `rewrite` streamed | 200, `delta` events, then `done`; the model gave the text back unchanged | succeeded, 158 and 21 |
| `translate` to `fr` | 200, `boats at sea`: the model did not translate | succeeded, 136 and 4 |
| `seo` | 200, `metaTitle` and `metaDescription` | succeeded, 151 and 31 |
| `seo` streamed | 200, `done` only, with both members | succeeded, 151 and 48 |
| `altText` of the photograph with `moondream` | 200, a paragraph on a stop sign in a green field under a blue sky | succeeded, 736 and 157, 64,677 ms with the load of the model |
| `expand` past `requestTimeout` 1 | 504, `FCMS1688` | failed, 0 and 0, 1015 ms; `timeout` |
| `expand` streamed past `requestTimeout` 1 | 200, `error` with `FCMS1688` and no `delta` before it | failed, 0 and 0, 1012 ms; `timeout` |
| `expand`, the client gives up after 2 s | the connection is closed | failed, 0 and 0, 2008 ms; `cancelled` |
| `expand` streamed, the client gives up after 1 s | 34 `delta` events, then the connection is closed | failed, 0 and 0, 1000 ms; `cancelled` |

The streamed `expand` with a client that gives up after 2 s was answered in 1.87 s, a row that succeeded with 186 and 50 tokens, so it was repeated with 1 s. The answers of the 0.5b model are poor, but each was a suggestion of the shape the action asks for. The log at `Debug`, 32,973 lines of which 11,305 debug lines, held none of the texts, no suggestion and no `data:` URI, and there was no event 9361 to 9367; the line of event 9110 is there as on OpenRouter.

The four points:

- Ollama does not read `max_completion_tokens`: a cap of 5 was answered with 141 tokens and `finish_reason` `stop`, so `maxOutputTokens` did not cap an answer. It reads `max_tokens`, which ended the same request at 5 tokens with `length`. A site of such an endpoint turns the setting `legacyMaxTokens` on, and the cap then goes as `max_tokens` alone (`AiClientFactory`); it is off by default, since OpenAI's reasoning models refuse `max_tokens` and a gateway may pass a request on to them. `AiClientFactoryTests.TheCapIsMaxCompletionTokensUnlessTheSiteAsksForTheOlderMaxTokens` and `AiWireTests.TheCapGoesUnderTheNameTheSiteChose` hold it. The check of the cap, run again on 15 Nov 2026 with that setting and `maxOutputTokens` 8 on `rewrite` plain and streamed: on Ollama with `legacyMaxTokens` off, 21 and 20 completion tokens, so the cap was ignored; on, 8 and 8; on OpenRouter with the default, 8 and 8.
- A stream carries the usage only when it is asked for, in a chunk of its own with no choices after the chunk that ends the answer, with a member `timings` of Ollama's own; the stub's `OwnChunk` is that form.
- `seo` came back as the JSON object asked for, plain and streamed, even from the 0.5b model.
- `moondream` read the image as a `data:` URI.

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

The settings of the site name `openAiCompatible`, the model, the endpoint, `"apiKey": null` and `"legacyMaxTokens": true`, since Ollama caps an answer only by `max_tokens`. The text model is `qwen2.5:0.5b`, or the smallest that answers `seo` with the JSON object, and `altText` uses `moondream` or another vision model of at most about 3 GB.

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
     -d '{"provider":"openAiCompatible","model":"qwen2.5:0.5b","endpoint":"http://127.0.0.1:11434/v1","apiKey":null,"legacyMaxTokens":true,"requestTimeout":120,"fields":{"article":["title","body"]}}'
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
7. A caller that goes away. Set `requestTimeout` back to 120 and give up on `expand` before it ends, plain and streamed, after 2 s, or after 1 s against an endpoint that answers sooner:

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
