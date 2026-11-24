# Admin configuration

At start the admin reads `config.json` from its own base, with `cache: no-store`, so one build of the app works against any API host. A host with `FluentCMS.Admin.Hosting` builds the file from its configuration; on a static host it is a file of the build that the operator edits ([hosting the admin](admin-hosting.md)). This page has its members, how a host builds them, what an admin on another origin needs, and the rate limits the admin counts against. The [admin overview](admin.md) has the distributions.

## `config.json`

```json
{
  "apiBaseUrl": "https://cms.example.com",
  "authority": "https://cms.example.com/",
  "clientId": "fluentcms-admin",
  "scopes": ["openid", "profile", "email", "offline_access", "management"],
  "branding": {
    "name": "Example CMS",
    "logoUrl": "images/logo.svg",
    "faviconUrl": "https://static.example.com/favicon.svg",
    "accentColor": "#0f6cbd"
  },
  "cultures": {
    "offered": ["en"],
    "default": "en"
  },
  "previewOrigins": ["https://www.example.com"]
}
```

| Member | In `AdminConfiguration` | Rule | Default |
| --- | --- | --- | --- |
| `apiBaseUrl` | `ApiBaseUrl` | The origin and path base of the API host, such as `https://cms.example.com` or `https://example.com/cms`: an absolute `http` or `https` URL without user info, query or fragment. A trailing slash is optional; the admin adds one, so that its calls resolve under the path base. | required |
| `authority` | `Authority` | The issuer of the authorization server, a URL by the same rule, kept as written: the admin compares it with the issuer of the discovery document as an exact string. A FluentCMS host writes its issuer with a slash after the host name, `https://cms.example.com/`, and without one after a path base, `https://example.com/cms`. | `apiBaseUrl` as written |
| `clientId` | `ClientId` | The OAuth client id, visible ASCII characters without spaces. | `fluentcms-admin` |
| `scopes` | `Scopes` | The scopes asked at sign-in, a non-empty array of distinct scope tokens. | `["openid", "profile", "email", "offline_access", "management"]` |
| `branding.name` | `Branding.Name` | The page title and the name of the shell's logo: text with no control character and no space at its start or end. | `FluentCMS` |
| `branding.logoUrl` | `Branding.LogoUrl` | The image of the shell's logo: an absolute `http` or `https` URL without user info or fragment, or a path relative to the admin's base, such as `images/logo.svg`. | the built-in logo |
| `branding.faviconUrl` | `Branding.FaviconUrl` | The favicon, by the rule of `logoUrl`. | the built-in favicon |
| `branding.accentColor` | `Branding.AccentColor` | A hex color: `#rgb`, `#rgba`, `#rrggbb` or `#rrggbbaa`. | the theme's accent |
| `cultures.offered` | `Cultures.Offered` | The interface cultures the admin offers, a non-empty array of culture names such as `en` or `fr-CA`, each once in any case. | every culture the admin ships |
| `cultures.default` | `Cultures.Default` | The culture the admin starts in when neither the user nor the browser names an offered one; one of `offered` when that is set. | `en` |
| `previewOrigins` | `PreviewOrigins` | The origins the preview frame may load, an array of `http` or `https` URLs without a path, such as `https://www.example.com`, never the admin's own origin. | none, which turns preview off |

Every URL is ASCII only, with no space, quote, parenthesis, angle bracket, backslash or backtick: write an internationalized host name in its `xn--` form. A plain `http` URL of `apiBaseUrl` or `authority` on a host that is not loopback is accepted, with a warning in the browser's console. A member set to `null` counts as not set. A member the admin does not know, also inside `branding` and `cultures`, is ignored with a warning in the console, so a newer file works with an older build.

A missing or invalid member, a member written twice in one object, a file that is not a JSON object, and a file that cannot be read within 15 seconds each show a page that names the problem, before the admin calls the API. Fix the file and reload.

In the app, the file is the record `AdminConfiguration` of `FluentCMS.Admin.Abstractions`, with `Branding` an `AdminBranding` and `Cultures` an `AdminCultures`. The admin registers it as a singleton only when the file is valid, so a component of an admin half that injects it never sees an invalid one. `PreviewEnabled` is true when `PreviewOrigins` holds an origin.

`config.json` is as trusted as `index.html`: whoever can write it can point `authority` anywhere. The checks guard against mistakes, not against an attacker.

## Built by the host

