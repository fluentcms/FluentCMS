# Host configuration

To run FluentCMS without writing a host, use the reference host and its image; see [Run with Docker](docker.md).

A host registers the engine with `AddFluentCms()` and puts its HTTP surfaces on the routes with `MapFluentCms()`:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddFluentCms(cms => cms.UseSqlite(builder.Configuration.GetConnectionString("cms")));

var app = builder.Build();
app.UseFluentCms();
app.MapFluentCms();
app.Run();
```

`MapFluentCms()` creates the route groups `/api/delivery/v1`, `/api/management/v1`, `/connect`, `/account` and `/health`, and maps the [hosted pages](#hosted-pages). They are relative to the request's `PathBase`, which `UseFluentCms()` sets from `FluentCms:ReverseProxy:PathBase` (see [reverse proxy](reverse-proxy.md)), so a host on a sub-path does not call `UsePathBase` itself. The builder it returns applies conventions to every FluentCMS endpoint.

`UseFluentCms()` puts the request pipeline in place, in the order it works in: what `FluentCms:ReverseProxy` asks for (forwarded headers, the path base, HSTS and the HTTPS redirect), the exception handler (problem details), routing, security headers, CORS, the gate that holds FluentCMS routes to the path base, a coarse limit per client address, authentication, the rate limiter of the endpoints and the request limits. Call it once, before `MapFluentCms()`, which needs it (the endpoints carry CORS and rate limit metadata that only these middleware read), and add `UseAuthorization()` after it. It includes `UseExceptionHandler()`, `UseRouting()` and `UseAuthentication()`, so a host does not add them again. The order matters: CORS comes before the limiters so that a 429 still carries CORS headers, the coarse limit comes before authentication so that a flood is turned away before it reaches a key lookup or the token endpoint, and authentication comes before the endpoint limiter because that partitions by API key or principal. `MapFluentCms()` throws `FCMS1104` at startup when `UseFluentCms()` was not called.

## Requests

An endpoint runs as the principal of the `FluentCms` authentication scheme, which `AddFluentCms()` registers: the API key handler when a key is sent, otherwise the bearer scheme set in `FluentCmsAuthenticationOptions.BearerScheme`, and anonymous when neither applies. The handler writes `sub` (the id), `name`, `fcms_kind` (a `PrincipalKind` name), `roles` and `scope` (both space-separated), and `fcms_sites` for an API key bound to sites. A request without an authenticated user runs as the anonymous principal, which holds the Public role of its site, so anonymous Delivery access is what that role grants. A principal without a usable `fcms_kind` or `sub` is refused with 401.

The site is the key's binding, then the `X-Cms-Site` header, then the `site` parameter (an id or a slug), then the default site. A key bound to one site reads that site. A key bound to several reads the one the request names, or the default site when it is among them, and is otherwise refused with `FCMS3122`. A request that names a site outside the binding, or one that does not exist, is refused with `FCMS3121` (403). A header and a parameter that name different sites are refused. The locale is the `locale` parameter, then the `X-Cms-Locale` header, then the site's default; `locale=all` returns every locale of an entry as a map.

## Options

Everything is read from the `FluentCms` section of the host's configuration, so any configuration provider works. Environment variables use `__` for `:`, as in `FluentCms__Database__MigrateOnStartup=false`.

| Section | Holds |
| --- | --- |
| `FluentCms:Database` | `MigrateOnStartup`, `SchemaSync` (see [content migrations](content-migrations.md)) |
| `FluentCms:Security` | `AllowGeneratedSigningKey`, `SigningCertificate:Path`, `SigningCertificate:Password`, `Cors:Delivery:AllowedOrigins`, `Cors:Management:AllowedOrigins`, `ReferrerPolicy`, `FrameAncestors` |
| `FluentCms:Users` | `Password`, `Lockout`, `InvitationLifetime` (7 days), `PasswordResetLifetime` (1 hour) (see [hosted pages](#hosted-pages)) |
| `FluentCms:Email` | `Smtp:Host`, `Smtp:Port` (587), `Smtp:Security` (`StartTls`, `SslOnConnect` or `None`), `Smtp:UserName`, `Smtp:Password`, `Smtp:From` (see [email](#email)) |
| `FluentCms:Clients` | `AdminClientId`, and `Applications:{clientId}` with `DisplayName`, `Secret`, `RedirectUris`, `PostLogoutRedirectUris`, `Scopes`, `Roles`, `RequireConsent` (see [authorization server](#authorization-server)) |
| `FluentCms:ReverseProxy` | `PathBase`, `ForwardedHeaders`, `HttpsRedirection`, `Hsts` (see [reverse proxy](reverse-proxy.md)) |
| `FluentCms:RateLimits` | `Delivery:PermitLimit`, `Delivery:MaxKeyLimit`, `Management:PermitLimit`, `Auth:PermitLimit`, `PreAuth:PermitLimit` |
| `FluentCms:Limits` | `MaxBodySize` |
| `FluentCms:Delivery:Cache` | `CacheControl`, `OutputCache`, `OutputCacheDuration` (see [Delivery caching](#delivery-caching)) |
| `FluentCms:Query` | `DefaultPageSize`, `MaxPageSize`, `MaxPopulateDepth`, `MaxFilterNodes`, `MaxPopulatedEntries` |
| `FluentCms:Jobs` | the job runner: `Queues`, `PollInterval`, `LeaseDuration`, retries, retention |
| `FluentCms:Events:Outbox` | the outbox: `BatchSize`, `MaxAttempts`, `LeaseDuration`, retries, retention |
| `FluentCms:Activity` | the activity log: `Enabled`, `Retention`, `SiteRetention`, `PruneSchedule` |
| `FluentCms:Health` | `JobRunnerHeartbeatTimeout` |

A key the configuration sets wins over what the host set in code inside `AddFluentCms`; a key it leaves out keeps the code's value. The options are checked when the host starts, and an invalid value stops it with an `FCMS` code and the key, for example `FCMS1101: FluentCms:Query:MaxPageSize: The field MaxPageSize must be between 1 and 2147483647.`

A CORS origin is an `http` or `https` origin with no path, such as `https://app.example.com`. The lists are empty by default, which allows no browser origin, and a wildcard is refused.

