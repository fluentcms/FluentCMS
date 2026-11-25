# Roadmap

What the project's own planning documents say comes next. It gives no dates. The [feature specification](docs/planning/specification/README.md) sorts every feature into four tiers: Must, in the first public release; Should, planned for the minor releases after it; Could, built when time or contributions allow; and Won't, out of scope ([the tiers](docs/planning/specification/00-scope-tiers-locked-decisions.md#tiers-moscow)). The [change log](CHANGELOG.md) lists what has shipped.

## Now

- **The second preview, `1.0.0-preview.2`, with the admin** in its three distributions: served at `/admin` by the reference image and by a host of `dotnet new fluentcms`, a prebuilt static build, and an app of your own from `dotnet new fluentcms-admin` ([#3465](https://github.com/fluentcms/FluentCMS/issues/3465), [the admin](docs/admin.md#distributions)).

## Before 1.0.0

- **Member sign-in with Apple**, verified against the real service ([#3165](https://github.com/fluentcms/FluentCMS/issues/3165)).
- **AI assist on Azure OpenAI**, verified against the real service (H3 of the [backend technical specification](docs/planning/backend-technical-spec.md)).
- **At 1.0.0, the contract freezes**: the public API files of the contract packages move to shipped, and package validation compares each later release with the release before it ([releasing](docs/releasing.md#cutting-a-release), steps 2 and 7).
- **The documentation site, with its `llms.txt`, and the frontend starters**, Must rows 11.7, 11.8 and 11.10 of the [specification](docs/planning/specification/11-developer-experience-tooling.md), are not built; no document gives them a release.

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
