# Roadmap

What the project's own planning documents say comes next. It gives no dates. The [feature specification](docs/planning/specification/README.md) sorts every feature into four tiers: Must, in the first public release; Should, planned for the minor releases after it; Could, built when time or contributions allow; and Won't, out of scope ([the tiers](docs/planning/specification/00-scope-tiers-locked-decisions.md#tiers-moscow)). The [change log](CHANGELOG.md) lists what has shipped.

## Now

- **The way from the second preview to 1.0.0.** `1.0.0-preview.2` is out, the first preview with the admin ([notes](docs/releases/1.0.0-preview.2.md), [the admin](docs/admin.md#distributions)); the work now is what "Before 1.0.0" lists below.

## Before 1.0.0

- [ ] **Verify AI assist on a real Azure OpenAI deployment.** The maintainer with access to the deployment runs the [AI verification steps](docs/ai-assist-verification.md) and records the model, results for plain and streamed responses, usage, timeout, cancellation and log privacy. Existing runs against OpenRouter, Ollama or a stand-in do not verify Azure.
- [ ] **Finish member sign-in verification with Entra ID** ([#3160](https://github.com/fluentcms/FluentCMS/issues/3160)). A maintainer with a test tenant records a successful generic OIDC sign-in with a user who has a mail attribute, a denied Microsoft consent, and the result of an unregistered redirect URI in the [sign-in verification record](docs/member-sign-in-verification.md). Record outcomes, not tokens or credentials. Apple sign-in was removed and needs no run.
- [ ] **Assign a release to each outstanding developer-experience Must** ([specification, rows 11.7–11.10](docs/planning/specification/11-developer-experience-tooling.md)): the versioned documentation site; `llms.txt`, `llms-full.txt` and the MCP docs resource; agent instructions and skills for the repository and templates; and the Next.js, Astro and Blazor starters. Before freezing the contract, the maintainer records for each row either its 1.0 delivery issue or an explicit deferral with a reason and follow-up. None is marked delivered by this checklist.
- [ ] **Freeze the public contract at 1.0.0, not in a preview.** Move the five packages' public API entries to shipped as part of the stable release. After it is published, set the four package-validation baselines in a follow-up ([releasing](docs/releasing.md#cutting-a-release), steps 2 and 7).

## After 1.0.0

- **The rendering plugin**: Blazor components and Razor tag helpers for rich text, image URLs, the SEO head, draft preview, a sitemap and robots, in the first minor releases ([locked decisions](docs/planning/specification/00-scope-tiers-locked-decisions.md#locked-decisions), and the sequencing note of the [decisions log](docs/planning/specification/12-decisions-log.md)).
- **Approval workflows**: stages per content type, transitions per role and a required stage before publishing, in the first minor releases (row 6.12 of the [specification](docs/planning/specification/06-publishing-workflow-versioning.md), and the [decisions log](docs/planning/specification/12-decisions-log.md)).
- **Live updates over SignalR**, such as document locks and job progress, where the first release polls (decision 16 of the [decisions log](docs/planning/specification/12-decisions-log.md)).
- **Row rules written as JSON**, beside the C# predicates of the first release (decision 32 of the [decisions log](docs/planning/specification/12-decisions-log.md)).
- **Plugin data in transfer packages** for the first-party plugins other than media, in a minor release (P11 of the [backend technical specification](docs/planning/backend-technical-spec.md)).
- **The `fluentcms` tool as standalone binaries, with an interactive login** (P3 and P12 of the [backend technical specification](docs/planning/backend-technical-spec.md)).
- **The other Should rows of the specification**, such as autosave, saved views, passkeys, SAML 2.0 and semantic search ([admin UI](docs/planning/specification/03-admin-ui.md), [identity](docs/planning/specification/04-identity-permissions.md), [AI and agents](docs/planning/specification/09-ai-agents.md)). Their order is not decided.

## Not planned

Among the Won't rows of the specification, stated so that no one proposes them:

- A hosted SaaS or managed cloud ([infrastructure](docs/planning/specification/10-infrastructure-operations.md)).
- Usage telemetry sent to the project ([infrastructure](docs/planning/specification/10-infrastructure-operations.md)).
- A document or NoSQL store; storage stays relational through EF Core ([content modeling](docs/planning/specification/01-content-modeling.md)).
- Templates, themes, layouts or a page builder chosen in the admin ([content modeling](docs/planning/specification/01-content-modeling.md)).
- Plugins loaded at run time, sandboxed or sold in a marketplace ([plugins](docs/planning/specification/08-plugin-system-extensibility.md)).
- An admin on Blazor Server, or field editors in an iframe or JavaScript ([admin UI](docs/planning/specification/03-admin-ui.md)).
- Realtime co-editing ([admin UI](docs/planning/specification/03-admin-ui.md)).
- Agents that publish by default or run without a person's request, and a bundled model or AI credits ([AI and agents](docs/planning/specification/09-ai-agents.md)).
- Paid or gated SSO, audit or two-factor ([identity](docs/planning/specification/04-identity-permissions.md)).