## Health and telemetry

`/health/live` answers while the process runs, and `/health/ready` also needs the database and the job runner. Both need no authentication, answer 200 or 503, and print the status and nothing else. `GET /api/management/v1/system/health` lists every check with its duration and needs the `system.read` permission.

`FluentCms:Health:JobRunnerHeartbeatTimeout` is how old the last beat of the job runner may be before `/health/ready` is unhealthy. The runner beats every `FluentCms:Jobs:MaintenanceInterval` (15 seconds), so keep this well above it. The default is `00:02:00`. An instance that runs no jobs leaves the runner out of the check with the infinite value, which as a `TimeSpan` string is `-00:00:00.001` (`Timeout.InfiniteTimeSpan` in code).

FluentCMS exports nothing. To send traces and metrics to your own tools, subscribe your OpenTelemetry setup to the source and the meter both named `FluentCMS`, with `AddSource("FluentCMS")` and `AddMeter("FluentCMS")`.

## Security defaults

Cookie authentication schemes and the antiforgery cookie are `Secure` and `HttpOnly`, and authentication cookies are `SameSite=Lax`. A host that configures a cookie after `AddFluentCms` overrides this.

## Rate limits

ASP.NET Core's rate limiter runs three policies, each a fixed window of one minute. The groups carry their policy as endpoint metadata:

| Policy | Group | Partitioned by | Default per minute |
| --- | --- | --- | --- |
| `delivery` | `/api/delivery/v1` | the API key's `sub`, or the client address | 600 |
| `management` | `/api/management/v1` | the principal's `sub`, or the client address when there is none | 300 |
| `auth` | `/connect`, `/account`, checked by path before authentication | the client address | 20 |
| coarse (`PreAuth`) | every route but `/health`, before authentication | the client address | 6000 |

An API key whose principal has an `fcms_rate_limit` claim gets that many requests per minute instead of the `delivery` default, at most `Delivery:MaxKeyLimit` (5000), which must not be below `Delivery:PermitLimit` nor above `PreAuth:PermitLimit`, the coarse limit that holds every caller behind one address together. `/health` is not limited.

The coarse limit is checked by path, before authentication, so it also covers requests that no endpoint answers, such as `/connect/token` from the authorization server. It adds no `RateLimit-*` headers; a refusal is the same 429 problem. The strict `auth` limit is checked there too, for every path under `/connect` and `/account`, because the authorization server answers `/connect/token`, `/connect/introspect` and `/connect/revoke` without routing; the endpoints of those groups do not count a request a second time.

