# FluentCMS admin: technical specification (draft for review)

Draft 1, 13 Nov 2026; approved 15 Nov 2026 with the decisions of section 11. Status: **approved**. The GitHub issues of section 10 are created milestone by milestone, as the backend's were; those of A0 and A1 were created on 15 Nov 2026.

This document takes the approved [feature specification](specification/README.md) and keeps what the [backend specification](backend-technical-spec.md) left to a document of its own (1.1 there): the Blazor WebAssembly admin, its generated views, the admin halves of the plugins, the admin distributions and the preview protocol. It is drafted beside M7, as decided when M7 was planned (backend 10.4, H10), and its input is the audit of the Management API against the admin's rows (B-105, [#3050]).

How to read it: sections 1 to 3 set scope, architecture, packages and layout; section 4 describes each area of the admin with the rows it implements and the endpoints it calls; section 5 is the contract of a plugin's admin half and the admin half of each first-party plugin; section 6 the three distributions; section 7 testing; section 8 maps every row to an area and a milestone; section 9 sequences the work in milestones A0 onward; section 10 is the issue list, numbered `A-nn`; section 11 holds the questions that were open in the draft, each with its decision. What the draft marked *proposed* was adopted as written unless section 11 says otherwise.

Endpoints are named as they are on `feature/m7` at `e31e894b`, whose routes are those the audit read at `8aea4aca`, relative to `/api/management/v1` unless they start with another prefix of the route map (backend Appendix B). Section numbers without a document name are this document's; "backend 4.10" is section 4.10 of the backend specification.

## 1. Scope

### 1.1 What counts as admin

| In scope | Out of scope |
| --- | --- |
| The Blazor WebAssembly app: shell, sign-in, runtime configuration, generated lists and forms, field editors, rich text, blocks and components, pickers, publishing controls, versions, preview, bulk actions, users, roles, keys, settings, plugins, jobs and diagnostics pages | The Management API and the authorization server, including the hosted login, consent, two-factor and reset pages (backend 4.7, 4.10) |
| The Razor Class Libraries the app is built from, and the wrapped component set | The documentation site, `llms.txt` and agent skills (rows 11.7 to 11.9) |
| The contract of a plugin's admin half and the admin halves of the first-party plugins | The frontend starter kits (row 11.10), including the preview page a front end serves |
| The admin side of the preview protocol: the iframe, the preview URL and the `postMessage` contract | The rendering plugin (decision 2, Should) |
| The three distributions of decision 5 and how the reference image builds admin and API together | Should and Could rows of category 3, beyond keeping them possible (8.1) |
| The admin halves of the `fluentcms` and `fluentcms-plugin` templates, and the `fluentcms-admin` template | Changes to the Management API: each one the admin needs is an issue of the backend (1.4) |

### 1.2 Decisions this document is bound by

From the [scope and locked decisions](specification/00-scope-tiers-locked-decisions.md), the [decisions log](specification/12-decisions-log.md) and the backend specification. Numbers are decision numbers in the log; B and H items are the backend's (its 9.8 and 10.4).

| # | Decision | Effect on the admin |
| --- | --- | --- |
| 4 | Blazor WebAssembly, pure Management API client, RCL components talking only through `HttpClient` | No server service is injected; no admin assembly that runs in the browser references a server package (3.3). |
| 5 | Reference image, `fluentcms-admin` template, prebuilt static build with runtime `config.json` | Section 6. |
| 6 | Admin, CLI and MCP use the Management API only | The admin calls no Delivery route; the preview page, which is the front end's, does. |
| 7, 30 | OpenIddict in the API host, Authorization Code with PKCE | The admin is a public client, `fluentcms-admin`, with refresh tokens (2.4). |
| 9 | MIT, no paywalled features, no phone-home telemetry | Only openly licensed libraries; the admin sends nothing anywhere but to the API it is configured with and the preview page. |
| 12 | Plugin admin contributions are Blazor components only, lazily loaded per plugin | Section 5; no iframe or JavaScript editor contract. |
| 13, 47 | First-party features are plugins; core holds the admin shell | Media, webhooks, MCP, AI assist and multi-site pages are admin halves of their plugins, on the same contract as third-party ones. |
| 14 | `FluentCMS.Sdk` with semver guarantees | The admin's own contract package is semver-guaranteed the same way (2.8). |
| 16 | Live updates are polled in the first release; SignalR is Should | Nothing a Must row needs is live (2.7). |
| 18 | ProseMirror/Tiptap JSON for rich text | The editor reads and writes the stored format as it is. |
| 26 | Fluent UI Blazor, wrapped behind FluentCMS components (recommended, adopted unless changed) | 2.3; confirmed in AQ1. |
| 27 | Tiptap via JS interop, an internal implementation and not an extension contract | 4.7. |
| 28 | Per-type preview URL template with a signed token; the `postMessage` contract defined now | 4.12. |
| 29 | Lazy loading per plugin assembly with `LazyAssemblyLoader` | 2.6. |
| 61 | `FluentCMS.*` package family, `FluentCMS.Admin` among them | Package names in 2.8. |
| B-108 | One version for every package and the image | The admin packages and the static build take it too. |
| H10 | Milestones A0 onward, issues numbered `A-nn` | Sections 9 and 10. |

Row 3.35 (Blazor Server) and row 3.34 (iframe or JavaScript field editors) are Won't, which rules out a server-rendered admin and a non-Blazor editor contract.

### 1.3 Platform targets (proposed)

| Target | Version | Note |
| --- | --- | --- |
| .NET | 10 LTS, the SDK of `global.json` | Blazor WebAssembly standalone app; no ahead-of-time compilation in the first release, since it grows the download. |
| Browsers | The last two versions of Chrome, Edge, Firefox and Safari | Desktop and tablet (row 3.1); a phone gets a readable layout, not a tested one (AQ12). |
| Accessibility | WCAG 2.2 AA | Row 3.19; 4.18. |
| Back end | The FluentCMS version of the same release | Admin and API are released together (B-108); the admin reads no version range of the API. |

### 1.4 Input: the audit of the Management API

