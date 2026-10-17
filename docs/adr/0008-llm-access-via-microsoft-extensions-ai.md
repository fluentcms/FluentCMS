# ADR-0008: Model access through Microsoft.Extensions.AI behind one internal gateway

- Status: accepted
- Date: 2026-10-16
- Related: MDL-01 to MDL-04, SEC-03, SEC-04, SEC-05, CHK-05, AUD-04, RUL-04, RTE-02, P6

## Context

Model calls occur in rule compilation (RUL-04), LLM-judged checks (CHK-01), risk scoring (RTE-02) and, in M2, the built-in agent. The owner brings a key or a gateway (P6). At least two provider families and one local option must work (MDL-02). The whole governance loop must function with no model configured (MDL-03). Every call must record model and version (MDL-04) and, for checks, the prompt and response verbatim (CHK-05, AUD-04). Content, rule text and sources are untrusted input to every call (SEC-03), spend is capped per agent (SEC-04), and outbound hosts are allow-listed (SEC-05).

## Decision

- All model calls go through one internal gateway service in Core. Nothing else in the codebase references a provider client.
- The gateway is built on Microsoft.Extensions.AI (`IChatClient` and its middleware pipeline). Providers the owner can configure: any OpenAI-compatible endpoint (OpenAI itself, gateways such as LiteLLM or OpenRouter, local servers such as Ollama, LM Studio or vLLM), and Anthropic through a provider package implementing `IChatClient`. That covers two provider families and a local option with two integrations.
- Models are selected by named profile (for example `checks`, `risk-scoring`, `compile`), each mapping to a provider, a model and limits. The owner can point profiles at different providers.
- The gateway adds, as pipeline middleware: structured prompts that separate instructions from data with explicit delimiting (SEC-03); per-call recording of provider, model, version, prompt, response, tokens and cost (MDL-04, CHK-05, AUD-04); spend accounting against the calling agent's cap (SEC-04); and an allow-list check on the configured endpoints (SEC-05).
- With no profile configured, the gateway reports "no model". LLM-judged checks resolve to "needs review", risk scoring routes to human review, and rule compilation produces advisory rules only (MDL-03).

## Consequences

- Swapping or adding a provider is configuration plus, at most, one small adapter.
- Audit and cost rules cannot be bypassed by calling a client directly, because no other code holds one.
- Microsoft.Extensions.AI is a Microsoft-maintained abstraction. The project tracks its releases and the provider packages' compatibility with it.
- Prompt and response storage grows the database. Retention settings (AUD-04) bound it.

## Alternatives considered

- Vendor SDKs called directly: fastest to start, and exactly the lock-in P6 forbids.
- Semantic Kernel: a full orchestration framework. v2 needs a client abstraction, not planners and plugins.
- A hand-written abstraction: reinvents what Microsoft.Extensions.AI already standardises, including the middleware pipeline the gateway relies on.