A response carries `RateLimit-Limit`, `RateLimit-Remaining` and `RateLimit-Reset` (seconds to the end of the window), from the IETF draft. A refused request is a 429 `application/problem+json` with code `FCMS3110` and a `Retry-After` in seconds.

The key and the principal are those of the `FluentCms` authentication scheme when the host has one (the API key registration adds it, as a policy scheme that forwards to the key scheme when a key is presented and otherwise to the default scheme), so a cookie or token default scheme does not hide a key. Without it they are those of the default scheme.

The client address is the connection's remote address, an IPv4-mapped address counted as its IPv4 address and an IPv6 address by its /64. Behind a proxy, turn on `FluentCms:ReverseProxy:ForwardedHeaders` with the proxies you trust, which `UseFluentCms()` applies before the limiters, or every caller shares the proxy's address.

## CORS

`Cors:Delivery:AllowedOrigins` applies to `/api/delivery/v1`, which allows `GET` and `HEAD`. `Cors:Management:AllowedOrigins` applies to `/api/management/v1` and to `/connect`, so the admin application can sign in from its own origin. An origin that is not on the list gets no CORS headers, and an empty list, the default, allows none. The lists come from configuration only; a setting that edits them at run time is not part of this release.

## Security headers and request limits

Every FluentCMS response carries `X-Content-Type-Options: nosniff` and `Referrer-Policy` (`FluentCms:Security:ReferrerPolicy`, default `no-referrer`). The `/account` group, which is where the hosted pages are, also carries `Content-Security-Policy: frame-ancestors ...` from `FluentCms:Security:FrameAncestors`, default `'none'`. A header that an endpoint sets itself is kept, and `frame-ancestors` is added to an endpoint's own policy when that has none. A host's own endpoints are not touched.

A request to a FluentCMS endpoint with a body larger than `FluentCms:Limits:MaxBodySize` bytes (default 2 MiB) is refused with 413 and code `FCMS3111`, whether the size is declared or only found out while the body is read. The cap only lowers: a lower limit that the host or Kestrel already set stays, and an endpoint with its own size limit keeps it. The page size, populate depth and filter node caps are the engine's `FluentCms:Query` options; a query over one comes back as a 400 problem with code `FCMS2207`.

## Delivery caching

The options are `FluentCms:Delivery:Cache` (`cms.DeliveryCache` in code). A Delivery response carries an `ETag`, a `Last-Modified` and a `Cache-Control`, and varies on `Authorization`, `X-Api-Key`, `X-Cms-Site` and `X-Cms-Locale`. A request with a matching `If-None-Match` gets a 304; `If-Modified-Since` alone is answered in full, because an entry that leaves a list moves no date.

| Key | Default | Meaning |
| --- | --- | --- |
| `CacheControl` | `public, max-age=60, stale-while-revalidate=300` | What browsers and CDNs are told for a published read without credentials. A read with an API key or a token gets the same value as `private`, a draft read gets `no-store`, and an error gets `no-cache,no-store`. |
| `OutputCache` | `true` | Whether the server keeps published reads in the ASP.NET Core output cache. Off, nothing is evicted and no `CacheInvalidatedEvent` is raised. |
| `OutputCacheDuration` | `00:01:00` | How long it keeps one, at most. |

The server keeps one copy for every request without credentials and one per API key, and keeps nothing for a bearer token, a draft read, credentials it refuses, an error or a request with `If-Modified-Since` and no `If-None-Match`. Publishing, unpublishing, trashing and restoring an entry evict the tags `entry:{id}` and `type:{site}:{type}` (`type:global:{type}` for a global type) through the outbox and raise `CacheInvalidatedEvent` with them, so a CDN can purge the same; a change to a site or its locales evicts `site:{id}` the same way. Every read is tagged with its site, its type and every entry in it, populated ones included. A change to a role's permissions, or a newly published target of a relation, shows when the copy expires.

`UseFluentCms()` adds the output cache middleware, so a host that caches its own endpoints does not call `UseOutputCache()` again. FluentCMS routes other than Delivery are never cached, even under a base policy of the host's. The store is in memory: with several instances, only the instance that delivers the outbox event evicts its copies, and the others serve theirs until `OutputCacheDuration` runs out. A shared `IOutputCacheStore`, such as the Redis one from `Microsoft.AspNetCore.OutputCaching.StackExchangeRedis`, removes that lag.

## Startup report and diagnostics