The audit of B-105 ([#3050], comment of 13 Nov 2026) walked the nineteen Must rows of category 3 and the admin-only rows 7.7, 7.9, 8.7 to 8.9, 9.13 and 9.14 against the route map and the OpenAPI document of the reference host with every first-party plugin. The API serves rows 3.2, 3.3, 3.4, 3.6, 3.8, 3.9, 3.14, 3.16, 3.19, 8.8 and 8.9 as it is, and every other row partly. Each gap has an issue. The two that change a shape are in M7; the others only add and have no milestone.

| Issue | Gap | Milestone | Needed by |
| --- | --- | --- | --- |
| [#3094] | No response shows a pending schedule; system field names to reserve | M7 | A-48, A-27 |
| [#3095] | A preview token populates no relation | M7 | A-29 |
| [#3096] | No route gives the caller's access, the locales of its sites or its preferences | A1 | A-08, A-49, A-16, A-17, A-22, A-41 |
| [#3097] | A version names its author by id only | none | A-28 |
| [#3098] | No reset of a settings area, and no list of the areas | none | A-34 |
| [#3099] | No per-type preview URL template | none | A-29 |
| [#3100] | Management reads of an asset carry no URL | none | A-20, A-21, A-25 |
| [#3101] | An editor cannot learn which fields take AI actions | none | A-38 |
| [#3102] | No registration of an MCP client, and no change of its role | none | A-37 |
| [#3103] | The built-in Editor and Author roles hold no media or AI permission | none | A-20, A-21, A-38 |
| [#3104] | 45 plugin operations declare no body in the OpenAPI document | none | A-20, A-21, A-35 to A-38 |

An admin issue that needs a gap starts once the gap is merged. Where an area can work without it, it says how (section 4). Where the gap issues run is AQ13. By 15 Nov 2026 every gap was merged into `main`, #3096 last, so none holds an admin issue back; where section 4 says that an area waits for a gap, or how it works until the gap is merged, it describes the API as the audit found it.

The audit also named points that are awkward but need no new route. The admin answers them itself:

- **Labels.** The audit found no display label or title field on types and entries. `/schema` now gives a type an optional `displayName` and `titleField` ([#3150]). The admin reads them first, and derives a type's label from its name and an entry's from its fields when they are null (4.1; AQ10).
- **Operators.** The filter operators and sort keys of a field are in the OpenAPI document only, which is off outside Development. The admin derives them from the field type (4.4).
- **Bulk progress.** A bulk call reports nothing until it ends, so the admin sends the selection in chunks (4.13).
- **Labels of references.** Usage names an entry by type and id. The admin reads the labels with one list call per type (4.14).
- **Webhook events.** The event names are fixed in the plugin. Its admin half, released with it, carries the list (5.3).
- **Conditional reads.** A Management read answers `If-None-Match` with a 200 and the whole body, so the admin caches only what it knows to be stable (2.7).

## 2. Architecture

### 2.1 A pure client of the Management API

```text
Browser
|
+-- FluentCMS.Admin.App (Blazor WebAssembly)
|   config.json -> API base URL, OIDC client, branding, cultures
|   |
|   +-- Shell and core areas (FluentCMS.Admin)
|   +-- Rich text editor, lazy (FluentCMS.Admin.RichText; Tiptap through JS interop)
|   +-- Plugin admin halves, lazy per assembly (FluentCMS.Plugins.*.Admin)
|   |   all of them on FluentCMS.Admin.Abstractions and FluentCMS.Admin.Components
|   |
|   +-- HttpClient pipeline: bearer token, X-Cms-Site, X-Cms-Locale, problem mapping
|
+-- HTTPS
    |
    +-- /connect/*, /.well-known/*   sign-in, token refresh, revoke, logout
    +-- /account/*                   hosted pages, reached by redirect
    +-- /api/management/v1/*         every read and write
    +-- /media/{id}/...              signed asset URLs the Management API hands out (#3100)
    +-- the front end's preview page, in an iframe (4.12)
```

Rules that follow from decisions 4 and 6:

- The admin reaches the server only over HTTP. No admin assembly that runs in the browser references `FluentCMS.Core`, `FluentCMS.Sdk`, `FluentCMS.Plugins.Abstractions`, a data provider or the server half of a plugin, and an architecture test holds it (3.3). Records the admin reads are its own (AQ6).
- The server decides. The admin hides what the caller may not do (row 4.12) by reading the caller's access (#3096), and still treats every 403, 404 and 422 as the answer: hiding a control is a convenience, not a check.
- Every request from the admin's client runs with `Origin.Admin` on the server (backend 4.7), so versions and the activity log tell admin writes apart without anything the admin sends.
- The admin calls no Delivery route (decision 6). What looks like a Delivery read, a thumbnail or a preview, comes from a URL the Management API hands out or from the front end's own page.

### 2.2 The app and the Razor Class Libraries

The admin is a Blazor WebAssembly standalone app, `FluentCMS.Admin.App`, which holds only `Program.cs`, `wwwroot/index.html`, a sample `config.json` and the references to the plugin admin halves it is built with. Everything else is in Razor Class Libraries (decision 4), so that the template of decision 5 and a team's own app build the same admin:

```xml
<ItemGroup>
  <PackageReference Include="FluentCMS.Admin" />
  <PackageReference Include="FluentCMS.Plugins.Media.Admin" />
  <BlazorWebAssemblyLazyLoad Include="FluentCMS.Plugins.Media.Admin.wasm" />
</ItemGroup>
```

```csharp
var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<AdminApp>("#app");
await builder.AddFluentCmsAdmin();
await builder.Build().RunAsync();
```

`AddFluentCmsAdmin` reads `config.json` (2.5), registers sign-in (2.4), the API client (2.7), localization (4.17) and the core areas, and reads the manifest of the plugin admin halves the app references (2.6). An admin half is in the app because the app references it; there is no second list to keep. *Proposed*: the shape above; A-01 and A-18 settle the names.

An entry is edited as a JSON document (`JsonObject`), driven by the schema description: the admin is generic over content types and uses no generated type (decision 58 is about generated types for hosts and front ends, which the admin is not). The fixed groups of the API, such as users, roles and keys, are typed records (AQ5).

### 2.3 The wrapped component library

The component library is decision 26's: Fluent UI Blazor, wrapped behind FluentCMS components so that it can be swapped. Decision 26 is confirmed (AQ1): `Microsoft.FluentUI.AspNetCore.Components` 5.0.1, pinned in `Directory.Packages.props` by A-02, which opens with a check of the data grid, tree, drawer, dialog, menu and date picker against the rules of 4.18; MudBlazor is the fallback behind the same wrappers if the check fails. The library's own script is on the allow-list of 3.1 and is loaded by `FluentCMS.Admin.Components` only.

- `FluentCMS.Admin.Components` holds the wrapped set: buttons, inputs, selects, date and time pickers, checkboxes, dialogs, drawers, toasts, menus, tabs, a data grid, a tree, a file drop zone, layout primitives and icons. Each FluentCMS component exposes the parameters the admin needs, not those of the library, and names them for FluentCMS (`CmsButton`, `CmsDataGrid`).
- The core areas and every plugin admin half use only the wrapped set. An architecture test refuses a reference from an admin half to the library itself (3.3), so swapping the library touches `FluentCMS.Admin.Components` only.
- Theme tokens (colors, spacing, typography, radius, focus ring) are CSS custom properties of the wrapped set, with a light and a dark theme (row 3.1). Branding from `config.json` sets the accent color and the logo (2.5). Per-site theming is row 3.31, a Could.
- Layout uses logical CSS properties (`margin-inline-start`, not `margin-left`), so that a right-to-left culture mirrors the layout without a second stylesheet (row 3.18).
- The wrapped set carries the accessibility rules of 4.18: a label for every input, `aria-describedby` for help and errors, focus management in dialogs and drawers, a visible focus ring in both themes.

### 2.4 Sign-in: code flow with PKCE

The admin is the public OAuth client `fluentcms-admin`, the default of `cms.Clients.AdminClientId` (backend 4.7). Nothing registers it by default; each distribution does (section 6).

| Step | What the admin does | Endpoint |
| --- | --- | --- |
| Discovery | Reads the issuer's metadata at start. | `GET /.well-known/openid-configuration` |
| Sign-in | Redirects the whole page with `response_type=code`, `code_challenge_method=S256`, `state`, `nonce` and the scopes `openid profile email offline_access management`. The hosted login page shows the SSO buttons when providers are configured (row 3.3, backend 4.7). | `/connect/authorize`, then `/account/login` |
| Callback | Exchanges the code at `{admin base}/authentication/login-callback` and returns to the route the user was on. | `POST /connect/token` |
| Refresh | Before the access token expires (one hour by default), and once on a 401, exchanges the refresh token for a new pair. Refresh tokens rotate, and a used one is still accepted for 30 seconds, so two tabs that refresh at once both succeed (backend 4.7). | `POST /connect/token` |
| Sign-out | Revokes the refresh token, then ends the session on the server and returns to the admin. | `POST /connect/revoke`, `/connect/logout` |

- **No iframe.** The hosted pages send `frame-ancestors 'none'` by default (`FluentCms:Security:FrameAncestors`), so renewal through a hidden iframe with `prompt=none` cannot work, and the admin renews with refresh tokens only.
- **Library.** The flow is written in the admin, in C# (AQ2): the framework's `Microsoft.AspNetCore.Components.WebAssembly.Authentication` renews through a hidden iframe with `prompt=none`, which the rule above forbids, and brings a script of its own. A-07 records the choice as a decision record and tests the flow against the test host.
- **Token storage.** The access token in memory, the refresh token in `sessionStorage`, and a strict `Content-Security-Policy` on the admin page (AQ3). A new tab holds no token and goes through `/connect/authorize`, which the server's session answers without a prompt; two tabs therefore hold two refresh tokens, and a refresh in one does not touch the other.
- **Expiry with the draft kept (row 3.3).** When a refresh fails, the session is over. Before the admin redirects to sign in, it writes every unsaved form to `sessionStorage` with its user, site, type, entry, locale and the version the form was based on, and finds it after sign-in by all of these but the version, which the entry may have moved past meanwhile. After sign-in it returns to the same route and restores the form. The save that follows sends `If-Match` with the old version, so a change made meanwhile is a 412 with `current`, which the conflict dialog of 4.5 shows. A restored draft is removed once saved or discarded.
- **Password and two-factor.** A forced password change and an enrolment that a role requires happen on the hosted pages during sign-in (`/account/change-password`, `/account/enable-two-factor`). Enrolment by choice is in the user's account page (4.3).

*As built (A-07, [#3145]; decision record [0007](../decisions/0007-admin-sign-in-flow.md)).* `AdminSession` in `FluentCMS.Admin` runs the flow; the shell uses it through the internal `IAdminSession` (the signed-in user's subject, name and email from the id token, `SignInAsync`, `SignOutAsync`, and `Changed`). `SignInGate` in `AdminApp` renders the router only for a signed-in tab or a route under `authentication/`; otherwise it restores the session from the refresh token, or redirects to sign in.

- **Routes.** The callback is `{admin base}/authentication/login-callback`. Sign-out returns to the admin's base, the post-logout URI of 6.1, so it adds no route; the gate then starts a new sign-in, which the ended server session answers with the login page. Every URL is built from `NavigationManager.BaseUri`.
- **Issuer.** The discovery document's `issuer` and `authority` are compared as strings, ordinal, after one trailing slash is taken off each, since OpenIddict writes the issuer of a host without a path base with one; every endpoint must be on the issuer's origin, and `code_challenge_methods_supported`, when present, must hold `S256`.
- **Secrets.** `state`, `nonce` and the PKCE verifier are 32 random bytes each in base64url (`RandomNumberGenerator` and `SHA256.HashData`, which the runtime supports on WebAssembly), kept in `sessionStorage` until the callback, which removes them before anything else, so a second callback with the same state, or a replayed code, is refused. A sign-in started more than 30 minutes before its callback is refused. The return route is the route the tab was on, never one from the query, and only a relative route under the app's base that is not under `authentication/`, does not start with a slash, and has no colon in its first segment, so that the browser cannot read it as a URL of its own. A discovery document of another issuer, with an endpoint on another origin or without S256 shows a configuration failure of its own, not the one of a server that does not answer.
- **Id token.** Its signature is not checked: it comes from the token endpoint over TLS in answer to this tab's code and verifier (OpenID Connect Core 3.1.3.7), and the .NET runtime on WebAssembly has no RSA (it has the SHA family, HMAC, AES-CBC, PBKDF2, HKDF and the random generator); the browser's SubtleCrypto has it, at the cost of more interop. `iss`, `nonce`, `exp` and `sub` are checked, and the audience is the client alone, or one of several only when `azp` is the client; a claim of the wrong type refuses the token; it gives the user's subject, name and email, and is not kept.
- **Refresh.** On demand: an access token is refreshed when it is asked for within a minute of its expiry (half its lifetime when that is shorter), and once on a 401 by `TokenRefreshHandler`, between `RateLimitHandler` and `SessionHeadersHandler`, which resends the request with the new token. The flight is shared within the tab. A refusal by the token endpoint (400 or 401 with an OAuth `error`) ends the session; a network failure, a 5xx, or a 400 or 401 without an `error`, such as a proxy's, keeps it, and the call gets its 401. Without a refresh token, which a `config.json` without `offline_access` gives, a 401 is the answer and nothing redirects. A session that the server ends within a minute of its sign-in is not signed in again on its own (one the admin ends at the expiry of an access token it cannot refresh is): the gate says so and offers "Try again", so that a server that keeps refusing cannot make a loop of silent sign-ins. Each request to the authorization server is held to 20 seconds, and a timeout shows the gate's or the callback's failure with a way to try again.
- **Sign-out.** Revokes the refresh token at `revocation_endpoint`, then clears the session, the pending sign-in and the drafts from the tab whatever the revocation answered, then goes to `end_session_endpoint` with `client_id` and `post_logout_redirect_uri` and no `id_token_hint`, which would put the id token in the URL; the hosted logout page asks the user to confirm.
- **Drafts.** `IFormDraftStore` in `FluentCMS.Admin.Abstractions`: a form tracks itself while it has changes, finds its draft by site, type, entry and locale, whatever its base version, and removes it once saved or discarded. The base version is the read's entity tag, `"0"` for a locale the entry did not have, and null for an entry not created yet. The drafts are one `sessionStorage` item, each with its user, site, type, entry, locale, base version and fields; a sign-in by another user removes the drafts of the one before.

### 2.5 Runtime configuration (`config.json`)

At start the app reads `config.json` from its own base URL with `cache: no-store` (row 3.2), so one prebuilt build works against any API host. Members, as `FluentCMS.Admin.Abstractions` exposes them in the record `AdminConfiguration`, which the app registers as a singleton only when the file is valid:

| Member | Meaning | Default |
| --- | --- | --- |
| `apiBaseUrl` | Origin and path base of the API host, such as `https://cms.example.com` or `https://example.com/cms`: an absolute `http` or `https` URL without user info, query or fragment. A trailing slash is optional; the admin always adds one, so that a relative address resolves under the path base | required |
| `authority` | Issuer of the authorization server, a URL as `apiBaseUrl`, kept as written without a slash added, since the issuer of the discovery document is compared as an exact string | `apiBaseUrl` as written |
| `clientId` | OAuth client id, visible ASCII without spaces | `fluentcms-admin` |
| `scopes` | Scopes asked at sign-in, an array of distinct scope tokens | `["openid", "profile", "email", "offline_access", "management"]` |
| `branding` | An object of `name` (text), `logoUrl` and `faviconUrl` (an absolute `http` or `https` URL, or a path relative to the admin's base) and `accentColor` (a hex color: `#rgb`, `#rgba`, `#rrggbb` or `#rrggbbaa`) | `FluentCMS`, the built-in logo and favicon, the theme's accent |
| `cultures` | An object of `offered`, an array of distinct culture names, and `default`, which must be one of them | every shipped culture, `en` |
| `previewOrigins` | Origins the preview frame may load, each an `http` or `https` URL without a path; the admin refuses a preview URL of another origin, and the reference host puts them in the page's `frame-src` (4.12, 6.1) | none, which turns preview off |

Every URL is ASCII only; an internationalized host name is written in its `xn--` form. A plain `http` URL of `apiBaseUrl` or `authority` on a host that is not loopback is accepted with a console warning.

A missing or invalid member shows a static error page that names it, before anything else runs; the app makes no request to the API until its configuration is valid. A member set to `null` counts as not set, and a member that appears twice in one object is refused. A file that cannot be read within 15 seconds is a problem of the file. A member the app does not know, also inside `branding` and `cultures`, is ignored with a console warning, so a newer `config.json` works with an older build. In the reference host, `config.json` is an endpoint built from the host's configuration, so the image needs no file (6.1). `config.json` is as trusted as `index.html`: whoever can write it can already point `authority` anywhere, so the validation guards against mistakes, not against an attacker.

The branding is applied without a `<style>` element, which a strict `Content-Security-Policy` blocks: `name` is the page title and the name of the shell's logo, `faviconUrl` a `<link rel="icon">`, `logoUrl` the image of the shell's logo (`CmsLogo`), and `accentColor` the custom property `--fluentcms-brand-accent` on the document element, which the wrapped set reads (2.3).

### 2.6 Lazy loading by plugin assembly

Decision 29: each plugin admin half is one assembly, loaded with `LazyAssemblyLoader` the first time one of its contributions is used, and listed as `BlazorWebAssemblyLazyLoad` in the app. The first download holds the shell and the core areas only. The rich text editor is loaded the same way, the first time a form has a rich text field (4.7).

The shell needs a plugin's contributions, its menu items, pages and the field types it edits, before the assembly is loaded. Decided (AQ4): each admin half ships a manifest, `fluentcms-admin.json`, written by hand as a static web asset of the package (`_content/{PackageId}/fluentcms-admin.json`), which lists the server plugin's name, the admin range and the contributions of the table below. A test helper of `FluentCMS.Admin.Abstractions` fails the half's tests when the file and the contribution attributes of its assembly disagree, and the plugin template carries that test (A-23). The app's build gathers the manifests of the halves it references into one file, which the shell reads at start with one request. A build step that writes the manifest from the attributes can replace the hand-written file later without a change to the shell.

| Contribution | Declared by | Loads the assembly when |
| --- | --- | --- |
| Page and menu item | `[AdminPage(route)]`, `[AdminMenuItem]` | the route is navigated to, through the router's `OnNavigateAsync` |
| Field editor and list cell | `[FieldEditor(fieldType)]`, `[ListCell(fieldType)]` | a form or list of a type with that field opens |
| Settings page | `[SettingsPage(area)]` | that area's page opens |
| Entry, bulk and field actions | `[EntryAction]`, `[BulkAction]`, `[FieldAction]` | a list or form that the action targets opens |
| Dashboard widget | `[DashboardWidget]` | the home page opens and the widget is visible to the user |
| Services for other halves, such as the media picker | `[AdminService(typeof(IMediaPicker))]` | the service is first asked for |

A contribution is shown only while its server plugin is enabled in the site (`enabled` of `GET /plugins`, row 8.15) and the caller holds the permission it declares (#3096). The manifest names the server plugin, so a disabled plugin's admin half is never loaded in that site.

**As built (A-19, [#3288]).** An app references a half and lists it as `<BlazorWebAssemblyLazyLoad Include="{Assembly}.wasm" />`; the build target `build/FluentCMS.Admin.targets` of `FluentCMS.Admin`, which a package reference imports and `FluentCMS.Admin.App` imports by path, writes the `fluentcms-admin.json` of every referenced half into `fluentcms-admin.plugins.json`, a list that is a static web asset of the app, not fingerprinted, and stops the build when a half is not lazy. `AddFluentCmsAdmin` reads it beside `config.json`, without the cache; each manifest is read on its own, and one that is not `IsValid`, that cannot be read, whose `AdminRange` is missing or does not hold the running version of `FluentCMS.Admin.Abstractions`, that is named after a reserved segment (3.2) or that repeats a name is left out, logged and listed on `/system/plugins`, which the start page links to while there is one; an unreadable file gives no half and one problem, and the admin works either way. The sidebar shows a half's menu items from the manifest, in their section by `order` and then label, while the plugin is enabled and the caller holds the page's permission; the label is a text of the manifest, since the half's resources are not loaded before the half: `labels` holds the texts by culture name, the sidebar shows the interface culture's, else its parents', else `en`'s, which `AdminPluginManifestCheck` requires of every item, with each culture a culture and each text not empty. Icons stay in the manifest; the sidebar draws none, as for core's links. The router's `OnNavigateAsync` loads a half when the first segment of the route is its plugin's name, the plugin is enabled in the site and the caller holds the permission of the page whose route template matches, once the shell has read the site; otherwise the half is not loaded and the route answers the not-found page, or a no-access state for a missing permission. A site switch on a half's page goes to the start page when the plugin is disabled in the new site and otherwise navigates to the route again, and a page the new site or the caller may not see answers as above although its half is loaded. A half that fails to load gives an error state with a retry. The field editor, list cell and filter registries take a half's field types with `AddLazy`, gated by the same enablement, and load the half in `FindAsync`, `CellForAsync` and `FindAsync`; the synchronous lookups answer a half's type only once loaded and while enabled, and core's types as before. A form and a list load the halves of their fields' types when they open. A half is loaded once per page load with `LazyAssemblyLoader`: its plugin class is created, `Configure` runs once into a `ServiceCollection` of the half, whose provider resolves every service the half does not register from the admin's container, so the half shares the admin's instances; `IAdminPluginServices.ProviderOf` answers it for a type of the half, and `FindAsync<TService>` loads the enabled half whose manifest names the service and answers the half's own registration of it, else one instance of the class the manifest names. The app's partial trimming keeps a half whole, so a half that sets `IsTrimmable` must keep its contribution classes itself. `FluentCMS.Plugins.Media.Admin` under `admin/src/plugins/` is the stub half: `/media` with its menu item in the media section and the permission `media.read`, and its manifest test in `admin/tests/FluentCMS.Plugins.Media.Admin.Tests`. A half takes `Microsoft.Extensions.Localization` for `IStringLocalizer<T>` over its resources, and `FluentCMS.Admin.Components` keeps the component library's compile assets private, so a half that names the library's types does not compile (#3178). The first download grew from 3,421,629 B to 3,459,163 B, most of it the manifest's JSON metadata, which the app now reads; the half itself is left out.

### 2.7 Calling the API

One `HttpClient` pipeline, registered by `FluentCMS.Admin` and handed to plugin admin halves through `FluentCMS.Admin.Abstractions` (5.1):

- **Headers.** `Authorization: Bearer`, `X-Cms-Site` with the site of the shell, and `X-Cms-Locale` with the content locale of the view, on every Management call. The base address is `{apiBaseUrl}/api/management/v1/`.
- **Problems.** A response of `application/problem+json` becomes an `ApiProblem` with `status`, `code` (`FCMS...`), `title`, `detail`, `errors[]` of `{ path, code, message }`, `current` on a 412 and `traceId`. Forms map `errors[].path` to fields (4.5); other views show `title` and `detail` and offer `traceId` to copy. The admin never parses `detail` for meaning; it branches on `status` and `code` only.
- **Concurrency.** A read keeps the `ETag`; a save sends it as `If-Match` (`"0"` to add a locale), as the content group requires (`FCMS3130` without it, backend 4.10). A weakened tag passed on by a proxy is read by its value, as the server does.
- **Idempotency.** Create and bulk calls send an `Idempotency-Key` per user action, so a retry after a network error does not create twice; the key is kept until the action has an answer.
- **Rate limits.** Every tab of a user shares the principal's budget (300 requests a minute by default). A 429 waits the `Retry-After` it carries and tries once more, then shows a notice. Views avoid one request per row: labels of referenced entries are read with one list call per type (`filter[id][in]=...`), and relations in a list are read with `populate` and `fields`.
- **Caching.** `/schema` and `/plugins` are read once per site per session and again on a site switch or a reload; the schema changes only with a new server process (backend 4.2). Lists and entries are not cached: a Management read answers `If-None-Match` with a 200 and the whole body, so a conditional read saves nothing today.
- **Polling (decision 16).** No Must row polls. The jobs page (4.15) refreshes a running job's status at most every five seconds while the page is visible, which costs one read each; nothing else polls. Locks (row 3.21) and live updates need the SignalR channel and are Should.
- **Serialization.** `System.Text.Json` with source-generated contexts for the records, so that the app trims; entries stay `JsonObject`.

### 2.8 Package map

*Proposed* NuGet packages, all `FluentCMS.*`, at the one version of B-108.

| Package | Contents | Depends on |
| --- | --- | --- |
| `FluentCMS.Admin.Abstractions` | The contract of an admin half (5.1): the manifest attribute, `IAdminPlugin` and its builder, the contribution attributes, the base classes of field editors, list cells and actions, the services a component may inject (API client, current site and locale, access, notifications, dialogs, navigation, pickers), and the records of the Management API the admin reads. Semver-guaranteed and tracked with the public API analyzers, as the SDK is (decision 14). The build step that writes the manifest of 2.6 ships in its `build/` folder. | `Microsoft.AspNetCore.Components.Web`, `Microsoft.Extensions.*` abstractions |
| `FluentCMS.Admin.Components` | The wrapped component set, theme tokens and icons (2.3). Its component parameters are part of the semver promise. | `FluentCMS.Admin.Abstractions`, the component library of decision 26 |
| `FluentCMS.Admin` | The shell, sign-in, the API client, localization, the generated lists and forms, the field editors of the core field types, the relation picker, publishing controls, versions, preview, bulk actions, the account page, and the pages of core's groups: users, roles, keys, members, sites, locales, settings, plugins, jobs and diagnostics. `AddFluentCmsAdmin()`. | Abstractions, Components, `Microsoft.AspNetCore.Components.WebAssembly`, the sign-in library (AQ2) |
| `FluentCMS.Admin.RichText` | The rich text editor: Tiptap through JS interop, with its bundle as a static web asset (4.7). Loaded lazily. | Abstractions, Components |
| `FluentCMS.Admin.Hosting` | Serves an admin app from an ASP.NET Core host (6.1): `cms.AddAdmin()` and `app.MapFluentCmsAdmin()`, `config.json` from configuration, the registration of the admin client, the admin page's headers, and the prebuilt first-party build (AQ8). | `FluentCMS.Core`, the ASP.NET Core framework |
| `FluentCMS.Plugins.Media.Admin`, `.Webhooks.Admin`, `.Mcp.Admin`, `.AiAssist.Admin`, `.MultiSite.Admin`, `.Search.Admin` | The admin halves of the first-party plugins (5.2 to 5.7). | Abstractions, Components |
| `FluentCMS.Templates` | Gains `fluentcms-admin` and the admin parts of `fluentcms` and `fluentcms-plugin` (6.2). | none |

Not a package: `FluentCMS.Admin.App`, the app itself, which is the prebuilt static build (6.3) and what the reference host serves (6.1).

A plugin ships its two halves as two packages with a shared ID prefix (row 8.1), `FluentCMS.Plugins.Media` and `FluentCMS.Plugins.Media.Admin`, not as one: the server half needs the ASP.NET Core framework, which a WebAssembly app cannot load, and the admin half would otherwise bring server code into the download. GraphQL has no admin half: its options are host configuration (backend 5.3).

## 3. Repository layout

The admin fills the reserved `admin/` folder of the monorepo (decision 59). *Proposed*:

```text
admin/
+-- README.md
+-- src/
|   +-- FluentCMS.Admin.Abstractions/
|   +-- FluentCMS.Admin.Components/
|   +-- FluentCMS.Admin/                    Shell/, Content/, Fields/, Publishing/, Versions/,
|   |                                       Preview/, Bulk/, Account/, Identity/, Sites/,
|   |                                       Settings/, Plugins/, Jobs/, Localization/
|   +-- FluentCMS.Admin.RichText/           the component, and js/ with the Tiptap bundle (AQ7)
|   +-- FluentCMS.Admin.App/                Program.cs, wwwroot/index.html, wwwroot/config.json
|   +-- FluentCMS.Admin.Hosting/
|   +-- plugins/
|       +-- FluentCMS.Plugins.Media.Admin/
|       +-- FluentCMS.Plugins.Webhooks.Admin/
|       +-- FluentCMS.Plugins.Mcp.Admin/
|       +-- FluentCMS.Plugins.AiAssist.Admin/
|       +-- FluentCMS.Plugins.MultiSite.Admin/
|       +-- FluentCMS.Plugins.Search.Admin/
+-- tests/
    +-- FluentCMS.Admin.Tests/              component tests
    +-- FluentCMS.Admin.ApiTests/           the admin's client against FluentCMS.Testing
    +-- FluentCMS.Admin.EndToEnd/           browser tests against the reference host
    +-- FluentCMS.Plugins.*.Admin.Tests/
```

Elsewhere: the admin's rules join `tests/FluentCMS.Architecture.Tests` (3.3); `templates/fluentcms-admin/` is the new template (6.2); `src/FluentCMS.Host` references `FluentCMS.Admin.App` and `FluentCMS.Admin.Hosting` (6.1). The admin projects are in `FluentCMS.slnx`, so `dotnet build -c Release -warnaserror` builds them with the rest. The plugin admin halves sit under `admin/` rather than beside their server halves in `src/plugins/`, so that a path filter tells admin work from backend work in CI (AQ16).

### 3.1 Build and quality conventions

- The conventions of backend 3.2 hold: `net10.0`, nullable, warnings as errors, analyzers at `latest-recommended`, central package versions, xUnit v3 with Shouldly.
- `FluentCMS.Admin.Abstractions` and `FluentCMS.Admin.Components` use the public API analyzers, with every entry in `PublicAPI.Unshipped.txt` until 1.0.0, as the SDK does (H1).
- The app publishes trimmed. Records are serialized through source-generated contexts, and trim warnings are errors.
- Static web assets are fingerprinted and compressed at publish, so the host can cache them for a year and `index.html` and `config.json` not at all.
- Strings shown to a user come from resources (4.17); a test with a pseudo-culture fails on a hard-coded one (7).
- No JavaScript outside an allow-list: the script of the component library, loaded by `FluentCMS.Admin.Components` (2.3); the rich text bundle; the document module of `FluentCMS.Admin`, whose one type `DocumentInterop` sets the document's `lang` and `dir` (4.17), the accent color of the branding as a custom property on the document element through `style.setProperty` (2.5), returns focus to the opener of a dialog of the admin's own, and moves focus to the page's heading after a route change and after the drawer closes on one (4.18); the session storage type `SessionStorageInterop` of `FluentCMS.Admin`, which reads and writes the tab's `sessionStorage` for the sign-in and the drafts of 2.4 through the browser's own `sessionStorage` functions, with no module; the theme storage type `ThemeStorageInterop` of `FluentCMS.Admin`, which keeps the theme the user chose (4.1) under one key of `localStorage`, holding `light` or `dark` and nothing else, through the browser's own functions; the column storage type `ColumnStorageInterop` of `FluentCMS.Admin`, which keeps the columns a user chose for a list (4.4) in `localStorage` under a key of the user's id, the site's id and the type's name, holding column names and nothing else, through the browser's own functions; and small interop modules for clipboard, file drop and `BroadcastChannel`, each in `FluentCMS.Admin` or `FluentCMS.Admin.RichText`.

### 3.2 Naming conventions

| Thing | Convention | Example |
| --- | --- | --- |
| Admin route of a content list and entry | `/content/{type}`, `/content/{type}/{id}`, `/content/{type}/new` under the app's base | `/admin/content/article/0193…` |
| Admin route of a plugin page | `/{plugin}/...`, the server plugin's name first | `/admin/media`, `/admin/mcp/clients` |
| Admin route of the component gallery | `/system/components`, every component of the wrapped set | `/admin/system/components` |
| Reserved first segments | `content`, `settings`, `account`, `users`, `roles`, `api-keys`, `members`, `sites`, `plugins`, `jobs`, `system`, `authentication` | a plugin of such a name stops the app with an error page |
| Component | `Cms` prefix in the wrapped set; area name in the core areas | `CmsDataGrid`, `ContentListPage` |
| Resource key | `{Area}.{View}.{Text}` | `Content.List.Empty` |
| Admin half package | `{server package}.Admin` | `FluentCMS.Plugins.Media.Admin` |

The component gallery ships in the product, linked from the start page and from no menu, since it is the page the A0 exit criterion names (#3180).

### 3.3 Architecture tests

`FluentCMS.Architecture.Tests` gains rules for the admin, each also run against a type that breaks it, as the GraphQL rules are (backend 3.4):

- No admin assembly that runs in the browser references `FluentCMS.Core`, `FluentCMS.Sdk`, `FluentCMS.Plugins.Abstractions`, a data provider or a server half (decision 4).
- An admin half references `FluentCMS.Admin.Abstractions` and `FluentCMS.Admin.Components` only, among FluentCMS packages, and never the component library or `FluentCMS.Admin` (2.3).
- `FluentCMS.Admin.Abstractions` references no implementation package.
- `FluentCMS.Admin.Hosting` is the only admin package that references `FluentCMS.Core`, and no admin assembly that runs in the browser references it.
- Only the allow-listed types use `IJSRuntime` (3.1).

As built (A-06, #3144): the project rules read the project files and follow project references through the repository; a development-only FluentCMS package counts unless it is `FluentCMS.Analyzers` or `FluentCMS.Generators`. An admin assembly that runs in the browser is every project under `admin/src/` but `FluentCMS.Admin.Hosting`, and every admin half; among FluentCMS packages it may reach only those and `FluentCMS.Client`, which is also checked on the references of each compiled assembly of `admin/src/`, so a `<Reference>` or a reference added by a `Directory.Build.props` is seen. An admin half is any project but the six packages of `admin/src/` that is under `admin/src/plugins/`, is named `*.Admin`, or references `FluentCMS.Admin.Abstractions` or `FluentCMS.Admin.Components`, wherever it is, so a half in a sample or a template, or one placed directly under `admin/src/`, is found too; it may reach no other FluentCMS package except through those two. The component library is what `FluentCMS.Admin.Components` references, matched by package id and the ids below it: no browser project reaches it except through the wrapped set, and no type outside the wrapped set's assembly depends on the `Microsoft.FluentUI` namespace. The interop rule scans the compiled browser assemblies of `admin/src/`, which a test requires the architecture tests to reference, so a `.razor` file's `@inject` counts: a type outside the list may not depend on `Microsoft.JSInterop` or `System.Runtime.InteropServices.JavaScript`. The list holds `DocumentInterop` and its nested type `FocusOpener`, `SessionStorageInterop` (A-07, #3145), `ThemeStorageInterop` (A-08, #3146) and `ColumnStorageInterop` (A-12, #3231); the library's script is loaded by the library's own types, and the rich text and clipboard, file drop and `BroadcastChannel` types join it by name with their issues. `FluentCMS.Admin.Components` keeps the library's compile assets private (`PrivateAssets="compile;contentfiles;analyzers;build"`), so code past the wrappers in the shell or a half does not compile either.

## 4. Areas

Each area lists the rows it implements (category.row; 3.4 is category 3, row 4), the design, the endpoints it calls and what it waits for. An endpoint marked "exists" is on `feature/m7`; any other names the issue that adds it.

### 4.1 Shell

Rows 3.1, 7.13 (site switcher and per-site admin), 4.12 (hide what the user cannot do), 8.15 (a disabled plugin contributes nothing).

- **Start.** After sign-in the shell reads `GET /me`, `GET /me/sites`, then for the site chosen `GET /schema`, `GET /plugins` and the caller's access (#3096): five requests. A user with no site gets a page that says so; a user whose roles lack `schema.read` gets a sidebar without content types and a notice, since the admin can draw no type without it.
- **Sidebar.** Collections and singletons the caller may read, from `/schema`, in schema order, singletons apart and global types marked; then the sections of plugin admin halves (2.6) and core's administration pages, each shown only when the caller's access allows it. A type's label is a resource key `ContentType.{name}` when the app has one, else its `displayName` from `/schema`, else its name made readable, `site-settings` as "Site settings" (AQ10).
- **Site switcher.** The sites of `/me/sites` (row 7.13: only sites with a role). Switching sets `X-Cms-Site`, reads `/schema`, `/plugins` and the access of the new site, and keeps the route when its type exists there.
- **User menu.** Account (4.3), interface language, theme (system, light, dark), sign-out.
- **Breadcrumbs** from the route: section, type, entry label, view.
- **Layout.** Sidebar, header and content; under 1024 CSS pixels the sidebar becomes a drawer. Touch targets are at least 24 by 24 pixels and no action is reachable by hover alone, so the admin is usable on a tablet (row 3.1).
- **Themes.** Light and dark from the wrapped set (2.3), following `prefers-color-scheme` until the user chooses. The choice is kept in the browser, in one key of `localStorage` (3.1), since the preferences of #3096 hold no theme; a token is never kept there (AQ3).
- **Entry labels.** An entry's label is the first of: the value of its type's `titleField` from `/schema`, a text field named `title` or `name`, the source field of its slug, its first required text field, its id (AQ10).

| Endpoint | Use | Status |
| --- | --- | --- |
| `GET /me` | user name, email, roles, `mustChangePassword`, `twoFactorEnabled` | exists |
| `GET /me/sites` | site switcher | exists |
| `GET /schema` | sidebar, lists, forms | exists |
| `GET /plugins` | `enabled` for the site, `contributions` | exists |
| `GET /me/access` | what the caller may do in the site and the instance | [#3096] |

Without #3096 the shell shows every type of `/schema` and every page, and answers a 403 where it comes; A-08 waits for it.

### 4.2 Runtime configuration

Row 3.2. `config.json` as in 2.5. The endpoints are those of the issuer's discovery document; nothing of the Management API is read before sign-in. CORS and the client registration of an admin hosted on another origin are host configuration (6.4).

### 4.3 Sign-in, session and account

Rows 3.3, 4.2 (session list with revoke), 4.15 (SSO buttons, on the hosted login), 4.16 (two-factor).

Sign-in, refresh, sign-out and expiry with the draft kept are as in 2.4. The account page holds what a user manages for itself:

| Endpoint | Use | Status |
| --- | --- | --- |
| `/connect/authorize`, `/connect/token`, `/connect/revoke`, `/connect/logout`, `/.well-known/openid-configuration` | 2.4 | exist |
| `GET /me/sessions`, `DELETE /me/sessions/{id}` | the user's sessions, ending one | exist |
| `GET /me/two-factor`; `POST /me/two-factor/enrol`, `/verify`, `/recovery-codes`, `/disable` | two-factor, with the authenticator URI as a QR code drawn in the browser | exist |
| `GET /me/logins`, `DELETE /me/logins/{provider}` | external logins | exist |
| `/account/change-password` | the hosted page, opened in the same tab | exists |
| `GET` and `PUT /me/preferences` | interface culture, time zone, formats (4.17) | [#3096] |

A key or a service account cannot sign in to the admin, which is a code-flow client. Called with one, `GET /me` answers 200 with `{ id, kind, name }` and `GET /me/sessions` an empty list; only the two-factor and logins routes answer 403.

*As built (A-09, [#3147]).* `AccountPage` at `/account`, in `Account/` of `FluentCMS.Admin`, reads each section on its own, so a 403 (shown as "only a user has this") or a failure with "Try again" stays in its section.

- **Sessions.** A row is one authorization. The API does not mark this tab's, so none is marked; the page says that ending one stops that sign-in at its next request and does not sign the browser out of the authorization server. Ending this tab's makes the next read a 401, which the session's own path of 2.4 answers by signing in again.
- **Two-factor.** The QR code is drawn as an SVG by `QrCode` (byte mode, level M, versions 1 to 15, up to 412 bytes) and `QrCodeImage`, both derived from Project Nayuki's QR Code generator, its `QrCode.java` and the `toSvgString` of its demo (MIT; the notice is in `THIRD-PARTY-NOTICES.md`, which the `FluentCMS.Admin` package carries). QRCoder 1.8.1, the package the issue named, brings `System.Drawing.Common` on `net6.0` and up. A module is 4 CSS pixels, scaled down only on a screen narrower than the code. When the address for the app, with the site's name, does not fit, the page shows the key alone to enter by hand. The key and the recovery codes live in the component while their step is open, and are dropped when the user leaves the step or the page; they are never logged, put in the URL or stored in the browser.
- **Signing in again.** Turning two-factor on or off, and removing an external login, give the user a new security stamp, which revokes every session of the user ([#3202]): the tab's access token is refused at its next request, the token endpoint refuses its refresh token, and the hosted pages' cookie no longer holds, so the user signs in again, with the password (and a code, once two-factor is on). The page says so before each change, and signs in again through the session (2.4) once the recovery codes are saved, after turning two-factor off and after removing a login. If the authorization server cannot be reached for it, the page says so. The recovery codes are shown in a modal dialog, which the browser keeps the rest of the page and the shell from, and the page's other cards are inert until the tab has left to sign in, so that no call can take the codes away; a reload or going back still can. New recovery codes keep the session.
- **Password.** A link to `{authority}/account/change-password` in the same tab, with `ReturnUrl` set to the account page when the admin is on the authority's origin, since the hosted page returns only to a local URL; on another origin the page says it does not come back.
- **Language and theme.** The theme is the shell's, kept in the browser (4.1). The language select shows only when more than one culture is offered; it saves the `culture` of `PUT /me/preferences`, with the other preferences read again just before. The start does not apply it yet ([#3214]).

### 4.4 Generated list views

Rows 3.4, 7.7 (locale switcher and translation status in lists), 6.5 (schedules visible in list views), 2.23 (`q=` through the search plugin).

- **Columns.** The system fields (status, locale, updated at and by) and the type's fields that a cell can show, chosen per user, site and type and kept in the browser; saved views are row 3.22, a Should. Columns read only what they show: `fields=` lists them, and `populate` with `fields` reads the label of a relation.
- **Sort.** Several keys, `sort=-updatedAt,title`, on the system fields and the fields whose type sorts.
- **Filters.** Built from the field type: the admin holds a table of the operators of the query model (backend 4.4, row 2.4) that each core field type takes, such as `eq`, `contains` and `startsWith` for text, `lt` to `between` for numbers and dates, `in` for select and relation, and `null` for any. A plugin field type names its operators in its admin half (5.1). A filter on a field the caller may not read is not offered, since the field is not in its schema.
- **Search.** A search box sends `q=` when the type is `searchable` in its schema, and is hidden otherwise (`FCMS1700`).
- **Paging.** `page`, `pageSize` and `withTotal` for page numbers; pickers use `cursor` for "load more".
- **Badges.** Status (draft, published, modified, unpublished); with `locale=all`, one badge per locale of the site with its status, and a missing translation as an empty badge (row 7.7). A pending schedule shows as a badge with its time once #3094 is merged.
- **Selection.** Rows, all on the page, or all that match the filter, for bulk actions (4.13).
- **Row actions.** Open, open in a side panel, and the entry actions of plugins (5.1).

| Endpoint | Use | Status |
| --- | --- | --- |
| `GET /content/{type}` with `fields`, `sort`, `filter[...]`, `q`, `page`, `pageSize`, `withTotal`, `cursor`, `locale=all`, `populate` | the list | exists |
| `GET /schema/types/{type}` or `/schema` | columns, filters, `searchable` | exists |
| `scheduledPublishAt`, `scheduledUnpublishAt` on each entry | schedule badge | [#3094] (M7) |
| the site's locales | locale badges and switcher | [#3096] |

**As built (A-12, [#3231]).** `ContentListPage` at `content/{type}` lists a collection, and shows A-13's `ContentForm` for a singleton. The first column is the entry's label (the type's label field of 4.1, or the id), always shown and linked to the entry; the others are status, locale for a localized type, updated at and by, and each field that is not hidden and whose type has a cell in `ListCellRegistry` (`FluentCMS.Admin.Abstractions`) or reads as text, which `TextListCell` shows. `fields=` names the label and every shown column, system members included, since a read with `fields=` has only what it names, or `id` alone; a relation is read as `{field}.{label of its target}` with `populate={field}`. Sort keys are the label, updated at and the single-valued fields of the types the query model sorts (text, email, url, slug, integer, decimal, boolean, date, dateTime, select); a key of the address that is not one is dropped, and `-updatedAt` is the default. `columns`, `sort`, `page` and `pageSize` (1 to 100, 25 by default) are in the query string, read as text so that a malformed one reads as the default, and a page past the end goes to the last page; a site switch leaves them behind; the column choice is also kept by `ColumnStorageInterop` per user, site and type, and an address without `columns` takes it. Selection is per page, and "all that match" is a state of its own over the read's query for A-32; a change of the query clears both. "New" shows when `IAdminAccess.ForType(type).Create` is true. The data grid is a table of the wrapped set's own rather than the library's grid (#3178): a native checkbox per row, a button with the checkbox role in the header for the mixed state, `aria-selected` on rows, the checkbox, or the row without selection, as the one tab stop of a row, the up and down arrows between rows, Enter to open, external sort with several keys (Shift with a click or with Enter adds one, which each sortable header's description says; `aria-sort` marks the first key and the button says the level of the others), `aria-rowcount` and `aria-rowindex` for a page of a larger list, loading and empty rows, and columns placed by `Order`.

**As built (A-48, [#3232]).** The operators each field type offers are `ListFilterRegistry` (`FluentCMS.Admin.Abstractions`, `FluentCMS.Admin.Lists`), which holds the core types and takes a plugin's with `Add(fieldType, operand, operators)`, the seam A-18 fills. The table is the server's set narrowed to what a filter of the list needs: each takes `null` and `notNull`, and text, email, url and slug also `eq`, `ne`, `contains`, `startsWith`, `in` and `nin`; long text `contains` and `startsWith`; integer and decimal `eq`, `ne`, `lt` to `between`, `in` and `nin`; date `eq`, `ne` and `lt` to `between`; date and time `lt` to `between`; boolean `eq` and `ne`; select `eq`, `ne`, `in` and `nin`, and a list of options `contains`, `in` and `nin`; relation `eq` and `in`; blocks `hasBlock`, of a block type of its `blocks` setting; rich text and JSON nothing more. `Add` has an overload that names the operators of a field of the type that holds a list; otherwise such a field takes those of the type's that test members (`contains`, `in`, `nin`, the null tests). A component, a hidden field and a type that names no operators are offered no filter; an API test checks that the server takes every operator the admin offers. A filter is set in a dialog (field, condition, value read as the field's type: a number in the user's format, a moment in the user's time zone and sent in UTC, where a day without a time is its start, or its end for `lte`, `gt` and the end of `between`; options as checkboxes, shown by their labels; values of a list comma separated, `\,` for a comma; until A-15 a relation takes entry ids) and shown as a button that opens it again, beside one that removes it. After a read whose filters or search changed, the number found is announced. A null test of the address reads `true` or nothing as the test and `false` as its opposite, as the server does. Filters and the search are in the address in the API's own grammar, `filter[title][contains]=a&q=tour`, written by the same writer as the read and checked against `clients/filter-cases.json`; a filter the list does not offer or whose operand does not read is dropped, and a change of either leaves `page` out. The search box shows when the type has a searchable field that is not hidden, `GET /me/access` says the site has an index, and the search plugin, when it is listed, is not disabled there; a search is trimmed and at most 256 characters. The status column is a badge per status; for a localized type in a site with more than one enabled locale, while the content locale is the site's default, it reads with `locale=all`, which the engine matches, sorts, searches and pages in the site's default locale, and shows one badge per enabled locale of `GET /me/sites` in the site's order, an empty one where the entry has no translation; the row shows the entry in the content locale. The status column also reads `scheduledPublishAt` and `scheduledUnpublishAt`, each shown as a badge with its time in the user's time zone. With another content locale the list reads that locale alone, which the engine matches, sorts and searches in it, and then the status of the page's entries in each other enabled locale by a read of that locale limited to their ids (`filter[id][in]`, `fields=id,status,scheduledPublishAt,scheduledUnpublishAt`), one per locale in batches of 99 ids, since each id is a node of the filter and the server allows 100: a read of `locale=all` limited to the ids would match in the site's default locale and miss an entry that has no translation there (A-17). A locale whose read is refused or fails gets no badge, and costs the others nothing.

### 4.5 Generated edit forms

Rows 3.5, 1.8 (conditional fields enforced in the admin), 1.10 (field metadata and hints), 4.5 (hidden fields omitted from forms), 6.8 (conflicting saves with both versions).

- **Layout from metadata.** `display` gives label, help, placeholder, group, tab and width; fields without a group form the main group; tabs follow the order of their first field. `readOnly` fields show as text. Fields the caller may not read are absent, since the schema leaves them out; `hidden` fields stay in the schema with `hidden: true`, and the form leaves them out itself.
- **Defaults.** A new entry starts from each field's `defaultValue`.
- **Conditions.** `visibleWhen` and `requiredWhen`, as `{ "operator", "field", "values", "operands" }`, are evaluated on every change against the form's values, so a field appears, hides or turns required at once. The server enforces them again at publish (backend 4.4); the form's evaluation is a convenience.
- **Validation.** Lengths, range and pattern of `validation` are checked inline as the user leaves a field, without a request. `required` is marked while editing and checked before a publish only, since a draft skips it (row 1.7); `unique` is the server's to check. A 422's `errors[].path` (`sections[0].text`, `seo.metaTitle`) is mapped to the field, block or component at that path, which is scrolled to and focused; an error whose path matches no field shows above the form.
- **Changeable fields.** Fields the caller may read but not change are read-only, from `creatableFields` or `updatableFields` of `GET /me/access` (#3096); the form sends them back as read, which the server takes as no write, and a create leaves out the default of one the caller may not set. Save is absent for a caller who may not create, or not update, entries of the type.
- **Dirty guard.** Leaving a form with unsaved changes asks first, for a route change, a site switch, a sign-out and a closed tab (`beforeunload`). A site switch or sign-out the user confirmed, and a session end that kept the form as a draft, give the form up, so the redirect that follows does not ask again.
- **Shortcuts.** `Ctrl+S` or `Cmd+S` saves, `Ctrl+Shift+Enter` or `Cmd+Shift+Enter` publishes (once A-16 gives the publish, after the checks of a publish), `Escape` closes a dialog or a side panel; the list of shortcuts opens with `?` outside a text field.
- **Conflicts.** A 412 opens a dialog with the user's values beside `current` for each field the user may change that differs, with three choices: keep mine (`current` with the fields the user changed, saved on `current`'s version, so a field only the other save changed keeps its newer value), take theirs (discard mine) or compare field by field (a choice per field, then the same save).
- **Unknown field types.** A field whose editor no loaded admin half provides, such as one of a plugin without an admin half or one disabled in the site, shows its JSON read-only with a note.

| Endpoint | Use | Status |
| --- | --- | --- |
| `GET /schema/types/{type}` | the form | exists |
| `GET /content/{type}/{id}` (with `ETag`) | read | exists |
| `POST /content/{type}` | create, 201 with `Location` | exists |
| `PUT /content/{type}/{id}` with `If-Match` | save; `"0"` adds a locale | exists |
| `PATCH /content/{type}/{id}` with `If-Match` | not used by the form, which sends the whole draft; kept for actions that change one field | exists |
| the fields the caller may change | read-only state | exists (#3096) |

### 4.6 Field editors

Rows 3.6, 8.5 (a field type's Blazor editor and list cell), 7.9 (direction and formats in editors).

One editor and one list cell for each core field type of row 1.3, reading the field type's `settings` and `validation` from the schema:

| Field type | Editor | Settings read |
| --- | --- | --- |
| text, long text | input, auto-growing text area, with length counter | lengths, pattern |
| integer, decimal | number input in the user's number format | range |
| boolean | switch | |
| date, date-time | pickers in the user's format; a date-time is shown and entered in the user's time zone | range |
| select (single, multi) | single: a dropdown, which an optional field can clear (the wrapped set has no radio group); multiple: a list of checkboxes at any number of options, scrolling from five, since the wrapped set has no multi-select dropdown; a chosen value the options no longer list is shown so that it can be taken off | `options` |
| slug | text generated from its source field as the user types, until locked; a lock button keeps it | `source` |
| JSON | code area with parse errors on the line, and formatting | |
| email, URL | input with type-specific keyboard and validation | pattern |
| relation | the relation picker (4.9) | `relation.target`, `ordered` |
| rich text | the rich text editor (4.7) | `blocks` |
| component, blocks | 4.8 | `component`, `blocks` |
| media | the media plugin's editor (5.2) | |

Every editor gets the field's description, its value, its errors, read-only, the content locale and its text direction (4.17), and raises changes; the base class is in `FluentCMS.Admin.Abstractions`, so a plugin's field editor is built the same way (5.1).

### 4.7 Rich text editor

Rows 3.7, 1.9, 5.12 (media in rich text through the same picker); decisions 18 and 27.

- **Engine.** Tiptap through JS interop, inside `FluentCMS.Admin.RichText`, loaded lazily. Only the openly licensed packages of Tiptap are used (decision 9). Tiptap is not an extension contract (decision 27): a plugin cannot add nodes or marks.
- **Format.** The editor's ProseMirror schema is the server's rich text model (backend 4.2, `RichText/`): the same node and mark names, inline blocks with their `attrs.id` (ADR 0002), links to entries and media references in the form the server stores and extracts. The document goes to and from Blazor as JSON on every change, debounced, and Blazor holds the form's value.
- **Features.** Marks, headings, lists, links to URLs and to entries (with the relation picker in a dialog), media embeds (with the media picker, 5.2), typed inline blocks from the field's `blocks` (each with a small generated form, as 4.8), paste cleanup that keeps only what the schema allows, Markdown shortcuts (`#`, `-`, `1.`, `**`, `>`), undo and redo.
- **Accessibility.** A labelled toolbar with roving focus and keyboard shortcuts for every button; the editor region has the field's label.
- **Round trip.** A document the editor writes is read back unchanged by the server; a test set of documents checks it (7).

| Endpoint | Use | Status |
| --- | --- | --- |
| `GET /content/{type}` with `q` or `filter[...]` | entry links | exists |
| `GET /media`, `POST /media` | embeds, through the media picker | exist |
| the URL of an asset on Management reads | embed preview | [#3100] |

### 4.8 Component and block editors

Rows 3.8, 1.5, 1.6; ADR 0002; nesting depth 3 (decision 20).

- **Components.** A single component is a collapsible group of its fields; a repeatable one a list of such groups with add, remove and reorder.
- **Blocks.** A block list with an add-by-type picker from the field's union, drag to reorder with buttons and keys to move up and down (WCAG 2.5.7), duplicate, remove, collapse one or all, and a one-line preview of each collapsed block from its first text field.
- **Ids.** A block keeps the `id` the server gave it. A new, duplicated or pasted block gets a fresh 12-character id made in the browser, never the source's, since a repeated id is refused (`FCMS1407`) and an unknown one is a new block (ADR 0002).
- **Depth.** The add picker offers no block type that would nest deeper than the schema allows.

The endpoints are those of the form (4.5); a block's fields are part of the entry.

### 4.9 Relation picker

Row 3.9; row 1.4.

- A dialog over the target type's list (4.4): search with `q` or a `contains` filter on the label field, the target's filters, multi-select for a multi relation, and order by drag or keys for an `ordered` one.
- Inline create: a side panel with the target's form (4.5); the new entry is selected when saved.
- Open a related entry in a side panel without leaving the form.
- A relation to a type the caller may not read is absent from the schema, so it is not offered.

| Endpoint | Use | Status |
| --- | --- | --- |
| `GET /content/{target}` with `q`, `filter[...]`, `cursor` | the picker | exists |
| `GET /content/{target}/{id}` | side panel | exists |
| `POST /content/{target}` | inline create | exists |

**As built (A-15, [#3236]).** `RelationFieldEditor` and `RelationListCell` are the editor and cell of `relation`. The editor reads a value as a read gives it, a reference `{ "id", "type" }`, an entry or an id, and a change writes ids, one or a list, which the server takes beside the references a save sends back unchanged; each related entry shows by its label (4.1), read once for the ids the form holds with `filter[id][in]` and `fields=id,{label}`, or its id when it cannot be read; a read that failed is tried again on the next render, and an entry the caller may not read or that no longer exists keeps its id. An entry a relation to many holds more than once, which the server keeps, keeps each of its places. A target absent from the schema, which the caller may not read, leaves the ids shown and offers no picker. `RelationPicker` is a dialog over the target's list: the label and status columns, `fields=id,status,{label},updatedAt`, sorted by the label when it is a text and by `-updatedAt` otherwise, 25 a page with `withTotal=false`, and "load more" by `nextCursor`; A-48's `ListToolbar` gives the search and the target's filters, held in the picker as a query string in the address's grammar rather than in the address. The search sends `q` under the conditions of the list's search box, and a `contains` filter on the label field otherwise. A relation to many takes several entries by checkbox, in the order they were chosen, and keeps chosen entries the pages read do not show; for one, Enter on a row chooses it. An `ordered` relation to many orders by drag (native HTML drag and drop; `document.js` sets the drag's data, which Firefox needs), by a button up and down per entry, and by Alt with the up and down arrows, each move announced with its position. `EntryPanel` is a drawer at the end of the page with `ContentForm` in a mode of its own (`InPanel`): a create stays in the panel and raises `OnSaved` instead of moving to the entry's route, its heading is an `h2` that takes focus once the form is read, and the document's shortcuts are not listened for, since a dialog has none; its drafts are kept under a type of their own (`panel:{type}`), so a panel neither restores nor removes the draft of the page's form of the same type. The panel's Close button, Escape and a click outside close it; with unsaved changes it asks first, and keeping them opens it again; closing it gives the changes and their draft up, and focus returns to the control that opened it. A new entry of the target is offered when `IAdminAccess.ForType(target).Create` is true and is chosen once saved, after those chosen before for a relation to many; a related entry opens in the same panel, and a save there relabels it. The list's last column holds the row action "open in a side panel", a button after the row's checkbox in the tab order whose Enter and arrows are its own rather than the row's; a save in its panel reads the list again once the panel closes. A relation to one and a filter with `eq` say "an entry" in the picker's title and button, one to many and `in` "entries". A relation filter of A-48 takes its entries from the picker, one for `eq` and several for `in`, and is shown by their labels, read for the ids of the address; a relation to a type the caller may not read still takes ids. The picker and the panel take the content locale of their form, the view's when it has none, and a target that is not localized is read without one.

### 4.10 Draft, publish and status controls

Rows 3.11, 6.1, 6.2, 6.5, 6.7 (warnings for unpublished references), 7.6 (per-locale publish and schedule).

- **Controls.** Save draft, publish, unpublish, discard changes, each shown by the entry's status and the caller's access (#3096), and each in the locale of the form for a localized type.
- **Status.** A badge with the status of the locale and a "Modified" indicator when a published locale has a newer draft; under `locale=all` the status of every locale (row 7.7).
- **Publish warnings.** `POST .../publish` answers `{ "entry", "warnings" }`; warnings about unpublished references (decision 41) are listed after the publish, with links to the entries. A type set to block answers a 422 instead, shown as errors.
- **Schedule.** A dialog for publish or unpublish at a time, with the user's time zone named (`at` without an offset and `timeZone`), and the pending schedules with move and cancel. Showing a pending schedule needs #3094 (M7); A-27 waits for it.
- **Discard** asks first, since it drops the draft.

| Endpoint | Use | Status |
| --- | --- | --- |
| `POST /content/{type}/{id}/publish`, `/unpublish`, `/discard` | controls | exist |
| `POST /content/{type}/{id}/schedule`, `DELETE .../schedule/{action}` | schedule and cancel | exist |
| `scheduledPublishAt`, `scheduledUnpublishAt` on reads | show the pending schedule | [#3094] (M7) |
| `GET /content/{type}/{id}?locale=all` | status of every locale | exists |

### 4.11 Version history panel

Rows 3.12, 6.3, 6.4.

- A side panel lists the versions newest first, with author, time and origin. The author is named by kind and name once #3097 is merged; before it the panel shows "you" for the caller's own id, "system" for the empty id and the id otherwise.
- Compare any two versions side by side, from `changes` of the diff: fields added, removed and changed, blocks by id, rich text by its text.
- Restore makes a new draft from a version, after a confirmation that names it, and sends `If-Match` with the draft's version.

| Endpoint | Use | Status |
| --- | --- | --- |
| `GET /content/{type}/{id}/versions`, `GET .../versions/{n}` | list and read | exist |
| `GET .../versions/{a}/diff/{b}` | compare | exists |
| `POST .../versions/{n}/restore` | restore | exists |
| `authorKind`, `authorName` on version items | the author | [#3097] |

### 4.12 Preview

Row 3.13; decision 28; rows 3.23 (live preview, Should) and 3.30 (click-to-edit, Could) kept possible.

- **Frame.** A preview pane, beside the form or full width, loads the front end's page for the entry in an iframe. Breakpoints: phone (390 pixels), tablet (820) and full width. A toggle switches between the draft and the published page.
- **URL.** The admin mints a preview token for the entry and the form's locale, and the answer carries the type's preview URL with the token filled in (#3099). A type without a template has no preview button. The page reads the draft from the Delivery API with `Authorization: Preview <token>`; a front end that holds a preview key (row 3.13, "works with external frontends via preview key") reads the draft with it and ignores the token.
- **Token lifetime.** The admin asks for the default lifetime (ten minutes) and mints a new token when the pane reloads after it expired.
- **Populated relations.** Until #3095 (M7) is merged, a page read with a token gets no populated relation, so the preview is not the page; A-29 waits for it.
- **Published view.** The token's answer also carries `publishedUrl` (#3099, AQ15): the same template, without the token's parameter, filled from the published row of the locale that gave the draft, or null when that locale was never published. The toggle loads it, and is off while it is null.
- **Content Security Policy.** The admin page allows the preview origins of `config.json` in `frame-src` (2.5); the reference host builds them from its configuration (6.1). The front end must allow the admin's origin in its own `frame-ancestors`.

**`postMessage` contract, version 1 (proposed).** Decision 28 asks for it now, so that live preview is additive. Every message is a JSON object `{ "protocol": "fluentcms-preview", "version": 1, "type", ... }`. Each side checks `event.origin` against the origin it expects (the admin: the origin of the URL it loaded; the page: the admin origins it is configured with) and ignores a message of another protocol, a higher version or an unknown type. No message carries a token.

| Type | From | Members | Meaning | Tier |
| --- | --- | --- | --- | --- |
| `ready` | page | `url`, `entry` (`type`, `id`, `locale`) when known | The page has loaded and listens. | Must |
| `refresh` | admin | `entry` (`type`, `id`, `locale`, `version`) | The draft was saved; the page reads it again. | Must |
| `navigated` | page | `url` | The user followed a link inside the frame. | Must |
| `draft` | admin | `entry`, `document` | The unsaved form, for live preview; the page renders it without a read. | Should (3.23) |
| `focus` | admin | `path` | Highlight the element of a field. | Could (3.30) |
| `edit` | page | `type`, `id`, `path` | The user clicked an element marked with a field; the admin opens that field. | Could (3.30) |

A page that sends no `ready` within five seconds is shown as it is, with a note that saves reload the frame instead. The token in the page's URL can leak through the `Referer` of the page's own requests; the starters set `Referrer-Policy: no-referrer` on preview pages and may move the token into a cookie on first load (row 11.10, out of scope here).

| Endpoint | Use | Status |
| --- | --- | --- |
| `POST /content/{type}/{id}/preview-token` (`minutes`, `locale`) | the token | exists |
| `url` and `publishedUrl` in its answer, from the per-type template | the frame's URL, draft and published | [#3099] |
| populated relations under a token | the page as it will look | [#3095] (M7) |

### 4.13 Bulk actions

Rows 3.14, 8.8, 2.14.

- On a selection of the list (4.4): publish, unpublish, delete (to the trash) and the bulk actions of plugin admin halves for the type (5.1).
- A confirmation names the action, the type and the count; a selection by filter shows the filter.
- **Progress.** A bulk call answers only when it ends, so the admin sends the selected ids in chunks of 50 (`items: [{ "id" }]`; the size is *proposed*), one chunk at a time, and shows a progress bar and the per-entry results as they come: id, status, warnings, and for a failed entry the problem it got. Cancel stops after the current chunk. Each chunk carries its own `Idempotency-Key`.
- A selection by filter is sent as the filter with a `cap` of at most 1000, and its result is shown when it ends.

| Endpoint | Use | Status |
| --- | --- | --- |
| `POST /content/{type}/bulk` (`operation`, `items` by id, or `cap` with `filter[...]` in the query; `entries` holds the documents of a `create` only) | core's actions | exists |
| a plugin's own `MapManagement` routes | plugin actions | the plugin's |

### 4.14 Media library

Rows 3.15, 5.2 (drag-and-drop and paste upload), 5.3, 5.4, 5.7 (focal point set in the admin), 5.10 (usage, replace), 5.11.

The media library is a page of the media plugin's admin half (5.2), described here because category 3 lists it:

- Grid and list views of a folder, with a folder tree, search, filters by type and tag, and paging.
- Upload by button, by dropping files on the page and by pasting; each file shows its progress and its refusal (`FCMS1581` to `FCMS1584`) by name.
- A detail panel: title, alt and caption in the content locale, credit, tags, focal point set by clicking the image, private flag; usage as a list of entries with their labels (one list read per type, 2.7); replace; download; move; delete with the usage shown first.
- Folders: create, rename, move and delete an empty one.
- Presets of the site, for an administrator with `media.presets.manage`.

| Endpoint | Use | Status |
| --- | --- | --- |
| `GET /media` (`folder`, `subfolders`, `type`, `tag`, `search`, `page`, `pageSize`), `POST /media` | browse, upload | exist |
| `GET`, `PATCH`, `DELETE /media/{id}`; `POST /media/{id}/move`, `/replace`, `/download-url`; `GET /media/{id}/usage` | detail | exist |
| `/media/folders` (list, create), `PATCH` and `DELETE /media/folders/{id}`, `POST /media/folders/{id}/move` | folders | exist |
| `GET /media/presets`, `PUT` and `DELETE /media/presets/{name}` | presets | exist |
| `urls` (original and thumbnail) on Management reads | thumbnails | [#3100] |
| `media.*` grants of the built-in Editor and Author | the library for editors | [#3103] |
| bodies of the media routes in the OpenAPI document | contract tests (7) | [#3104] |

### 4.15 Users, roles, keys and administration

Rows 3.16, 4.2 to 4.9, 4.14 (activity queryable), 8.14 (plugin permissions in role editing), 8.10 and 6.6 (a visible job log), 10.14 (startup report visible in the admin), 8.15, and the members and groups pages of row 4.22, a Should taken early (AQ11).

- **Users.** List with search, detail with roles, deactivate and reactivate, sessions with revoke, external logins, two-factor reset (which needs the caller signed in with a second factor), forced password change; invitations with roles, pending list and revoke.
- **Roles.** List and editor: name, description, site, `requireTwoFactor`, and grants. The site's Public role is edited here too, which is the public access of row 4.8. A grant picks a permission or pattern from the catalogue, core's and the plugins' (row 8.14), and its constraints: sites, locales, fields and a registered row rule. The editor offers only what the caller holds, since a role must give nothing beyond the caller's grants; the server refuses such a role with a 403 `FCMS3103`. Other refusals are shown by their code: a built-in role changed or deleted (`FCMS3043`), a role still held (`FCMS3044`), the last active Super Admin (`FCMS3052`).
- **API keys.** Create with class, role, sites, types, expiry, rate limit and allowed addresses; the secret is shown once with a copy button and is not kept by the admin; rotate with overlap, expire, revoke.
- **Members and member groups.** Row 4.22 (Should): list with search and filters, detail with sessions, logins and groups, create, deactivate, delete; groups with their role, and group assignment. Its custom profile fields stay a Should without an issue (AQ11).
- **Plugins.** The plugins of `GET /plugins` with version, SDK range, contributions and enablement per site, with a switch for each site where the caller holds `plugins.manage`.
- **Jobs.** Jobs by status, type and queue, a job's attempts, the dead letters with retry, cancel of a pending job; the event dead letters with retry, remove and purge by handler. A running job's status refreshes as 2.7 says.
- **Diagnostics.** The startup report of `GET /system/diagnostics`: providers, plugins, pending migrations, schema drift and insecure defaults (row 10.14), and the detailed health of `GET /system/health`.
- **Activity.** Not a Must page (row 3.26 is Should); the MCP half filters it for agent writes (5.4).

| Endpoint | Use | Status |
| --- | --- | --- |
| `GET /users` (`search`), `GET` and `PATCH /users/{id}`, `POST /users/{id}/deactivate`, `/reactivate`, `PUT` and `DELETE /users/{id}/roles/{roleId}`, `GET /users/{id}/sessions`, `DELETE /users/{id}/sessions/{sessionId}`, `GET /users/{id}/logins`, `DELETE /users/{id}/logins/{provider}`, `POST /users/{id}/two-factor/reset` | users | exist |
| `GET` and `POST /invitations`, `DELETE /invitations/{id}` | invitations | exist |
| `GET` and `POST /roles`, `GET`, `PUT` and `DELETE /roles/{id}`, `GET /permissions` | roles | exist |
| `GET` and `POST /api-keys`, `GET` and `DELETE /api-keys/{id}`, `POST /api-keys/{id}/expire`, `/rotate`, `PUT /api-keys/{id}/allowed-addresses` | keys | exist |
| `/members`, `/members/{id}` and its sessions, logins and groups; `/member-groups` | members | exist |
| `GET /plugins`, `PUT /plugins/{name}/sites/{site}` | plugins | exist |
| `GET /jobs`, `GET /jobs/{id}`, `GET /jobs/dead-letter`, `POST /jobs/{id}/retry`, `/cancel`; `GET /events/dead-letter`, `POST /events/dead-letter/{id}/retry`, `DELETE /events/dead-letter/{id}`, `DELETE /events/dead-letter?handler=` | jobs and events | exist |
| `GET /system/diagnostics`, `GET /system/health` | diagnostics | exist |
| `GET /activity` | agent writes (5.4) | exists |

### 4.16 Settings, sites and locales

Rows 3.17, 8.7 (settings pages), 7.1, 7.10, 7.13 (settings per site with instance defaults).

- **Settings menu.** The areas the caller may manage in the site and for the instance, each under its plugin or core, from `GET /settings` (#3098). Without #3098 the admin cannot find the areas; A-34 waits for it.
- **Generated settings page.** From the JSON schema of the area's read: a form of the same field editors as 4.6 for its properties, with a switch between the site's values and the instance defaults (`/defaults`). The source of each value is shown: the site's own, inherited from the defaults or the code default. A secret shows as set or not set (`secretsSet`), and as inherited when it is the defaults' (`secretsInherited`); it is written only when the user types a new one. A reset returns the site to the defaults (#3098). A plugin may replace the generated page of its area (5.1).
- **Sites and locales.** The site's name and default locale, and its locales with position, enabled flag and fallbacks. Creating and deleting sites and their host names are the multi-site half's (5.6).

| Endpoint | Use | Status |
| --- | --- | --- |
| `GET` and `PUT /settings/{area}`, `GET` and `PUT /settings/{area}/defaults` | a settings page | exist |
| `GET /settings` | the settings menu | [#3098] |
| `DELETE /settings/{area}`, `DELETE /settings/{area}/defaults` | reset | [#3098] |
| `GET /sites`, `GET` and `PATCH /sites/{id}`, `/sites/{id}/locales` (`GET`, `POST`), `PATCH` and `DELETE /sites/{id}/locales/{code}` | sites and locales | exist |

### 4.17 Admin localization

Rows 3.18, 7.7, 7.9.

- **Interface culture.** Resource files (`.resx`) per assembly through `IStringLocalizer`; plugin halves bring their own. The culture is the user's preference (#3096), else the browser's when shipped, else the default of `config.json`. Changing it reloads the app, since Blazor WebAssembly sets the culture at start.
- **Two directions.** The interface's direction follows the interface culture; a field's direction follows the content locale, from its language tag: an explicit script decides (`Arab`, `Hebr`, `Thaa`, `Syrc`, `Nkoo`, `Adlm` and `Rohg` are right to left, any other script left to right), otherwise a table of right-to-left languages. `CultureInfo.TextInfo.IsRightToLeft` is not used, since the culture data of WebAssembly lacks most right-to-left locales and answers left to right for them, such as `ur`, `ps` and `yi`. An Arabic editor of an English entry gets a right-to-left interface with left-to-right fields, and the reverse (row 7.9).
- **Formats.** Dates, times and numbers in the user's culture and time zone, from its preferences (#3096) or, until then, the browser's, kept in the browser. The app loads all globalization data (`BlazorWebAssemblyLoadAllGlobalizationData`), so a format does not depend on the browser. The app needs it: with the culture data of the browser's language only, choosing the interface culture at start (`en` for a browser in `en-US`) stops the app with a blank page. It costs 186 KB in Brotli (#3143).
- **Content locales.** A locale switcher on lists and forms with the status of each locale (row 7.7), copy from another locale into a new one (a read in the source locale, then `PUT` with `If-Match: "0"`), and a side-by-side view of two locales for translators (two reads). An editor without `sites.manage` learns the site's locales from #3096; A-17 waits for it.
- **Shipped cultures.** English at the first release, with the resources ready for contributed translations, and pseudo-cultures (left-to-right and right-to-left) in tests (AQ14).

| Endpoint | Use | Status |
| --- | --- | --- |
| `GET /content/{type}?locale=all`, `GET /content/{type}/{id}?locale=all` | status of each locale | exist |
| `PUT /content/{type}/{id}` with `If-Match: "0"` | copy from locale | exists |
| the locales of each site in `GET /me/sites` | switcher for editors | [#3096] |
| `GET` and `PUT /me/preferences` | culture, time zone, formats per user | [#3096] |

**As built (A-17, [#3238]).** The content locale is in the address, `?locale=fr`; the shell holds it beside the site (`ShellSite`, sent as `X-Cms-Locale`). An address that names a locale sets it, on any page and on a cold load; one that names none keeps it, and the list and the form of a localized type replace such an address with one that names it, so that Back goes to the locale an address was in and a reload keeps it. A locale the site does not have enabled reads as the default. The routed page sets the locale of its address before it reads, and `GET /me/access` and `GET /schema` are read again in it, since the server narrows both by the request's locale: a translator whose grants are limited to `fr` sees its types and may change entries in `fr`, and reads the others where its grants let it, with the form read-only there. `/me/access` is also read once per site in each enabled locale when a view first needs it, so that a locale the user may not read gets no badge rather than look untranslated, and so that where the content locale shows no type the user may read, the sidebar names the locales that show some. A site switch resets the locale to the new site's default in the same step, which a route it keeps then names. The switcher is a link per enabled locale of `GET /me/sites`, in the site's order, on the list and the form of a localized type, with the current locale as text, `hreflang` and the language's own name; a form with unsaved changes asks before it is left, as for any route change, and a switch is announced and moves focus to the heading. `ContentForm` edits the locale it is given; only the routed page's form, which owns the address, offers the switcher and side by side, so a form in a side panel does not move the shell's locale. The form reads the entry in its locale and `GET /content/{type}/{id}?locale=all` for the status of each locale, shown as a badge per readable locale with an empty one where the entry has no translation. A locale without a translation opens a form of the defaults of the fields the caller may change, with the fields the locales share as the entry has them and read-only, since the server keeps them when it adds the locale; "Copy from" another locale the entry has fills each field the caller may change from a read in that locale, as an unsaved change, after asking when the user typed values; the first save sends `PUT` with `If-Match: "0"`, and a publish saves first. A localized singleton that has no entry in the content locale is found in another and opened as a new locale of it, since a second create is refused. The status controls of A-16 act on the read, so on the form's locale. Side by side: a second locale, chosen beside the form and read on its own, shows each field's editor read-only beside the field, labelled with that locale, in its language and direction. A field's `lang` and `dir` are those of the form's locale, the site's default included; the script is the subtag after the language and its extended language subtags only. Dates, times and numbers are A-14's, in the user's culture and time zone from `GET /me/preferences`.

### 4.18 Accessibility baseline

Row 3.19, WCAG 2.2 AA as the target.

- **Keyboard.** Every core flow (find, edit, save, publish, preview, bulk action, upload, pick) works by keyboard alone, with a skip link, a logical tab order and no keyboard trap.
- **Focus.** A route change moves focus to the page's heading; a dialog takes focus and returns it to the control that opened it; after a save, focus stays where it was and the result is announced.
- **ARIA on generated controls.** Each generated input has its label from `display.label`, `aria-describedby` for help and errors, `aria-required` and `aria-invalid`; groups and tabs use the roles of the wrapped set; lists announce sorting and selection.
- **Live regions.** Save, publish, bulk progress and errors are announced through one polite region and one assertive region of the shell.
- **WCAG 2.2.** Visible focus not hidden by sticky headers (2.4.11), dragging with an alternative (2.5.7), targets of 24 pixels (2.5.8), contrast in both themes, reduced motion respected.

No endpoint.

## 5. Plugins, admin halves

### 5.1 The contract of an admin half

Rows 8.1, 8.5, 8.7 to 8.9, 8.14, 8.15; decisions 12 and 29. *Proposed* shape:

```csharp
[assembly: FluentCmsAdminPlugin(typeof(ReviewsAdminPlugin))]

[FluentCmsAdminPlugin("reviews", Version = "1.0.0", AdminRange = "[1.0.0,2.0.0)")]
public sealed class ReviewsAdminPlugin : IAdminPlugin
{
    // Runs once, when the assembly is first loaded (2.6).
    public void Configure(IAdminPluginBuilder admin)
    {
        admin.Services.AddScoped<ReviewsApi>();
    }
}

[AdminPage("/reviews", Permission = "reviews.notes.manage")]
[AdminMenuItem("Reviews.Menu", Section = AdminSection.Content, Icon = "comment")]
public partial class ReviewsPage : ComponentBase { }

[FieldEditor("rating")]
public partial class RatingEditor : FieldEditorBase<int?> { }

[DashboardWidget("Reviews.Widget.Title", Size = WidgetSize.Small, Permission = "reviews.notes.manage")]
public partial class LatestReviewsWidget : ComponentBase { }
```

- **Package.** A Razor Class Library `{server package}.Admin` that references `FluentCMS.Admin.Abstractions` and `FluentCMS.Admin.Components` only among FluentCMS packages (3.3). It never references its server half: the records it reads are its own, and the OpenAPI document of the reference host is the contract between the two, which a test checks (7).
- **Manifest.** The name is the server plugin's name, so the shell matches it with `GET /plugins`: enabled in the site, version, contributions. `AdminRange` is the range of `FluentCMS.Admin.Abstractions` it was built for; a half outside the running range is not loaded, and the shell lists it on an error page instead of failing.
- **Contributions** (2.6): pages with a route under `/{plugin}/`, menu items in a section (content, media, administration, settings), settings pages that replace the generated page of an area the plugin registers, field editors and list cells for the plugin's field types, with the filter operators of each (4.4), entry actions and bulk actions targeted by content type, with a confirmation text and progress reported through the bulk service of 4.13, field actions on fields of given field types (AI assist, 5.5), dashboard widgets with a size hint (small, medium, wide) and a visibility by permission or role (row 8.9), and services other halves may ask for, such as the media picker.
- **What the admin gives a component.** The API client of 2.7, already set to the site and locale; `IAdminContext` with the user, the site, the content locale and the caller's access; `IAdminAccess.Allows(permission)` (#3096); notifications, dialogs, confirmation, navigation and the bulk progress service; the pickers (`IRelationPicker`, and `IMediaPicker`, which the media half implements); `IStringLocalizer<T>` over the plugin's own resources; the wrapped components. The interfaces are in `FluentCMS.Admin.Abstractions`, so a half names a service of another half without referencing it, and asking for it loads the half that provides it.
- **Services of a half.** The app's container is built before a lazy assembly loads, so a half adds nothing to it. `Configure` runs once, when the assembly first loads, into a provider of the plugin that falls back to the admin's services; the half's components reach it through `IAdminPluginServices`. Decided with AQ4.
- **Field editors.** A field editor derives from `FieldEditorBase<TValue>`: it gets the field's description, value, errors, read-only, locale and direction (4.6), raises `ValueChanged`, and meets the accessibility rules of 4.18. A list cell derives from `ListCellBase<TValue>`.
- **Gating.** The shell shows a contribution only while the plugin is enabled in the site and the caller holds its permission; a disabled plugin's half is not loaded there (row 8.15). The server still checks every call.
- **No JavaScript contract.** A component may use JS interop internally, but the admin offers no slot for script, and a plugin cannot extend the rich text editor (decisions 12 and 27).
- **Template.** `dotnet new fluentcms-plugin` gains the admin half: a page, a menu item, a settings page for its area and a widget, with a test project (6.2).

**As built (A-18, [#3287]).** The contract is in `FluentCMS.Admin.Abstractions`, in three namespaces. `FluentCMS.Admin.Plugins` holds `FluentCmsAdminPluginAttribute`, `IAdminPlugin`, `IAdminPluginBuilder` (`Name`, `Services`, and `Filters`, the `ListFilterRegistry` a half names the operators of its field types on), `IAdminPluginServices` (`ProviderOf(Type)` for the provider of a loaded half, and `FindAsync<TService>()`, which loads the half that provides a service and answers null when no enabled half does), the contribution attributes, `AdminSection` and `WidgetSize`, and the manifest. `FluentCMS.Admin.Actions` holds `EntryActionBase`, `BulkActionBase` and `FieldActionBase`, plain classes the provider of the half creates: each has a `Label`, an optional icon name and a `ConfirmationText`, reads its content types or field types from its attribute, and runs with a context; a field action answers a `FieldActionResult` that leaves the draft or replaces the field's value. A bulk action reports to an `IBulkProgress`, which `IBulkProgressService.Start` also gives a page of a half; A5 implements both. `FluentCMS.Admin.Pickers` holds `IRelationPicker` and `IMediaPicker`, each one `PickAsync` that answers the choice or null; the shell implements the first with the issue that needs it, and the media half the second (A-20). A page carries its route twice, `@page` for the router and `[AdminPage]` for the manifest; a menu item is declared on its page and links to it, and takes the page's permission. The targets of an action and its permission are on the attribute, since the shell needs them before the half loads. The manifest is the record `AdminPluginManifest`: `manifestVersion` (1), `name`, `version`, `adminRange`, `assembly`, and one list per kind of contribution (`pages`, `menuItems`, `settingsPages`, `fieldEditors`, `listCells`, `entryActions`, `bulkActions`, `fieldActions`, `widgets`, `services`), each entry with the full name of its class as `type`. `AdminPluginManifestJson` reads and writes it with source-generated metadata, for one manifest and for a list of them, in camel case, with sections and sizes by name in lower case; a reader skips members it does not know and reads a section or a size it does not know as a value outside its enumeration, so a manifest of a later version does not stop the read of a gathered list, and `AdminPluginManifest.IsValid` tells such a manifest, and one with a null in a list, apart for the reader to leave out and report; any other null where the manifest takes none is refused; and a hand-written file may hold comments and trailing commas. `AdminPluginManifestCheck` is the test helper: `FromAssembly` reads the attributes, `Differences` and `Compare` name each difference in a sentence, `Verify` and `VerifyFile` throw with them, and a declaration the admin could not use is named too: an `[AdminPage]` without its `@page` or with a route that is not `/{plugin}` or below it, a plugin named after a reserved first segment (3.2), an `AdminRange` that is missing or no range, an action that does not derive from its base class, a contribution declared twice, and in the file a version of 0, an unknown section or size and a null in a list. `AdminVersionRange` reads the range of `AdminRange` in NuGet's notation and tells whether it holds a version. Only the three kinds of action derive from `AdminActionBase`. Icons are named in camel case after the members of `CmsIconName`, which gains `Comment` and `Plugin`; `CmsIconNames.Find` of `FluentCMS.Admin.Components` answers the icon of a name whatever its case, and `Plugin` for none and for one it does not know. The sample half of the tests, `admin/tests/FluentCMS.Admin.Plugins.Sample`, declares every contribution, and `FluentCMS.Admin.Plugins.Careless` beside it what the helper must name. A menu item's label is a key of the half's resources, which the shell cannot read before the half loads; A-19 added the texts per culture to the manifest for the sidebar (2.6).

### 5.2 Media

`FluentCMS.Plugins.Media.Admin`. Rows 3.10, 3.15, 5.2 to 5.4, 5.7, 5.10 to 5.12.

| Contribution | What | Endpoints |
| --- | --- | --- |
| Page `/media` and menu item | the media library (4.14) | 4.14 |
| Field editor and list cell for `media` | a thumbnail with pick, upload, remove; alt text and focal point editable from the picker | `GET /media/{id}`, `PATCH /media/{id}` (`alt`, `focalPoint`) |
| Service `IMediaPicker` | a dialog over the library with folders, search, upload inline and multi-select; used by the field editor, rich text embeds (4.7) and block fields | `GET /media`, `GET /media/folders`, `POST /media` |
| Settings page for `media` | the generated page, with the SVG and type lists explained | `/settings/media` |
| Page `/media/presets` | presets (4.14) | `/media/presets` |

Waits for #3100 (URLs) and #3103 (grants of the built-in roles); its contract tests need #3104.

### 5.3 Webhooks

`FluentCMS.Plugins.Webhooks.Admin`. Row 3.17.

| Contribution | What | Endpoints |
| --- | --- | --- |
| Page `/webhooks` and menu item | list, create, edit, enable and disable, delete | `GET` and `POST /webhooks`, `GET`, `PUT` and `DELETE /webhooks/{id}` |
| Page `/webhooks/{id}` | the delivery log with status, duration, body and answer; redeliver; ping | `GET /webhooks/{id}/deliveries`, `POST /webhooks/{id}/deliveries/{deliveryId}/redeliver`, `POST /webhooks/{id}/ping` |

The event names a webhook may subscribe to are fixed in the plugin and appear in no response. The admin half carries the same list, since it is released with the server half (1.4). Contract tests need #3104.

### 5.4 MCP

`FluentCMS.Plugins.Mcp.Admin`. Rows 9.13, 9.10, 9.11, 3.17.

| Contribution | What | Endpoints | Status |
| --- | --- | --- | --- |
| Page `/mcp/clients` | clients with status, role and last activity; approve with a role, reject, revoke | `GET /mcp/clients`, `POST /mcp/clients/{id}/approve`, `/reject`, `DELETE /mcp/clients/{id}` | exist |
| Register a client, change an approved client's role | the rest of row 9.13 | `POST /mcp/clients`, `PUT /mcp/clients/{id}/role` | [#3102] |
| Link "agent writes" | the activity log with one filter (row 9.13) | `GET /activity?origin=mcp` | exists |
| Page `/mcp/instructions` | guidelines of the site and instructions per type | `GET`, `PUT` and `DELETE /mcp/instructions`, `PUT` and `DELETE /mcp/instructions/{type}` | exist |
| Page `/mcp/prompts` | stored prompts | `GET` and `POST /mcp/prompts`, `GET`, `PUT` and `DELETE /mcp/prompts/{id}` | exist |
| Settings page for `mcp` | registration policy, generated | `/settings/mcp` | exists |

Service accounts are clients from configuration and are neither listed nor changed (#3102); the page says so and points to management keys, which `/mcp` accepts.

### 5.5 AI assist

`FluentCMS.Plugins.AiAssist.Admin`. Rows 9.14, 9.15.

| Contribution | What | Endpoints | Status |
| --- | --- | --- | --- |
| Field action on text, long text and rich text fields | a menu of the actions (rewrite, shorten, expand, fix grammar, title, summary, SEO, translate) on the fields the site opted in; the suggestion streams into a panel and is inserted into the draft only when the user accepts it, never saved or published by the action | `POST /ai/actions/{action}` (JSON or `text/event-stream`) | exists |
| Field action on media fields | alt text for an image | `POST /ai/actions/altText` with `options.mediaId` | exists |
| Which fields take actions, and whether AI assist is on | so that the actions appear only where they work | `GET /ai/fields` (name open) | [#3101] |
| Page `/ai/usage` | usage per user and action | `GET /ai/usage` (`ai.usage.read`) | exists |
| Settings page for `ai` | provider, model, endpoint, key as a secret, opted-in fields | `/settings/ai` | exists |

Waits for #3101 and #3103 (`ai.use` for the built-in roles); its contract tests need #3104.

### 5.6 Multi-site

`FluentCMS.Plugins.MultiSite.Admin`. Rows 7.10, 7.11, 3.17.

| Contribution | What | Endpoints |
| --- | --- | --- |
| Pages under `/multi-site/sites` and a menu item beside core's sites | create a site (name, slug, default locale), delete one after a confirmation that names it | `POST /sites`, `DELETE /sites/{id}` |
| Page `/multi-site/sites/{id}/hosts` | host names with the primary first, add, make primary, remove | `GET` and `POST /sites/{id}/hosts`, `PATCH` and `DELETE /sites/{id}/hosts/{hostname}` |

All need `sites.manage` for the instance. Contract tests need #3104 for three of the routes.

### 5.7 Search and GraphQL

`FluentCMS.Plugins.Search.Admin` is small: a "Rebuild index" action on the plugin's entry of the plugins page, for the site or one type, which answers a job id that the jobs page follows (`POST /search/rebuild`, `search.manage`). The list's search box (4.4) needs nothing from it, since `searchable` in the schema says where `q=` works. GraphQL has no admin half (2.8).

## 6. Distributions

Decision 5 names three. All three are the same app built from the same libraries; they differ in who builds it, which admin halves it holds, and where it is served from.

| Distribution | For | Built by | Admin halves | Served from | Client and CORS |
| --- | --- | --- | --- | --- | --- |
| Reference image | evaluation and first-party setups | the `Publish` workflow, with the host | first-party | the API host at `/admin` | registered by the host; same origin, no CORS |
| `fluentcms-admin` template | teams with their own plugins, or hosting the admin apart | the team | first-party and the team's | the team's choice | the API host's configuration (6.4) |
| Prebuilt static build | first-party setups on a static host or a CDN | the `Publish` workflow | first-party | any static host | the API host's configuration (6.4) |

### 6.1 Reference image: admin and API built together

- `src/FluentCMS.Host` references `FluentCMS.Admin.App`, which references the first-party admin halves, and `FluentCMS.Admin.Hosting`. `dotnet publish` of the host compiles the WebAssembly app with them and publishes its static web assets at the root of the host's `wwwroot`, which `MapFluentCmsAdmin` maps under `/admin`, so one build gives both, and the Dockerfile keeps its stages. No Node is needed in the image's build (AQ7).
- `ReferenceHost.Build` calls `cms.AddAdmin()` and `app.MapFluentCmsAdmin()` with the path `/admin`, which Appendix B of the backend reserves, under `FluentCms:ReverseProxy:PathBase` when set.
- `MapFluentCmsAdmin` serves `index.html` with its `<base href>` set to the path base and `/admin/`, the framework files with long caching, a fallback to `index.html` for client routes, and `config.json` built from `FluentCms:Admin` (`ApiBaseUrl` defaults to the public origin and path base, or the request's in Development, `ClientId` to `cms.Clients.AdminClientId`, `Branding`, `Cultures`, `PreviewOrigins`). It sends the admin page's headers: `Content-Security-Policy` with `script-src 'self' 'wasm-unsafe-eval'`, the `style-src` the component library needs (A-10 measures it and keeps it as narrow as the library allows), `connect-src` of the API, `img-src` of the API and `data:` and `blob:`, `frame-src` of the preview origins, `frame-ancestors 'none'`, and the FluentCMS security headers.
- `AddAdmin` registers `fluentcms-admin` as a public client of the admin pool when `FluentCms:Clients:Applications:fluentcms-admin` is not configured, with the redirect URI `{origin}{path base}/admin/authentication/login-callback`, the post-logout URI `{origin}{path base}/admin/`, and the scopes of 2.4; the origin is `FluentCms:PublicOrigin`, which the admin requires in every environment but Development (`FCMS1101` at start), and in Development without one the addresses the host listens on, since no request exists when clients are registered; `config.json` there follows the request. A configured client wins, as any client of `cms.Clients.Applications` does.
- `FluentCms:Admin:Enabled` set to false turns the admin off in the image, for a team that hosts its own. It is false by default until the admin is released (A-47), which makes it true (A-10, #3148).

### 6.2 Templates

- `dotnet new fluentcms-admin -n <name>` writes a WebAssembly app project like `FluentCMS.Admin.App`: `Program.cs` with `AddFluentCmsAdmin` and the first-party admin halves as package references, `wwwroot/config.json` pointing at a local API host, the `BlazorWebAssemblyLazyLoad` items, and a README on adding a team's own admin halves and hosting the result: as static files (6.3), or from the team's API host with `FluentCMS.Admin.Hosting` serving that project instead of the prebuilt build.
- `dotnet new fluentcms` (row 11.1, "API host with admin") gains `--admin` (default on), which adds `FluentCMS.Admin.Hosting`, `cms.AddAdmin()` and `app.MapFluentCmsAdmin()` with the prebuilt first-party build of the package (AQ8).
- `dotnet new fluentcms-plugin` gains the admin half of 5.1 and its tests.
- The `Templates` job of CI creates each with the admin and builds it, and starts the SQLite host and reads `/admin/config.json` and `/admin/`.

### 6.3 Prebuilt static build

- The published output of `FluentCMS.Admin.App` with the first-party admin halves, trimmed and compressed, with a sample `config.json`, attached to each GitHub release as `fluentcms-admin-{version}.zip` by the `Publish` workflow (B-108), and carried in `FluentCMS.Admin.Hosting` (AQ8).
- It is served from any static host or CDN. The operator edits `config.json` and the `<base href>` of `index.html` for the path it serves from, falls back to `index.html` for unknown paths, serves `config.json` and `index.html` without caching, and sends the headers of 6.1. `docs/admin-hosting.md` gives recipes for nginx, Caddy, Azure Static Web Apps, Amazon S3 with CloudFront and Cloudflare Pages.

### 6.4 An admin on another origin

An admin served from another origin than the API needs two settings on the API host, both existing:

- `FluentCms:Security:Cors:Management:AllowedOrigins` lists the admin's origin. It covers `/api/management/v1`, `/connect` and `/.well-known` with the methods, request headers (`Authorization`, `X-Cms-Site`, `X-Cms-Locale`, `If-Match`, `If-None-Match`, `Content-Type`, `Idempotency-Key`) and exposed headers (`ETag`, `Location`, `Retry-After`, `RateLimit-*`, `Idempotent-Replayed`) that the admin uses (audit of B-105).
- `FluentCms:Clients:Applications:fluentcms-admin` registers the client with the admin's `RedirectUris`, `PostLogoutRedirectUris` and `Scopes`; it is public, uses PKCE and gets no consent page, and its tokens run with `Origin.Admin`.

Signed asset URLs (#3100) are relative to the API's origin, so the admin resolves them against `apiBaseUrl`, and the page's `img-src` must allow that origin.

For the local loop (row 11.4, A-10): `dotnet watch` on the app project, with `config.json` pointing at a reference host run in Development, whose `appsettings.Development.json` lists the app's development origin and redirect URI; or `dotnet watch` on the reference host, which serves the referenced app with hot reload.

## 7. Testing

| Kind | Project | What it proves | Runs in |
| --- | --- | --- | --- |
| Component tests | `admin/tests/FluentCMS.Admin.Tests`, `FluentCMS.Plugins.*.Admin.Tests` | each field editor, list cell and form behaviour (conditions, defaults, read-only, error mapping by path, dirty guard), the shell's gating by access and enablement, the bulk chunking, the `postMessage` handling, the token refresh rules with a fake token endpoint; on bUnit (AQ9) | Feature CI and full CI |
| API tests | `admin/tests/FluentCMS.Admin.ApiTests` | the admin's real API client and services against `FluentCmsTestHost` with the Blog sample and every first-party plugin, as Super Admin and as the built-in Editor: every endpoint section 4 marks "exists" answers the shape the admin reads, so an API change that breaks the admin fails here; each gap gets its test with the issue that closes it | full CI |
| Contract against OpenAPI | in the API tests | every route and record the admin and the first-party halves read appears in the reference host's OpenAPI document with a matching schema; needs #3104 for plugin routes | full CI |
| Filter grammar | in the component tests | the admin's query writer writes `clients/filter-cases.json` as the C# and TypeScript clients do | Feature CI |
| Rich text round trip | API and end-to-end tests | a set of documents made by the editor, with every node, mark and inline block, saves and reads back unchanged | full CI |
| End-to-end | `admin/tests/FluentCMS.Admin.EndToEnd` | the editor's daily loop on the reference host on SQLite with the Blog sample in Chromium: sign-in through the hosted page with PKCE, two tabs refreshing at once, find, edit, save, conflict, publish, schedule, preview against a stub front end page that speaks the `postMessage` contract, bulk publish, upload and pick an image, expiry with the draft kept; on Playwright (AQ9) | full CI, and nightly on Firefox and WebKit |
| Accessibility | in the end-to-end tests | axe-core checks of each core page in both themes and in a right-to-left pseudo-culture, and keyboard-only runs of the daily loop (AQ9); a manual screen reader pass (NVDA with Firefox, VoiceOver with Safari) before A7 ends | full CI; manual in A-42 |
| Localization | in the component tests | every key of the neutral resources is in each shipped culture; rendered under a pseudo-culture, no visible text of a core page is left untranslated, data aside | Feature CI |
| Architecture | `tests/FluentCMS.Architecture.Tests` | the rules of 3.3 | Feature CI and full CI |
| Size budget | the `Pack` job | the compressed size of the first download, without lazy assemblies, stays within a budget set in A-06: 3,500,000 B | full CI |
| Templates and image | the `Templates` and `Compose` jobs | 6.1 and 6.2 | full CI |

**Size budget as set (A-06, #3144).** The first download is the Brotli size of every file of the published `wwwroot` (`_framework`, `_content` and the app's own files), less the lazy assemblies and the satellite resources that the boot configuration in `_framework/dotnet.js` names; the `.br` the publish writes is used, and a file without one is compressed. `eng/check-admin-size.cs` measures it from the trimmed publish of the `Pack` job, prints the files it left out and the largest files and fails above the budget, and the job checks that a budget below the measurement fails it. The total is an upper bound: every other published file counts, about 21 KB of which the start page fetches later or not at all (the library's lazy modules, `reboot.css`, the second logo), which is the gap to a network measurement of a first visit. The `_content` files of a lazily loaded package, such as the rich text editor's bundle, count too, since nothing the script can read tells them apart; only their assemblies are left out. Measured from a clean publish of `62b2966f`, which is `feature/a0` at `2df6fa65` (after #3183) merged with the changes of #3144, with all globalization data (`icudt.dat`, 330 KB, which the admin needs, 4.17) and Fluent UI Blazor (its own assemblies and `_content`, about 318 KB, and 692 KB with the framework assemblies it roots, the growth of #3172): 3,226,834 B. The budget is 3,500,000 B, 273,166 B (7.8 %) of headroom: room for the areas of a milestone, whose assemblies add tens of KB each, and not for a second component library or a framework reference by mistake, either of which adds several hundred KB. A milestone that needs more raises the budget here and in `ci.yml` in its pull request, with the new measurement.

Tests use xUnit v3 and Shouldly, as the backend's do. The API tests need no browser and no Docker. The end-to-end tests install their browsers in CI; locally they are skipped with a message when the browsers are missing, as the media storage tests are without Docker.

## 8. Feature inventory

Every Must row of category 3, every admin-only row of the other categories, and the admin part of the other Must rows that have one, with its area and milestone. A row the admin only relies on, such as the query model (2.4, 2.5, 2.19) or the authorization server (4.1), is named by the area that calls it, not here.

### 8.1 Category 3, Admin UI

| Row | Feature | Area | Milestone | Needs |
| --- | --- | --- | --- | --- |
| 3.1 | Admin shell | 4.1, 2.3 | A1 | #3096 |
| 3.2 | Runtime configuration | 2.5, 4.2 | A0 | |
| 3.3 | Login and session | 2.4, 4.3 | A1 | |
| 3.4 | Generated list views | 4.4 | A2 | #3094 (badge) |
| 3.5 | Generated edit forms | 4.5 | A2 | #3096 |
| 3.6 | Field editors for every core field type | 4.6 | A2 | |
| 3.7 | Rich text editor | 4.7 | A4 | #3100 (embeds) |
| 3.8 | Component and block editors | 4.8 | A4 | |
| 3.9 | Relation picker | 4.9 | A2 | |
| 3.10 | Media picker | 5.2 | A3 | #3100, #3103 |
| 3.11 | Draft, publish and status controls | 4.10 | A2 (controls), A5 (schedule) | #3094 |
| 3.12 | Version history panel | 4.11 | A5 | #3097 |
| 3.13 | Preview | 4.12 | A5 | #3095, #3099 |
| 3.14 | Bulk actions | 4.13 | A5 | |
| 3.15 | Media library | 4.14, 5.2 | A3 | #3100, #3103 |
| 3.16 | Users, roles and API keys | 4.15 | A6 | |
| 3.17 | Settings | 4.16, 5.3, 5.4, 5.6 | A6 | #3098, #3102 |
| 3.18 | Admin localization | 4.17, 2.3 | A0 (groundwork), A7 | #3096 |
| 3.19 | Accessibility baseline | 4.18, 2.3 | A0 (groundwork), A7 | |

Should, kept possible: 3.20 autosave (the form's draft store of 2.4 and `If-Match` already carry it), 3.21 locking indicator (SignalR), 3.22 saved views (columns are already per user), 3.23 live preview (`draft` message of 4.12), 3.24 dashboard (the home page of A-22 holds the widgets), 3.25 command palette, 3.26 activity log viewer, 3.27 trash (the trash routes of content exist; media has none, since its trash is row 5.19, a Could). Could and Won't rows are omitted.

### 8.2 Admin-only rows of other categories

| Row | Feature | Area | Milestone | Needs |
| --- | --- | --- | --- | --- |
| 7.7 | Locale-aware admin | 4.4, 4.10, 4.17 | A2 | #3096 |
| 7.9 | RTL and locale-aware fields | 4.6, 4.17 | A2 | #3096 |
| 8.7 | Admin pages, menu items and settings pages | 5.1, 4.16 | A3 (contract), A6 (settings pages) | #3098 |
| 8.8 | Content list and entry actions | 5.1, 4.13 | A3 (contract), A5 (bulk) | |
| 8.9 | Dashboard widgets | 5.1 (the home page of A-22) | A3 | #3096 (by permission) |
| 9.13 | MCP client management in admin | 5.4 | A6 | #3102 |
| 9.14 | AI assist in the editor | 5.5 | A6 | #3101, #3103 |

### 8.3 Admin parts of other Must rows

| Row | Feature | Admin part | Area | Milestone |
| --- | --- | --- | --- | --- |
| 1.3 | Core scalar fields | an editor and a list cell for each | 4.6 | A2 |
| 1.4 | Media and relation fields | the relation picker, the media field editor | 4.9, 5.2 | A2, A3 |
| 1.5 | Components | the component editor | 4.8 | A4 |
| 1.6 | Typed block field | the block editor | 4.8 | A4 |
| 1.8 | Conditional fields | enforced in the form | 4.5 | A2 |
| 1.9 | Structured rich text | the rich text editor on the stored JSON | 4.7 | A4 |
| 1.10 | Field metadata and admin hints | the form's layout | 4.5 | A2 |
| 2.8 | Error model | problems by code, errors on their fields | 2.7, 4.5 | A0, A2 |
| 2.14 | Bulk operations | bulk actions in chunks | 4.13 | A5 |
| 2.15 | Optimistic concurrency | `If-Match` on every save | 2.7 | A0 |
| 2.23 | Full-text search parameter | the search box of lists | 4.4 | A2 |
| 4.2 | Admin users | users, invitations, sessions | 4.3, 4.15 | A1, A6 |
| 4.3 to 4.8 | Roles, type, field, row, locale and site permissions, public access | the role editor | 4.15 | A6 |
| 4.5 | Field-level permissions | hidden fields absent from forms | 4.5 | A2 |
| 4.9 | API keys | keys | 4.15 | A6 |
| 4.10 | Member authentication | members and groups, the pages of row 4.22 (Should, taken early: AQ11) | 4.15 | A6 |
| 4.12 | Permission checks in one place | the admin hides what the user cannot do | 4.1 | A1 |
| 4.15 | Admin SSO | buttons on the hosted login | 4.3 | A1 |
| 4.16 | Two-factor authentication | enrolment in the account page | 4.3 | A1 |
| 5.2 | Upload | drag-and-drop and paste | 4.14 | A3 |
| 5.3 | Asset metadata | the detail panel, alt and caption per locale | 4.14 | A3 |
| 5.4 | Folders | the folder tree and its actions | 4.14 | A3 |
| 5.7 | Focal point and crop presets | set in the admin | 4.14, 5.2 | A3 |
| 5.10 | Usage tracking | usage and replace | 4.14 | A3 |
| 5.11 | Private assets | the private flag, download by signed URL | 4.14 | A3 |
| 5.12 | Media in rich text and blocks | the same picker | 4.7, 4.8 | A4 |
| 6.1, 6.2 | Draft and published states; publish, unpublish, discard | controls | 4.10 | A2 |
| 6.3, 6.4 | Version on every write; diff and restore | the panel | 4.11 | A5 |
| 6.5, 7.6 | Scheduled publish and unpublish, per locale | schedule dialog and badges | 4.4, 4.10 | A5 |
| 6.6, 8.10 | Job runner and plugin jobs, a visible job log | the jobs page | 4.15 | A6 |
| 6.7 | Publish-time validation | warnings and errors | 4.10 | A2 |
| 6.8 | Optimistic concurrency | the conflict dialog | 4.5 | A2 |
| 7.1 | Locales per site | the site's locales | 4.16 | A6 |
| 7.10 | Multi-site model | sites and host names | 4.16, 5.6 | A6 |
| 7.11 | Site context in APIs | the shell's site as `X-Cms-Site` on every call | 2.7, 4.1 | A0 |
| 7.13 | Site switcher and per-site admin | switcher, per-site settings | 4.1, 4.16 | A1, A6 |
| 8.1 | Plugin package model | the admin half | 5.1 | A3 |
| 8.5 | Custom field types | editor and list cell | 5.1 | A3 |
| 8.14 | Plugin permissions in RBAC | in the role editor | 4.15 | A6 |
| 8.15 | Enable and disable per site | gating and the plugins page | 2.6, 4.15 | A3, A6 |
| 8.17 | Plugin template | the admin half of `fluentcms-plugin` | 5.1, 6.2 | A3 |
| 9.10 | Per-type agent instructions | instructions of the site and per type | 5.4 | A6 |
| 9.11 | Prompts | stored prompts | 5.4 | A6 |
| 9.15 | Provider-agnostic AI layer | provider, model and key per site; usage per user and action | 5.5 | A6 |
| 10.2 | Reference host image | admin and API built together | 6.1 | A1 |
| 10.14 | Startup diagnostics | visible in the admin | 4.15 | A6 |
| 10.15 | Supported platforms | the admin on .NET 10 LTS | 1.3 | A0 |
| 11.1 | Project templates | `fluentcms` with admin, `fluentcms-admin` | 6.2 | A7 |
| 11.4 | Fast local loop | `dotnet watch` for the admin | 6.4 | A1 |
| 11.11 | Semantic versioning and compatibility policy | the admin's contract packages under the policy | 2.8, 3.1 | A7 |

## 9. Milestones

The admin's milestones follow the editor's daily loop of category 3, "find an entry, edit it with confidence, preview it, publish it", then administration and distribution. They start once this document is approved; nothing in them blocks M7.

| Milestone | Goal | Exit criteria |
| --- | --- | --- |
| A0 Admin foundation | The admin projects build and test in CI; nothing is usable yet. | The projects of 2.8 build with `-warnaserror` in CI; the app starts from `config.json` and shows a page of the wrapped components in both themes and in a right-to-left pseudo-culture; the API client reads `/me` from the test host; the architecture tests of 3.3 pass. |
| A1 Shell and sign-in | An editor signs in to the admin of the reference image. | From the reference image on SQLite, an editor signs in at `/admin` through the hosted login with PKCE, sees the sidebar of the types it may read in each of its sites, switches site, stays signed in across a token refresh in each of two tabs, enrols in two-factor, and signs out; a session that ends keeps the unsaved form. |
| A2 Lists and forms | The daily loop without rich text. | On the Blog sample an editor finds an article by search and filter, edits it with the core field editors and a relation, sees errors from the API on their fields, saves, publishes, unpublishes and discards in two locales, copies a locale, and gets the conflict dialog when another tab saved first. |
| A3 Plugin contract and media | Plugins extend the admin, proven by the media plugin. | The media half loads only on first use; an editor uploads, picks and edits the image of `Article.Hero` and manages the library; a plugin from the template shows a page, a menu item, a settings page and a widget, and none of them in a site where it is disabled. |
| A4 Rich text and blocks | The editor is complete. | An editor writes an article body with headings, lists, links to entries, an embedded image and an inline block, and builds content from blocks and components to depth 3, all saved and read back unchanged. |
| A5 Publishing, versions, preview and bulk | The editor works with confidence. | An editor schedules a locale and sees and cancels it, compares two versions and restores one, previews the draft in a front end's page at three widths with relations populated, and publishes 200 selected entries with progress and per-entry results. |
| A6 Administration and plugin halves | Nothing a Must row needs takes the CLI or a direct API call. | An administrator manages users, roles, keys, members, settings, locales, sites and host names, webhooks, MCP clients, plugins and jobs, and reads the startup report, from the admin alone; an editor runs AI actions on the opted-in fields. |
| A7 Distributions and admin preview | The admin ships. | The admin is in its three distributions with a preview of the packages; the end-to-end and accessibility checks pass on the image; the admin runs in English and a right-to-left pseudo-culture with each user's formats; the screen reader pass is done. |

A1 to A2 are the critical path. The gaps of 1.4 run before the admin issue that needs them (AQ13): all were merged into `main` on 13 and 15 Nov 2026, before A0 started, #3096 among them, which A-08 and three issues of A2 wait for.

## 10. Proposed issues

Grouped by milestone, as the backend's section 9: each becomes one GitHub issue with the Feature template, labelled `type:feature` and `area:admin` (AQ13), with the milestone. One tracking issue per milestone holds the checklist. "Section" is this document's; "Rows" names rows of the feature specification and, with a `d`, decisions of the log. Sizes: S under two days, M a week, L more than a week. "Needs" names the backend issue that must be merged first.

### 10.1 A0 Admin foundation

Tracking issue: [#3151]. Created on 15 Nov 2026, when the document was approved. The issues run on `feature/a0`, cut from `main`.

| Id | Issue | Section | Rows | Size | Needs |
| --- | --- | --- | --- | --- | --- |
| A-01 [#3139] | Admin projects of 2.8 as stubs in `FluentCMS.slnx`, with an admin shard in CI and the component and API test projects; the plugin admin halves are created by the issues that fill them | 3, 7 | 10.15 | S | |
| A-02 [#3140] | Wrapped component set over the library of decision 26, with theme tokens, light and dark, logical CSS and icons, after the check of AQ1 | 2.3 | 3.1, 3.19, d26 | M | |
| A-03 [#3141] | Management API client: headers, problems, `ETag` and `If-Match`, idempotency keys, rate limits, source-generated JSON | 2.7 | 2.8, 2.15, 7.11 | M | |
| A-04 [#3142] | Runtime `config.json` with validation and an error page | 2.5 | 3.2 | S | |
| A-05 [#3143] | Localization and accessibility groundwork: resources, culture choice, pseudo-cultures, focus, live regions, the cost of culture data | 4.17, 4.18 | 3.18, 3.19 | S | |
| A-06 [#3144] | Architecture rules of the admin and a download size budget | 3.3, 7 | d4 | S | |

Beside A0, without a milestone: [#3150], the display name and title field of a content type (AQ10).

### 10.2 A1 Shell and sign-in

Tracking issue: [#3152]. Created on 15 Nov 2026, with A0. The issues run on `feature/a1`, cut from `main` after `feature/a0` is merged; #3096 runs beside A0 and goes into `main` directly (AQ13).

| Id | Issue | Section | Rows | Size | Needs |
| --- | --- | --- | --- | --- | --- |
| [#3096] | Caller's access, the locales of its sites and its preferences (a gap of the audit, backend 4.10) | 1.4 | 3.1, 7.7 | M | |
| A-07 [#3145] | Sign-in with the code flow and PKCE, refresh, sign-out, and expiry with the draft kept, written in the admin (AQ2) | 2.4 | 3.3, d7 | M | |
| A-08 [#3146] | Shell: layout, sidebar, site switcher, user menu, breadcrumbs, themes, tablet layout | 4.1 | 3.1, 7.13, 4.12 | M | #3096 |
| A-09 [#3147] | Account page: sessions, two-factor, external logins, password change link | 4.3 | 4.2, 4.16 | S | |
| A-10 [#3148] | `FluentCMS.Admin.Hosting` and the admin of the reference host at `/admin`, with the client registration and the local loop with `dotnet watch` | 6.1, 6.4 | 10.2, 11.4, d5 | M | |
| A-11 [#3149] | End-to-end harness on the reference host, with sign-in through the hosted page | 7 | 3.3 | S | |

### 10.3 A2 Lists and forms

Tracking issue: [#3239]. Created on 16 Nov 2026, after `feature/a1` was merged. The issues run on `feature/a2`, cut from `main`, in three waves: A-12 and A-13, then A-48, A-49, A-14 and A-16, then A-15 and A-17.

| Id | Issue | Section | Rows | Size | Needs |
| --- | --- | --- | --- | --- | --- |
| A-12 [#3231] | Generated list views: columns, multi-sort, paging, selection, row actions | 4.4 | 3.4 | M | |
| A-48 [#3232] | List filters by field type, search, status and locale badges | 4.4 | 3.4, 2.23, 7.7 | M | #3094 (badge only) |
| A-13 [#3233] | Generated edit forms: layout from metadata, defaults, save with `If-Match`, error mapping by path | 4.5 | 3.5, 1.10 | M | |
| A-49 [#3234] | Form behaviours: conditions, inline validation, changeable fields, dirty guard, shortcuts, conflict dialog | 4.5 | 1.8, 4.5, 6.8 | M | #3096 |
| A-14 [#3235] | Field editors and list cells for the core field types | 4.6 | 3.6, 1.3 | M | |
| A-15 [#3236] | Relation picker with inline create and side panel | 4.9 | 3.9, 1.4 | M | |
| A-16 [#3237] | Draft, publish, unpublish, discard and status per locale, with publish warnings | 4.10 | 3.11, 6.1, 6.2, 6.7 | M | #3096 |
| A-17 [#3238] | Locale-aware editing: switcher, status per locale, copy from locale, side by side, field direction and formats | 4.17 | 7.7, 7.9 | M | #3096 |

### 10.4 A3 Plugin contract and media

Tracking issue: [#3294]. Created on 17 Nov 2026, after `feature/a2` was merged. The issues run on `feature/a3`, cut from `main`, in four waves: A-18, then A-19 and A-23, then A-21 and A-22, then A-20 and A-50.

| Id | Issue | Section | Rows | Size | Needs |
| --- | --- | --- | --- | --- | --- |
| A-18 [#3287] | `FluentCMS.Admin.Abstractions`: manifest, registration and contributions of an admin half | 5.1 | 8.1, 8.5, 8.7 to 8.9, d12 | M | |
| A-19 [#3288] | Lazy loading per plugin assembly with the manifest, gated by enablement and access; creates the media half as a stub | 2.6 | 8.15, d29 | M | |
| A-20 [#3289] | Media admin half: field editor, list cell and the media picker | 5.2 | 3.10, 1.4, 5.7 | M | #3100, #3103, #3104 |
| A-21 [#3290] | Media library: grid and list, folders, search, upload by drop and paste | 4.14 | 3.15, 5.2, 5.4 | M | #3100, #3103, #3104 |
| A-50 [#3291] | Asset detail: metadata per locale, focal point, private flag, usage, replace, and presets | 4.14, 5.2 | 5.3, 5.7, 5.10, 5.11 | M | #3100, #3103, #3104 |
| A-22 [#3292] | Home page with plugin dashboard widgets | 5.1 | 8.9 | S | #3096 |
| A-23 [#3293] | Admin half in the `fluentcms-plugin` template | 5.1, 6.2 | 8.17 | S | |

### 10.5 A4 Rich text and blocks

| Id | Issue | Section | Rows | Size | Needs |
| --- | --- | --- | --- | --- | --- |
| A-24 | Rich text editor on Tiptap through interop, on the server's ProseMirror model, with the bundle of AQ7 | 4.7 | 3.7, 1.9, d18, d27 | L | |
| A-25 | Entry links and media embeds in rich text | 4.7 | 3.7, 5.12 | S | #3100 |
| A-26 | Component and block editors with ids, reorder by keys, duplicate and previews | 4.8 | 3.8, 1.5, 1.6 | L | |

### 10.6 A5 Publishing, versions, preview and bulk

| Id | Issue | Section | Rows | Size | Needs |
| --- | --- | --- | --- | --- | --- |
| A-27 | Scheduling per locale with time zone, pending schedules shown, moved and cancelled | 4.10 | 3.11, 6.5, 7.6 | S | #3094 |
| A-28 | Version history panel with diff and restore | 4.11 | 3.12, 6.3, 6.4 | M | #3097 |
| A-29 | Preview in an iframe with breakpoints and the `postMessage` contract | 4.12 | 3.13, d28 | M | #3095, #3099 |
| A-30 | Bulk actions in chunks with progress, per-entry results and plugin actions | 4.13 | 3.14, 8.8, 2.14 | M | |

### 10.7 A6 Administration and plugin halves

| Id | Issue | Section | Rows | Size | Needs |
| --- | --- | --- | --- | --- | --- |
| A-31 | Users and invitations | 4.15 | 3.16, 4.2 | M | |
| A-32 | Roles and the grant editor, with plugin permissions and the site's Public role | 4.15 | 3.16, 4.3 to 4.8, 8.14 | L | |
| A-33 | API keys | 4.15 | 3.16, 4.9 | M | |
| A-34 | Settings menu and generated settings pages, with secrets and reset | 4.16 | 3.17, 8.7, 7.13 | M | #3098 |
| A-35 | Sites and locales, and the multi-site admin half | 4.16, 5.6 | 3.17, 7.1, 7.10 | M | #3104 |
| A-36 | Webhooks admin half | 5.3 | 3.17 | M | #3104 |
| A-37 | MCP admin half: clients, instructions, prompts, agent writes | 5.4 | 9.13, 9.10, 9.11, 3.17 | M | #3102, #3104 |
| A-38 | AI assist admin half: field actions with streaming, usage | 5.5 | 9.14, 9.15 | M | #3101, #3103, #3104 |
| A-39 | Plugins, jobs, event dead letters and diagnostics pages, and the search rebuild | 4.15, 5.7 | 6.6, 8.10, 8.15, 10.14 | M | |
| A-40 | Members and member groups, a Should row taken early (AQ11) | 4.15 | 4.10, 4.22 | M | |

### 10.8 A7 Distributions and admin preview

| Id | Issue | Section | Rows | Size | Needs |
| --- | --- | --- | --- | --- | --- |
| A-41 | Translations and per-user culture, time zone and formats | 4.17 | 3.18 | M | #3096 |
| A-42 | Accessibility audit to WCAG 2.2 AA, with the screen reader pass | 4.18 | 3.19 | M | |
| A-43 | Prebuilt static build on the release, with the hosting guide | 6.3 | d5 | S | |
| A-44 | `dotnet new fluentcms-admin`, and the admin in `dotnet new fluentcms` | 6.2 | 11.1, d5 | M | |
| A-45 | End-to-end tests of the daily loop on the image, on three browsers | 7 | 3.19 | M | |
| A-46 | Admin guides in `docs/`: hosting, configuration, writing an admin half | 5.1, 6 | 8.1 | S | |
| A-47 | Release of the admin with a preview of the packages | 6 | 10.2, 11.11 | S | |

50 issues: the 47 of the draft, A-48 and A-49, split from A-12 and A-13 when the document was approved (AQ17), and A-50, split from A-21 when A3 was planned (AQ18), each numbered after A-47 so that no other reference moves. Should rows get issues when their milestone is planned, not now. The one exception is A-40, which takes the members and groups pages of row 4.22 early, for the reason AQ11 gives.

## 11. Decisions on the open questions

The draft asked sixteen questions, each with a recommendation. All were decided on 15 Nov 2026, when the document was approved, and AQ17 was added the same day. The table keeps each question and its decision; where the decision differs from the recommendation of the draft, it says so.

| Id | Question | Decision (15 Nov 2026) |
| --- | --- | --- |
| AQ1 | Component library. Decision 26 recommends Fluent UI Blazor wrapped behind FluentCMS components, adopted unless changed. Is it confirmed, and at which major version? | Confirmed: `Microsoft.FluentUI.AspNetCore.Components` 5.0.1, pinned in `Directory.Packages.props` and named in A-02. Version 5 is a rewrite of version 4 on web-components 3 and is weeks old, so A-02 opens with a check of the data grid, tree, drawer, dialog, menu and date picker against the rules of 4.18, with MudBlazor as the fallback behind the same wrappers. The wrapping of 2.3 and its architecture test hold whatever the library, so that the choice stays reversible. The library's script joins the allow-list of 3.1. |
| AQ2 | Sign-in library: the framework's `Microsoft.AspNetCore.Components.WebAssembly.Authentication`, or the admin's own code flow? | **The flow is written in the admin, in C#, without the spike.** The recommendation to spike the framework's package was not taken: it renews through a hidden iframe with `prompt=none`, which the hosted pages' `frame-ancestors 'none'` forbids, and brings a script of its own. PKCE, the code exchange, a single-flight refresh with rotation, revocation and sign-out are tested against the test host, whose `OAuthFlows` helper exists. A-07 records the choice as a decision record. |
| AQ3 | Where does the admin keep its tokens? | As recommended: the access token in memory and the refresh token in `sessionStorage`, with a strict `Content-Security-Policy` on the admin page (6.1); not `localStorage`. A new tab goes through the authorize redirect, which the server's session answers without a prompt, so two tabs hold two refresh tokens, and the 30-second reuse leeway is for a retry of one tab, not for two tabs sharing a token. |
| AQ4 | How does the shell learn a lazy plugin's contributions before loading its assembly, and where do the half's own services live? A build-time manifest per admin half, two assemblies per half (a small eager one and the lazy rest), or the server's `contributions`? | **A hand-written manifest checked by a test**, not the build step the draft recommended. Each half ships `fluentcms-admin.json` as a static web asset; a test helper of `FluentCMS.Admin.Abstractions` fails the half's tests when the file and the contribution attributes disagree, and the plugin template carries the test (2.6). A build step can replace the file later without a change to the shell. Two assemblies and the server's `contributions` were not taken, for the reasons the draft gave. The provider per half for its services (5.1) stands, and A-19 proves it with the media half. |
| AQ5 | API client: build the admin's calls on `FluentCMS.Client`, or keep a client of its own? | As recommended: reuse `FluentCMS.Client`'s query writer and problem type for content reads, which `clients/filter-cases.json` already tests, if it trims cleanly; keep the admin's own typed calls for the other groups in `FluentCMS.Admin`, so that `FluentCMS.Client` stays the small client of generated code (backend 1.1). |
| AQ6 | Records shared with the server: reference `FluentCMS.Sdk` for `SchemaDescription` and the like, or declare the admin's own? | As recommended: the admin's own records in `FluentCMS.Admin.Abstractions`, checked against the OpenAPI document by the API tests (7). A reference to the SDK would put its service interfaces within reach of every admin half, which decision 4 forbids, and the architecture test of 3.3 could no longer refuse it by package. |
| AQ7 | How is the Tiptap bundle built and shipped? | As recommended: an npm project in `admin/src/FluentCMS.Admin.RichText/js` with a lockfile and pinned versions, bundled into one ES module that is committed, with a CI job that rebuilds it and fails on any difference. So `dotnet build`, the Docker build and a contributor who does not touch the editor need no Node. No CDN at run time. |
| AQ8 | How do the static build and `dotnet new fluentcms` get the first-party admin? | As recommended: the static build ships twice, as a zip on each GitHub release and as static web assets inside `FluentCMS.Admin.Hosting`, so that a host that wants the first-party admin adds one package and `MapFluentCmsAdmin()`. A host with its own admin halves points the hosting package at its own app project instead. |
| AQ9 | Test tooling. New dependencies for component, browser and accessibility tests. | As recommended, with the versions pinned: bUnit 2.15.2 for component tests (A-01), `Microsoft.Playwright` 1.64.1 for end-to-end tests (A-11) and `Deque.AxeCore.Playwright` 4.14.0 for axe-core checks (A-42), each named in the issue that adds it, as CONTRIBUTING asks. Chromium in full CI, Firefox and WebKit nightly. |
| AQ10 | Display labels. Types have no display name and entries no title field in the schema. Derive them in the admin, or add schema metadata? | As recommended: derive them in the first release (4.1), with an optional resource key per type. The additive schema metadata (a display name per type, a title field) is a backend issue without a milestone, created with A0, and done by [#3150], which the admin reads first (4.1). |
| AQ11 | Do members and member groups get admin pages in the first release? They are row 4.22, a Should, and no Must row of category 3 names them; but row 4.10 (Must) has no other UI, its groups are what row rules and public access read (rows 4.6, 4.8), and the routes exist. | Yes, as recommended: A-40 in A6, a list, a detail page, and groups with their role and group assignment. The custom profile fields of row 4.22 stay a Should, with no issue now. |
| AQ12 | Browser support. | As recommended: the last two versions of Chrome, Edge, Firefox and Safari, on desktop and tablet; phones get a readable layout without a test of their own. |
| AQ13 | Where do the gap issues and the admin's work run, and how are its issues labelled? | As recommended: a gap issue takes the admin milestone that first needs it and runs on that milestone's integration branch (`feature/a0`, `feature/a1` and so on), merged into `main` after full CI, as M6 and M7 did (H11); bugs go to `main`. The `area:admin` label and one tracking issue per admin milestone. As decided, only #3096 was still open: it takes milestone A1 and runs beside A0, since A-08 and three issues of A2 wait for it; because it touches the backend only and `feature/a1` does not exist until A0 is merged, its pull request goes into `main` directly, as bugs do. |
| AQ14 | Which interface cultures ship in the first release? | As recommended: English, with every string in resources and a contribution guide for translations; left-to-right and right-to-left pseudo-cultures in the tests. A real right-to-left translation is welcome before 1.0.0 and does not block A7. |
| AQ15 | The published view of the preview needs the front end's URL without a token, and #3099 gives the URL only in the token's answer. | Settled by #3099, as the draft said: the token's answer carries `url` with the token and `publishedUrl` without it, filled from the same template with the published row of the same locale, or null when that locale has no published row. |
| AQ16 | Where do the plugin admin halves live: under `admin/src/plugins/`, or beside their server halves in `src/plugins/`? | As recommended: under `admin/` (3), so that a path filter tells admin work from backend work and the admin's CI shard builds what it tests. The shared ID prefix (row 8.1) keeps the two halves together in NuGet. |
| AQ17 | A-12 and A-13 were L and both on the critical path of A2, and the sizing rule of section 10 says an L issue probably splits during implementation. | Split when the document was approved, so that the lanes of A2 stay parallel: A-12 keeps columns, sort, paging, selection and row actions, and A-48 takes filters, search and badges; A-13 keeps layout, defaults, save and error mapping, and A-49 takes conditions, inline validation, changeable fields, the dirty guard, shortcuts and the conflict dialog (10.3). The new issues are numbered after A-47 so that no other reference moves. |
| AQ18 | A-21 was L: the library, its uploads, the asset detail and the presets in one issue, with the picker of A-20 waiting for its browse component. | Added and decided on 17 Nov 2026, when A3 was planned. Split, as AQ17 did: A-21 keeps browsing, folders, search and upload, and A-50 takes the asset detail (metadata, focal point, private flag, usage, replace) and the presets (10.4). A-19 creates the media half as a stub so that both start from an existing project. Upload shows a state per file and not a progress per byte, since the browser's fetch reports none and the access token stays in C#. |

[#3050]: https://github.com/fluentcms/FluentCMS/issues/3050
[#3094]: https://github.com/fluentcms/FluentCMS/issues/3094
[#3095]: https://github.com/fluentcms/FluentCMS/issues/3095
[#3096]: https://github.com/fluentcms/FluentCMS/issues/3096
[#3097]: https://github.com/fluentcms/FluentCMS/issues/3097
[#3098]: https://github.com/fluentcms/FluentCMS/issues/3098
[#3099]: https://github.com/fluentcms/FluentCMS/issues/3099
[#3100]: https://github.com/fluentcms/FluentCMS/issues/3100
[#3101]: https://github.com/fluentcms/FluentCMS/issues/3101
[#3102]: https://github.com/fluentcms/FluentCMS/issues/3102
[#3103]: https://github.com/fluentcms/FluentCMS/issues/3103
[#3104]: https://github.com/fluentcms/FluentCMS/issues/3104
[#3139]: https://github.com/fluentcms/FluentCMS/issues/3139
[#3140]: https://github.com/fluentcms/FluentCMS/issues/3140
[#3141]: https://github.com/fluentcms/FluentCMS/issues/3141
[#3142]: https://github.com/fluentcms/FluentCMS/issues/3142
[#3143]: https://github.com/fluentcms/FluentCMS/issues/3143
[#3144]: https://github.com/fluentcms/FluentCMS/issues/3144
[#3145]: https://github.com/fluentcms/FluentCMS/issues/3145
[#3146]: https://github.com/fluentcms/FluentCMS/issues/3146
[#3147]: https://github.com/fluentcms/FluentCMS/issues/3147
[#3148]: https://github.com/fluentcms/FluentCMS/issues/3148
[#3149]: https://github.com/fluentcms/FluentCMS/issues/3149
[#3150]: https://github.com/fluentcms/FluentCMS/issues/3150
[#3151]: https://github.com/fluentcms/FluentCMS/issues/3151
[#3152]: https://github.com/fluentcms/FluentCMS/issues/3152
[#3202]: https://github.com/fluentcms/FluentCMS/issues/3202
[#3214]: https://github.com/fluentcms/FluentCMS/issues/3214
[#3231]: https://github.com/fluentcms/FluentCMS/issues/3231
[#3232]: https://github.com/fluentcms/FluentCMS/issues/3232
[#3233]: https://github.com/fluentcms/FluentCMS/issues/3233
[#3234]: https://github.com/fluentcms/FluentCMS/issues/3234
[#3235]: https://github.com/fluentcms/FluentCMS/issues/3235
[#3236]: https://github.com/fluentcms/FluentCMS/issues/3236
[#3237]: https://github.com/fluentcms/FluentCMS/issues/3237
[#3238]: https://github.com/fluentcms/FluentCMS/issues/3238
[#3239]: https://github.com/fluentcms/FluentCMS/issues/3239
[#3287]: https://github.com/fluentcms/FluentCMS/issues/3287
[#3288]: https://github.com/fluentcms/FluentCMS/issues/3288
[#3289]: https://github.com/fluentcms/FluentCMS/issues/3289
[#3290]: https://github.com/fluentcms/FluentCMS/issues/3290
[#3291]: https://github.com/fluentcms/FluentCMS/issues/3291
[#3292]: https://github.com/fluentcms/FluentCMS/issues/3292
[#3293]: https://github.com/fluentcms/FluentCMS/issues/3293
[#3294]: https://github.com/fluentcms/FluentCMS/issues/3294
