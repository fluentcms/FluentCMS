# The admin

The admin of FluentCMS is a Blazor WebAssembly app that runs in the browser and is a pure client of the Management API: it reaches the instance only over HTTP, through the API, the authorization server and the hosted sign-in pages. Editors manage content and media in it, and administrators manage users, roles, keys, members, sites, settings, plugins and jobs. The admin halves of plugins add pages, menu items, field editors and more to it, each loaded the first time it is used.

This page says which distribution to take and how to sign in the first time. The other guides:

| Page | For |
| --- | --- |
| [Admin configuration](admin-configuration.md) | `config.json`, how a host builds it from `FluentCms:Admin`, an admin on another origin, the client `fluentcms-admin`, branding, cultures, preview and the rate limits |
| [Writing an admin half](admin-plugins.md) | the admin half of a plugin, from `dotnet new fluentcms-plugin` |
| [Translating the admin](admin-translating.md) | the resource files of a translation, the test of the keys and the pseudo-cultures |
| [Accessibility of the admin](admin-accessibility.md) | what CI checks against WCAG 2.2, and the script and result table of the screen reader pass |
| [Hosting the admin](admin-hosting.md) | the prebuilt static build on a static host or a CDN, with recipes |
| [Host configuration](host-configuration.md#admin) | every key of `FluentCms:Admin`, what the host serves at `/admin` and the headers of the page |
| [Run with Docker](docker.md#the-admin) | the admin in the reference image |

## Distributions

All three are the same app, built from the same packages. They differ in who builds it, which admin halves it holds and where it is served from.

| Distribution | For | Admin halves | Served from |
| --- | --- | --- | --- |
| The admin of the API host | most setups: the reference image, or a host of `dotnet new fluentcms` | the first-party ones | the API host at `/admin` |
| The prebuilt static build | the first-party admin on a static host or a CDN | the first-party ones | any static host, on an origin of its own |
| An admin app of your own | a team with admin halves of its own plugins | the first-party ones and the team's | the API host at `/admin`, or a static host |

**The admin of the API host.** `FluentCMS.Admin.Hosting` serves the admin from an ASP.NET Core host at `/admin`, with its `config.json` built from the host's configuration and its client registered by the host, so the admin is on the API's origin and needs no CORS setting. The reference image holds it ([Run with Docker](docker.md#the-admin)), and `dotnet new fluentcms` adds it unless `--admin false` is given, with the prebuilt build that the package carries ([the template's README](../templates/fluentcms/README.md#the-admin)).

**The prebuilt static build.** Each release attaches `fluentcms-admin-{version}.zip`: the published app with the first-party halves. Unzip it, edit `config.json` and the `<base href>` of `index.html`, and serve it with the headers and caching that [hosting the admin](admin-hosting.md) describes. The API host then needs the two settings of [an admin on another origin](admin-configuration.md#an-admin-on-another-origin).

**An admin app of your own.** `dotnet new fluentcms-admin` writes the app as a project of your own, with the administration pages, the rich text editor and the six first-party halves as package references. Add the halves of your own plugins to it ([writing an admin half](admin-plugins.md#use-the-half-in-an-admin-app)), then either reference its project from a host with `FluentCMS.Admin.Hosting`, which then serves it at `/admin` in place of the prebuilt build, or publish it and host it as static files ([the template's README](../templates/fluentcms-admin/README.md)).

A project made by a template references the FluentCMS packages, the admin's among them, at the version of the templates.

## The first sign-in

The admin has no users of its own: it signs in the users of the instance, through the hosted login page of the API host, with the authorization code flow and PKCE ([decision record 0007](decisions/0007-admin-sign-in-flow.md)). So the first step is the instance's first administrator, which [Run with Docker](docker.md#the-first-administrator) makes with `fluentcms user create`, and the template's README with `dotnet fluentcms user create`.

With the admin served by the host, as it is unless `FluentCms:Admin:Enabled` is false ([host configuration](host-configuration.md#admin)):

1. Open `{public origin}/admin/`, such as `https://cms.example.com/admin/`, under `FluentCms:ReverseProxy:PathBase` when it is set. A host of `dotnet new fluentcms` run in Development is at `https://localhost:5443/admin/`.
2. The admin sends the browser to the host's login page. Sign in as the first administrator, with the second factor there when the user has one.
3. The login page sends the browser back to `/admin/authentication/login-callback`, and the admin opens the address it was first opened at, the start page here.

The hosted pages answer https only, so the admin needs https too, with the development certificate on a local machine (`dotnet dev-certs https --trust`). Outside Development the host needs `FluentCms:PublicOrigin` for the admin, and stops without it: with `FCMS1105`, or with `FCMS1101` where `FluentCms:Security:AllowRequestOrigin` lifts that one.

An admin on another origin signs in the same way once the API host lists its origin and its addresses ([admin configuration](admin-configuration.md#an-admin-on-another-origin)). A `config.json` that is missing or wrong shows a page that names the member before the admin calls the API.

## Supported browsers

The admin is built for the last two versions of Chrome, Edge, Firefox and Safari, on desktop and tablet. A phone gets a readable layout that no test covers (decision AQ12 of the [admin technical specification](planning/admin-technical-spec.md#11-decisions-on-the-open-questions)).

The repository's end-to-end tests drive the admin in Chromium, against a publish of the reference host and against its image, and check every view with axe-core in the light and the dark theme, and on the publish right to left as well. Some cases also run at the width of a tablet.

Every night the same tests run in Firefox and in WebKit, Safari's engine, against the publish. Five cases are left out in WebKit, each with an issue: closing a tab with nothing unsaved ([#3648](https://github.com/fluentcms/FluentCMS/issues/3648)), the loading state on the reload of a lazily loaded page, in two cases ([#3649](https://github.com/fluentcms/FluentCMS/issues/3649)), the preview after its page navigates to one without the preview script, a fault of the admin in WebKit ([#3650](https://github.com/fluentcms/FluentCMS/issues/3650)), and a link set by keyboard in the rich text editor ([#3651](https://github.com/fluentcms/FluentCMS/issues/3651)). The list is in section 7 of the [admin technical specification](planning/admin-technical-spec.md#7-testing).

## Language and formats

English is the only interface language the admin ships at present; the resources are ready for translations ([translating the admin](admin-translating.md)). Which languages an instance offers is `cultures` of `config.json` ([admin configuration](admin-configuration.md#cultures)).

Each user sets the rest on the account page of the admin, which the user menu links to: `account` under the admin's base, such as `/admin/account`, and not the hosted pages at `/account` of the API host. Its preferences card holds the theme, the interface language when more than one is offered, the time zone, and the cultures whose conventions dates and times, and numbers, follow, each of the last three with an "As the browser" choice.

The theme is kept in the browser and applies as soon as it is chosen; it is not saved on the server. The other choices are saved with the card's button. A new interface language reloads the admin in it; the formats apply at once to the next text a page formats.

The saved preferences are stored on the server, so they follow the user to another browser. The browser keeps a copy of the language for the next start, so a new device starts once in the browser's language and reloads in the user's after the sign-in. An API key or a service account has no preferences, and keeps the browser's language and formats.