Once the migrations have run, the host logs one `StartupReport` (event id 1100, Information) and a warning per insecure default (event id 1101). The report is a public type of the SDK (`FluentCMS.Diagnostics.StartupReport`). It names the environment, the FluentCMS and data provider versions, the FluentCMS assemblies in the process, the plugins (none until the plugin host exists), the pending migrations of the system, plugin and content streams, the schema hash in code against the one last applied, and the insecure defaults: the refusals of the production checks below even in an environment that does not enforce them, a generated signing key that `AllowGeneratedSigningKey` accepted, the Development email sender, the HTTPS redirect being off in Production, `AllowedHosts` left at `*` or unset in Production, schema sync running outside Development (see [content migrations](content-migrations.md#schema-sync)), and forwarded headers trusted from every address. It holds no connection string, key or password. If the report cannot be made, the log says so (event id 1102, with the scrubbed message) and the report is reduced to what needs no database, with `failure` naming the exception type; a request that arrives before any report exists gets a 503 (`FCMS3106`).

`GET /api/management/v1/system/diagnostics` returns the same report as JSON. It needs the instance-wide permission `system.read` (a Super Admin has it), which a role of one site, a grant limited to sites and a key bound to sites do not reach, and which a Delivery or Preview key never gets because it lacks the management scope.

## Production refusals

In the Production environment some settings that are fine in development stop the host before it touches the database:

| Code | Refusal | Lifted by |
| --- | --- | --- |
| `FCMS1102` | Tokens are signed with a generated key, because no signing certificate is configured | `FluentCms:Security:AllowGeneratedSigningKey = true` |

## API keys

`AddFluentCms()` registers the `ApiKey` authentication scheme. It reads a key from `Authorization: ApiKey <key>` or from the `X-Api-Key` header, and leaves a request with neither to the other schemes. A request with a key and any other credential is refused. The FluentCMS endpoints authenticate with the `FluentCms` scheme, which uses `ApiKey` when the request carries a key and otherwise the bearer scheme of the authorization server. It never uses the host's default scheme, so a cookie is not a credential for the APIs. The host calls `app.UseAuthentication()` before `MapFluentCms()`. A key's `last_used_at` is written at most once every five minutes.

## Authorization server

`AddFluentCms()` adds the OpenIddict server at `/connect/authorize`, `/connect/token`, `/connect/userinfo`, `/connect/introspect`, `/connect/revoke` and `/connect/logout`, with discovery at `/.well-known/openid-configuration` and the keys at `/.well-known/jwks`. It answers HTTPS only; behind a proxy that ends TLS, forward the scheme. Under `FluentCms:ReverseProxy:PathBase` these endpoints, the issuer and the discovery URLs carry the path base, and the endpoints are not answered without it. The flows are the authorization code flow with PKCE (`S256`, for every client), refresh tokens that rotate and are refused once used, and client credentials. Access tokens are signed JWTs with the audience `fluentcms:management` and are valid for an hour.

A bearer token is read from the `Authorization` header only, never from the query string or a form. Logout takes a POST, or a GET that carries the session's `id_token_hint`; any other GET goes to the logout page, which asks the user and posts it. The token endpoint refuses a code or refresh token of a user who is deactivated, locked out or whose password or security stamp changed since. A recurring job, `oidc.prune` (daily at 04:15 UTC, scheduled a minute after the host starts), removes tokens and sessions that can no longer be used once they are 14 days old.

Clients are registered when the host starts, from `FluentCms:Clients:Applications` or `cms.Clients.Applications` in code, keyed by client id. A client with `RedirectUris` signs users in; one with a `Secret` is confidential and may sign in as a service account with client credentials, holding the instance-wide roles `Roles` names. `Scopes` are what it may ask for: `openid`, `profile`, `email`, `offline_access`, `draft` and `management`. Requests with a token of the client named by `AdminClientId` (`fluentcms-admin` by default) run with the `Admin` origin. A client that was registered from the options and is no longer listed is deleted, with its sessions and tokens, at the next start, with a warning that names it. When the options list no client at all, which is more likely a configuration that failed to load, nothing is deleted and a warning names the clients kept. A client registered some other way is left as it is. The authorize endpoint needs the user signed in on the hosted pages and sends anyone else to `/account/login`. A client with `RequireConsent` is a third-party one: the user allows it on the consent page before it gets a code, once for the scopes it asks; the clients of the host, such as the admin, sign in without that page.

Tokens are signed with `SigningCertificate` (a PKCS#12 file with an RSA key) when it is set. Otherwise the server generates an RSA key and keeps it in `cms_signing_key`, protected with Data Protection, so every instance and every restart uses the same one; Production refuses that unless `AllowGeneratedSigningKey` is set. The key that encrypts codes and refresh tokens is always kept there. The Data Protection key ring is kept in `cms_data_protection_key` under the application name `FluentCMS`, unless the host persists it elsewhere, and is loaded on its first use rather than when the host starts. A new key is made under the distributed lock (`IDistributedLock`, kept in `cms_lock` with no configuration), so instances that start together on an empty database share the first one.

The key ring is not encrypted at rest unless the host adds an encryptor, and anyone who can read the database (a backup, a read-only user) can then read it and forge the sign-in cookie the authorize endpoint accepts. In production, add one after `AddFluentCms`, with a certificate from the host's secret store:

```csharp
builder.Services.AddDataProtection().ProtectKeysWithCertificate(certificate);
```

or with Azure Key Vault (`ProtectKeysWithAzureKeyVault` from `Azure.Extensions.AspNetCore.DataProtection.Keys`). Keys written before the encryptor was added stay readable. Until one is added, the startup report lists `data-protection-keys-unencrypted`. Keep `cms_data_protection_key`, `cms_signing_key` and the `cms_oidc_*` tables out of exports, and treat a database backup as holding secrets.

OpenIddict logs whole tokens at the `Trace` level. Do not set the `OpenIddict` log category to `Trace` in production.

## Hosted pages

The pages of the authorization server are Razor Pages under `/account`: login, logout, consent, two-factor and recovery code, forgot and reset password, accept invitation, change password, external login and error. `AddFluentCms()` registers Razor Pages and `MapFluentCms()` maps them with `MapRazorPages()`, so a host adds nothing. That maps the host's own Razor Pages too, and a host that maps them itself still gets each page once. They carry the `/account` headers and the `auth` rate limit per client address, run no script, and send a `Content-Security-Policy` that allows inline styles, and images from the host or over https only.

Branding is the settings area `branding` (`BrandingSettings` in `FluentCMS.Core.Api.Account`): `Name` (default `FluentCMS`), `LogoUrl` (an https URL or a path of the host, such as `/media/logo.svg`), and the hex colours `PrimaryColor`, `BackgroundColor` and `TextColor`. The pages read the instance defaults, which need `settings.branding.manage` to change. To change more than that, override a page or the layout: add a Razor page at the same path to the host project, such as `Areas/FluentCms/Pages/Account/Login.cshtml` or `Areas/FluentCms/Pages/Shared/_Layout.cshtml`, with `@model FluentCMS.Core.Api.Account.LoginModel` for a page, so that it keeps the page's behaviour. A page of the host wins over the one of the same path in FluentCMS.

A user who forgets the password gets a link by email that works once and for `FluentCms:Users:PasswordResetLifetime`. The page answers the same whether or not the address belongs to a user. The link, like the issuer, is built from the request's scheme, host and path base, so set `AllowedHosts` to the host names of the instance in production: otherwise a request with a forged `Host` header gets a link to that host sent to a real user. An invitation links to `/account/accept-invitation`. Logins, failed and successful, are `user.login_failed` and `user.login_succeeded` activity rows with the client address.

## Email

Invitations and password resets go through `IEmailSender`. In Development the default writes each message, link included, to the log and the startup report lists `development-email-sender`; anywhere else it refuses to send (`FCMS3008`) until a sender is set up. Setting `FluentCms:Email:Smtp:Host` sends through that SMTP server in every environment, Development included:

| Key | Default | Meaning |
| --- | --- | --- |
| `Smtp:Host` | none | The SMTP server. Without it, the defaults above apply. |
| `Smtp:Port` | `587` | Its port. |
| `Smtp:Security` | `StartTls` | `StartTls` refuses a server that does not offer TLS; `SslOnConnect` is TLS from the first byte, as on port 465; `None` sends in the clear and is for a relay on a trusted network only. |
| `Smtp:UserName`, `Smtp:Password` | none | The account to sign in with. Without a user name the server is not asked to authenticate. Keep the password in a secret store. |
| `Smtp:From` | none, required with a host | The sender, such as `cms@example.com` or `FluentCMS <cms@example.com>`. |

A missing or invalid `From` stops the host with `FCMS1101`. A host or a plugin that registers its own `IEmailSender` replaces all of this.