A host with `FluentCMS.Admin.Hosting` answers `/admin/config.json` itself, with `Cache-Control: no-store`, and never serves the app's sample file. It checks each value at start with the rules above, so a value the admin would refuse stops the host with `FCMS1101` and the key instead of showing the admin's error page. [Host configuration](host-configuration.md#admin) has the defaults of the keys and what else the host serves.

| Key | Gives |
| --- | --- |
| `FluentCms:Admin:Enabled` | Whether the host serves the admin and registers its client at all. |
| `FluentCms:Admin:ApiBaseUrl` | `apiBaseUrl`. Unset, it is `FluentCms:PublicOrigin` with the path base, or the request's scheme, host and path base in Development. |
| `FluentCms:Admin:Branding:Name` | `branding.name` |
| `FluentCms:Admin:Branding:LogoUrl` | `branding.logoUrl`; the origin of an absolute URL joins the page's `img-src`. |
| `FluentCms:Admin:Branding:FaviconUrl` | `branding.faviconUrl`, as `LogoUrl` |
| `FluentCms:Admin:Branding:AccentColor` | `branding.accentColor` |
| `FluentCms:Admin:Cultures:Offered` | `cultures.offered`, each culture as an item: `FluentCms:Admin:Cultures:Offered:0`, `:1` and so on. |
| `FluentCms:Admin:Cultures:Default` | `cultures.default` |
| `FluentCms:Admin:PreviewOrigins` | `previewOrigins`, each origin as an item, and the page's `frame-src`. The admin's own origin stops the host with `FCMS1101`. |

The other members are not keys of `FluentCms:Admin`:

- `authority` is the issuer of the host's own authorization server, built from `FluentCms:PublicOrigin` (or the request in Development).
- `clientId` is `FluentCms:Clients:AdminClientId`, `cms.Clients.AdminClientId` in code, `fluentcms-admin` by default.
- `scopes` are always the five of the table above.

A host may set the same options in code. A single value from configuration wins over the one the code sets, but a list is not replaced: the items of `PreviewOrigins` and `Cultures:Offered` from configuration are added to those the code adds, so preview origins merge, and a culture named both in code and in configuration is named twice, which stops the host with `FCMS1101`:

```csharp
builder.Services.AddFluentCms(cms =>
{
    cms.UseSqlite(builder.Configuration.GetConnectionString("cms"));
    cms.AddAdmin(admin =>
    {
        admin.Branding.Name = "Example CMS";
        admin.PreviewOrigins.Add("https://www.example.com");
    });
});
```

In the environment of a container, the keys are written with double underscores, such as `FluentCms__Admin__Branding__Name` and `FluentCms__Admin__PreviewOrigins__0` ([Run with Docker](docker.md#the-admin)).

## An admin on another origin

An admin served from another origin than the API, such as the prebuilt static build on a static host or an app of `dotnet new fluentcms-admin` run on its own, needs two settings on the API host. Here for an admin at `https://admin.example.com/admin/`:

```json
{
  "FluentCms": {
    "Security": { "Cors": { "Management": { "AllowedOrigins": [ "https://admin.example.com" ] } } },
    "Clients": {
      "Applications": {
        "fluentcms-admin": {
          "RedirectUris": [ "https://admin.example.com/admin/authentication/login-callback" ],
          "PostLogoutRedirectUris": [ "https://admin.example.com/admin/" ],
          "Scopes": [ "openid", "profile", "email", "offline_access", "management" ]
        }
      }
    }
  }
}
```

- `FluentCms:Security:Cors:Management:AllowedOrigins` lets the admin's origin call `/api/management/v1`, `/connect` and `/.well-known`, where the admin reads the discovery document, with the headers the admin sends and reads ([CORS](host-configuration.md#cors)).
- `FluentCms:Clients:Applications:fluentcms-admin` registers the admin's client with the admin's addresses, as the next section says.

Signed asset URLs are relative to the API's origin, so the admin resolves them against `apiBaseUrl`, and the page's `img-src` must allow that origin. The admin's own host sends the page's headers; [hosting the admin](admin-hosting.md#what-any-host-must-do) has the policy for a static host.

A host that also serves the admin at `/admin` lists that admin's addresses beside these, since a configured client replaces the one the hosting package registers.

## The client `fluentcms-admin`

While `FluentCms:Admin:Enabled` is true, `cms.AddAdmin()` registers the admin's client itself, with the addresses of the admin it serves, unless `FluentCms:Clients:Applications` or `cms.Clients.Applications` lists a client under `cms.Clients.AdminClientId`. A listed client wins whole. A host that lists it, for an admin on another origin or for a host that registers its clients itself, gives it:

- **No `Secret`.** The admin is a public client: it runs in the browser and can keep no secret. It signs in with the authorization code flow and PKCE, which the authorization server requires of every client.
- **The five scopes**: `openid`, `profile`, `email`, `offline_access` and `management`. The host's `config.json` always asks for the five, and the admin renews its session with the refresh token that `offline_access` brings.
- **The redirect URIs of each admin that signs in with it**: `{base}authentication/login-callback` among `RedirectUris` and `{base}` among `PostLogoutRedirectUris`, where `{base}` is the address the admin is served at, with its trailing slash, such as `https://cms.example.com/admin/` for the admin of the host itself.
- **The admin pool.** `Pool` stays at its default, `Admins`; a client of the member pool under the admin's id stops the host at start.
- **No consent page.** `RequireConsent` stays at its default, false.

Decision record [0007](decisions/0007-admin-sign-in-flow.md) has the admin's side of the sign-in, and [authorization server](host-configuration.md#authorization-server) the server's.

## Branding

`branding` names and colours the admin, not the hosted sign-in pages, whose branding is the settings area `branding` of the instance ([hosted pages](host-configuration.md#hosted-pages)).

- `name` is the page title and the name of the shell's logo.
- `logoUrl` is the image of the logo and `faviconUrl` the favicon. Without them the admin shows its built-in logo and favicon, `_content/FluentCMS.Admin.Components/favicon.svg`, so a first load asks for no `/favicon.ico`. An absolute URL on another origin needs that origin in the page's `img-src`: the host adds it for the keys above, and a static host adds it to its policy.
- `accentColor` is the accent of the theme. The admin sets it as the custom property `--fluentcms-brand-accent` on the document, with two more it derives from it: the color on the accent, black or white, whichever contrasts more, and the shade its hover and pressed states mix in. An alpha is left out of that choice.

The admin applies the branding without a `<style>` element, so the page's `Content-Security-Policy` needs nothing more for it.

## Cultures

`cultures.offered` limits the interface languages to those named, among the cultures the admin ships: English, and the culture of each translation in the build ([translating the admin](admin-translating.md)). A culture the build does not ship is left out; with none left, every shipped culture is offered. Without `offered`, every shipped culture is.

At start the admin takes the first of these that is offered: the user's saved language, through the copy the browser keeps, then the browser's language, then `cultures.default`. A culture matches itself or a parent, so a browser in `en-GB` gets `en`. A `default` that the build does not ship gives the first offered culture. The account page offers a choice of language only when more than one culture is offered ([language and formats](admin.md#language-and-formats)).

## Preview

`previewOrigins` lists the origins of the front ends whose pages the admin shows in its preview pane, which loads the preview address the API gives for an entry in a frame. Without an origin the form offers no preview. The admin refuses a preview address on an origin the list does not hold, and one on the admin's own origin, since a frame of that origin could reach the admin's session. A host puts the origins in the page's `frame-src`; a static host puts them in its policy ([hosting the admin](admin-hosting.md#what-any-host-must-do)).

## Rate limits

An admin served by the API host at `/admin` counts against the host's rate limits before authentication, per client address, as [rate limits](host-configuration.md#rate-limits) describes:

- **The app's files**, those under `_framework/` and `_content/` with their compressed copies and fingerprinted paths, and the other files of the build, are marked as static files and count under `FluentCms:RateLimits:StaticFiles:PermitLimit`, 15000 a minute, instead of the coarse limit. A cold load is some 230 files, so the default holds about forty cold loads a minute from one address.
- **The page and its configuration**, `/admin/`, `index.html`, every client route that answers the page and `config.json`, count under the coarse limit, `FluentCms:RateLimits:PreAuth:PermitLimit`, 6000 a minute, with every call the admin makes to the API and the sign-in. The calls of the Management API count under `FluentCms:RateLimits:Management:PermitLimit` per user as well.
- **The sign-in**, every request to `/connect` and `/account`: the token exchange after the login page, each refresh of the access token, the sign-out and the hosted pages themselves, count under `FluentCms:RateLimits:Auth:PermitLimit`, 20 a minute per address, besides the coarse limit. It is the strict limit that guards passwords and tokens, so it is the one an office behind one address meets first, when many users sign in at once or their tabs refresh their tokens.

Behind one address, such as an office behind a proxy without forwarded headers, every user of the admin shares these. Turn on `FluentCms:ReverseProxy:ForwardedHeaders` with the proxies you trust, so that each caller is counted by its own address ([reverse proxy](reverse-proxy.md)); that is the fix for each of the three. Without it, raise `Auth:PermitLimit` when sign-ins and refreshes behind one address are refused, knowing that it also lets more password guesses through from that address; `StaticFiles:PermitLimit` only when more people than that load the admin behind one address after a deploy; and `PreAuth:PermitLimit` when they work at once through it. A refusal is a 429 with `Retry-After`. A call of the API that is refused waits its `Retry-After`, up to 30 seconds, and is sent once more; a second refusal shows a notice.

An admin on a static host loads its files from that host, so only its calls to the API and the sign-in count against the API host's limits.
