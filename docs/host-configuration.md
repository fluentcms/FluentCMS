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
| `FluentCms:Security` | `AllowGeneratedSigningKey`, `AllowRequestOrigin`, `AllowUnencryptedKeyRing` (see [Production refusals](#production-refusals)), `SigningCertificate:Path`, `SigningCertificate:Password`, `Cors:Delivery:AllowedOrigins`, `Cors:Management:AllowedOrigins`, `ReferrerPolicy`, `FrameAncestors` |
| `FluentCms:Users` | `Password`, `Lockout`, `InvitationLifetime` (7 days), `PasswordResetLifetime` (1 hour) (see [hosted pages](#hosted-pages)) |
| `FluentCms:Email` | `Smtp:Host`, `Smtp:Port` (587), `Smtp:Security` (`StartTls`, `SslOnConnect` or `None`), `Smtp:UserName`, `Smtp:Password`, `Smtp:From` (see [email](#email)) |
| `FluentCms:Clients` | `AdminClientId`, and `Applications:{clientId}` with `DisplayName`, `Secret`, `RedirectUris`, `PostLogoutRedirectUris`, `Scopes`, `Roles`, `RequireConsent` (see [authorization server](#authorization-server)) |
| `FluentCms:Sso` | `DisableLocalLogin`, and `Providers:{name}` with `Preset`, `DisplayName`, `Authority`, `TenantId`, `Domain`, `AuthorizationServerId`, `BaseUrl`, `Realm`, `ClientId`, `ClientSecret`, `Scopes`, `AutoProvision`, `LinkExistingUsers`, `DefaultRole`, `EmailClaim`, `GroupsClaim`, `RoleMappings`, `TrustMfaClaim`, `RequireHttpsMetadata` (see [single sign-on](#single-sign-on)) |
| `FluentCms:PublicOrigin` | the address users reach the instance at, such as `https://cms.example.com` (see [public origin](#public-origin)) |
| `FluentCms:ReverseProxy` | `PathBase`, `ForwardedHeaders`, `HttpsRedirection`, `Hsts` (see [reverse proxy](reverse-proxy.md)) |
| `FluentCms:RateLimits` | `Delivery:PermitLimit`, `Delivery:MaxKeyLimit`, `Management:PermitLimit`, `Auth:PermitLimit`, `PreAuth:PermitLimit`, `Health:PermitLimit` |
| `FluentCms:Limits` | `MaxBodySize` |
| `FluentCms:Delivery:Cache` | `CacheControl`, `OutputCache`, `OutputCacheDuration` (see [Delivery caching](#delivery-caching)) |
| `FluentCms:OpenApi` | `Enabled` (see [OpenAPI and the Scalar UI](#openapi-and-the-scalar-ui)) |
| `FluentCms:Query` | `DefaultPageSize`, `MaxPageSize`, `MaxPopulateDepth`, `MaxFilterNodes`, `MaxPopulatedEntries` |
| `FluentCms:Jobs` | the job runner: `Queues`, `PollInterval`, `LeaseDuration`, retries, retention |
| `FluentCms:Events:Outbox` | the outbox: `BatchSize`, `MaxAttempts`, `LeaseDuration`, retries, retention |
| `FluentCms:Activity` | the activity log: `Enabled`, `Retention`, `SiteRetention`, `PruneSchedule` |
| `FluentCms:Health` | `JobRunnerHeartbeatTimeout` |

A key the configuration sets wins over what the host set in code inside `AddFluentCms`; a key it leaves out keeps the code's value. The options are checked when the host starts, and an invalid value stops it with an `FCMS` code and the key, for example `FCMS1101: FluentCms:Query:MaxPageSize: The field MaxPageSize must be between 1 and 2147483647.`

A CORS origin is an `http` or `https` origin with no path, such as `https://app.example.com`. The lists are empty by default, which allows no browser origin, and a wildcard is refused.

## Health and telemetry

`/health/live` answers while the process runs, and `/health/ready` also needs the database and the job runner. Both need no authentication, answer 200 or 503, and print the status and nothing else. `GET /api/management/v1/system/health` lists every check with its duration and needs the `system.read` permission.

The probes stay reachable without credentials, because the image's `HEALTHCHECK`, a load balancer and an orchestrator call them, so they are kept cheap and bounded. An API key sent to them is never looked up, and each client address gets `FluentCms:RateLimits:Health:PermitLimit` requests a minute (1200), counted before authentication and apart from the coarse limit (see [rate limits](#rate-limits)). That is far above what an orchestrator sends one instance, even with several probes every second; raise it only when many probers share one address, such as health checks that reach the instance through a proxy without forwarded headers. Where the network allows it, still let only the orchestrator and the load balancer reach `/health`, for example with a proxy rule or a network policy.

`FluentCms:Health:JobRunnerHeartbeatTimeout` is how old the last beat of the job runner may be before `/health/ready` is unhealthy. The runner beats every `FluentCms:Jobs:MaintenanceInterval` (15 seconds), so keep this well above it. The default is `00:02:00`. An instance that runs no jobs leaves the runner out of the check with the infinite value, which as a `TimeSpan` string is `-00:00:00.001` (`Timeout.InfiniteTimeSpan` in code).

FluentCMS exports nothing. To send traces and metrics to your own tools, subscribe your OpenTelemetry setup to the source and the meter both named `FluentCMS`, with `AddSource("FluentCMS")` and `AddMeter("FluentCMS")`.

## Security defaults

Cookie authentication schemes and the antiforgery cookie are `Secure` and `HttpOnly`, and authentication cookies are `SameSite=Lax`. A host that configures a cookie after `AddFluentCms` overrides this.

## Rate limits

ASP.NET Core's rate limiter runs three policies, each a fixed window of one minute. The groups carry their policy as endpoint metadata:

| Policy | Group | Partitioned by | Default per minute |
| --- | --- | --- | --- |
| `delivery` | `/api/delivery/v1` and the groups plugins map with `MapGroup` | the API key's `sub`, or the client address | 600 |
| `management` | `/api/management/v1` | the principal's `sub`, or the client address when there is none | 300 |
| `auth` | `/connect`, `/account`, checked by path before authentication | the client address | 20 |
| coarse (`PreAuth`) | every FluentCMS route but `/health`, plugin routes included, before authentication | the client address | 6000 |
| `Health` | `/health`, before authentication, apart from the coarse limit | the client address | 1200 |

An API key whose principal has an `fcms_rate_limit` claim gets that many requests per minute instead of the `delivery` default, at most `Delivery:MaxKeyLimit` (5000), which must not be below `Delivery:PermitLimit` nor above `PreAuth:PermitLimit`, the coarse limit that holds every caller behind one address together. `/health` has no policy of the endpoints, only its own limit.

The coarse limit is checked before authentication on every endpoint that `MapFluentCms()` maps, those of plugins included, and on every path under the groups of core, so it also covers requests that no endpoint answers, such as `/connect/token` from the authorization server. A flood of API keys sent to a plugin's route is turned away there too, before any key is looked up. It adds no `RateLimit-*` headers, and neither does the `Health` limit; a refusal is the same 429 problem. The strict `auth` limit is checked there too, for every path under `/connect` and `/account`, because the authorization server answers `/connect/token`, `/connect/introspect` and `/connect/revoke` without routing; the endpoints of those groups do not count a request a second time.

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

## OpenAPI and the Scalar UI

`MapFluentCmsOpenApi()` maps the OpenAPI 3.1 document of the deployment at `/openapi/v1.json` and the Scalar UI at `/scalar`:

```csharp
app.UseFluentCms();
app.MapFluentCms();
app.MapFluentCmsOpenApi();
```

They are mapped in Development only, because the document names every content type, field, component and block of the schema to whoever can reach it, whatever that caller may read. `FluentCms:OpenApi:Enabled = true` maps them in any environment, and `false` leaves them out in Development too. The builder the method returns applies conventions to both, so a host can require a policy of its own, as in `app.MapFluentCmsOpenApi().RequireAuthorization("docs")`. A browser opening the page sends no bearer token or API key, so such a policy needs a credential the browser carries, such as a cookie of the host's.

The document holds the Delivery and Management routes and none of the host's. A content route is written once per content type, such as `/api/delivery/v1/article` and `/api/management/v1/content/article/{id}/publish`, with the type's `entry-{type}` schema for what it reads and `input-{type}` for what it writes. Components and blocks are `component-{name}` and `block-{name}`, and a block list is a `oneOf` of its blocks with `type` as the discriminator. A field is described by its field type's fragment (`IFieldType.OpenApiSchema`), so a plugin's field type is described the way it says. The `filter` parameter of a type (`style: deepObject`) lists the fields it can filter with their operators, and `sort` lists its sort keys. The security schemes are `oauth2`, with the authorization code flow and PKCE against `/connect/authorize` and `/connect/token` and client credentials, and `apiKey`, the `X-Api-Key` header. Examples come from `cms.OpenApi.AddExample<T>(json)`, which the Blog sample calls for its types. The other routes describe their bodies, and their query parameters with their types, defaults and limits.

The server of the document is the request's origin and path base, and so are the URLs of the authorization server. The routes are not held to `FluentCms:ReverseProxy:PathBase` (see [reverse proxy](reverse-proxy.md)). The document is the one named `v1` of `Microsoft.AspNetCore.OpenApi`, so a host that documents its own endpoints adds a document of another name, such as `AddOpenApi("host")`.

## Startup report and diagnostics

Once the migrations have run, the host logs one `StartupReport` (event id 1100, Information) and a warning per insecure default (event id 1101). The report is a public type of the SDK (`FluentCMS.Diagnostics.StartupReport`). It names the environment, the FluentCMS and data provider versions, the FluentCMS assemblies in the process, the plugins, the [provider slots](#provider-slots) and the implementation each resolves to, the pending migrations of the system, plugin and content streams, the schema hash in code against the one last applied, and the insecure defaults: the refusals of the production checks below (a generated signing key even in an environment that does not enforce it), a generated signing key that `AllowGeneratedSigningKey` accepted, the key ring stored unencrypted where it is not refused (`data-protection-keys-unencrypted`), the Development email sender, the HTTPS redirect being off in Production, `AllowedHosts` left at `*` or unset in Production, `FluentCms:PublicOrigin` unset in Production and accepted with `AllowRequestOrigin` (`public-origin-unset`), schema sync running outside Development (see [content migrations](content-migrations.md#schema-sync)), forwarded headers trusted from every address, and those a plugin reports through `IInsecureDefaultSource`, such as `webhooks-private-networks`. It holds no connection string, key or password. If the report cannot be made, the log says so (event id 1102, with the scrubbed message) and the report is reduced to what needs no database, with `failure` naming the exception type; a request that arrives before any report exists gets a 503 (`FCMS3106`).

`GET /api/management/v1/system/diagnostics` returns the same report as JSON. It needs the instance-wide permission `system.read` (a Super Admin has it), which a role of one site, a grant limited to sites and a key bound to sites do not reach, and which a Delivery or Preview key never gets because it lacks the management scope.

## Production refusals

In the Production environment some settings that are fine in development stop the host before it touches the database. Each refusal names its setting and the key that lifts it, and when several apply the host names them all at once:

| Code | Refusal | Fixed by | Lifted by |
| --- | --- | --- | --- |
| `FCMS1102` | Tokens are signed with a generated key, because no signing certificate is configured | `FluentCms:Security:SigningCertificate` ([authorization server](#authorization-server)) | `FluentCms:Security:AllowGeneratedSigningKey = true` |
| `FCMS1105` | Links in emails follow the request's `Host`, because `FluentCms:PublicOrigin` is not set | `FluentCms:PublicOrigin` ([public origin](#public-origin)) | `FluentCms:Security:AllowRequestOrigin = true` |
| `FCMS1106` | The Data Protection key ring is stored unencrypted, because no XML encryptor is registered | an encryptor ([authorization server](#authorization-server)) | `FluentCms:Security:AllowUnencryptedKeyRing = true` |

A setting that is lifted rather than fixed stays in the startup report, as `generated-signing-key`, `public-origin-unset` or `data-protection-keys-unencrypted`. Other environments start with all three, and their report is as it was.

## API keys

`AddFluentCms()` registers the `ApiKey` authentication scheme. It reads a key from `Authorization: ApiKey <key>` or from the `X-Api-Key` header, and leaves a request with neither to the other schemes. A request with a key and any other credential is refused. The FluentCMS endpoints authenticate with the `FluentCms` scheme, which uses `ApiKey` when the request carries a key and otherwise the bearer scheme of the authorization server. It never uses the host's default scheme, so a cookie is not a credential for the APIs. The host calls `app.UseAuthentication()` before `MapFluentCms()`. A key's `last_used_at` is written at most once every five minutes.

## Authorization server

`AddFluentCms()` adds the OpenIddict server at `/connect/authorize`, `/connect/token`, `/connect/userinfo`, `/connect/introspect`, `/connect/revoke` and `/connect/logout`, with discovery at `/.well-known/openid-configuration` and the keys at `/.well-known/jwks`. It answers HTTPS only; behind a proxy that ends TLS, forward the scheme. Under `FluentCms:ReverseProxy:PathBase` these endpoints, the issuer and the discovery URLs carry the path base, and the endpoints are not answered without it. The issuer and the discovery URLs are built from the [public origin](#public-origin) when it is set, and from the request otherwise. The flows are the authorization code flow with PKCE (`S256`, for every client), refresh tokens that rotate and are refused once used, and client credentials. Access tokens are signed JWTs with the audience `fluentcms:management` and are valid for an hour.

A bearer token is read from the `Authorization` header only, never from the query string or a form. Logout takes a POST, or a GET that carries the session's `id_token_hint`; any other GET goes to the logout page, which asks the user and posts it. The token endpoint refuses a code or refresh token of a user who is deactivated, locked out or whose password or security stamp changed since. A recurring job, `oidc.prune` (daily at 04:15 UTC, scheduled a minute after the host starts), removes tokens and sessions that can no longer be used once they are 14 days old.

Clients are registered when the host starts, from `FluentCms:Clients:Applications` or `cms.Clients.Applications` in code, keyed by client id. A client with `RedirectUris` signs users in; one with a `Secret` is confidential and may sign in as a service account with client credentials, holding the instance-wide roles `Roles` names. `Scopes` are what it may ask for: `openid`, `profile`, `email`, `offline_access`, `draft` and `management`. Requests with a token of the client named by `AdminClientId` (`fluentcms-admin` by default) run with the `Admin` origin. A client that was registered from the options and is no longer listed is deleted, with its sessions and tokens, at the next start, with a warning that names it. When the options list no client at all, which is more likely a configuration that failed to load, nothing is deleted and a warning names the clients kept. A client registered some other way is left as it is. The authorize endpoint needs the user signed in on the hosted pages and sends anyone else to `/account/login`. A client with `RequireConsent` is a third-party one: the user allows it on the consent page before it gets a code, once for the scopes it asks; the clients of the host, such as the admin, sign in without that page.

Tokens are signed with `SigningCertificate` (a PKCS#12 file with an RSA key) when it is set. Otherwise the server generates an RSA key and keeps it in `cms_signing_key`, protected with Data Protection, so every instance and every restart uses the same one; Production refuses that unless `AllowGeneratedSigningKey` is set. The key that encrypts codes and refresh tokens is always kept there. The Data Protection key ring is kept in `cms_data_protection_key` under the application name `FluentCMS`, unless the host persists it elsewhere, and is loaded on its first use rather than when the host starts. A new key is made under the distributed lock (`IDistributedLock`, kept in `cms_lock` with no configuration), so instances that start together on an empty database share the first one.

The key ring is not encrypted at rest unless the host adds an encryptor, and anyone who can read the database (a backup, a read-only user) can then read it and forge the sign-in cookie the authorize endpoint accepts. Production refuses to start without one (`FCMS1106`) unless `FluentCms:Security:AllowUnencryptedKeyRing` is true. Add it with Data Protection's own builder, before `AddFluentCms` so that the ring still loads on its first use, with a certificate from the host's secret store (an RSA key, with its private key):

```csharp
builder.Services.AddDataProtection().ProtectKeysWithCertificate(certificate);
```

or with a key in Azure Key Vault, through `Azure.Extensions.AspNetCore.DataProtection.Keys` and `Azure.Identity`:

```csharp
builder.Services.AddDataProtection()
    .ProtectKeysWithAzureKeyVault(new Uri("https://my-vault.vault.azure.net/keys/fluentcms-ring"), new DefaultAzureCredential());
```

Any other `IXmlEncryptor`, such as DPAPI on Windows, counts the same. Keys written before the encryptor was added stay readable. When the certificate is replaced, pass the old one to `UnprotectKeysWithAnyCertificate` until the keys it protected have expired. The [Docker image](docker.md#before-production) reads a certificate from `FluentCms:DataProtection:Certificate:Path` and `Password` instead. Keep `cms_data_protection_key`, `cms_signing_key` and the `cms_oidc_*` tables out of exports, and treat a database backup as holding secrets.

OpenIddict logs whole tokens at the `Trace` level. Do not set the `OpenIddict` log category to `Trace` in production.

## Public origin

`FluentCms:PublicOrigin`, or `cms.PublicOrigin` in code, is the address users reach the instance at, such as `https://cms.example.com`. Password reset and invitation links, the issuer, the URLs of the discovery document, and the server and authorization server URLs of the OpenAPI document are built from it, so the `Host` a request names, forged or not, plays no part in them. It is an absolute `https` URL (`http` too in Development) with no user, query or fragment. Its path is empty or the path base: with `FluentCms:ReverseProxy:PathBase` set to `/cms`, `https://cms.example.com` and `https://cms.example.com/cms` both give links under `https://cms.example.com/cms`. Another path, or a path without a path base, stops the host with `FCMS1101`. Tokens carry the issuer, so setting the origin or changing it refuses the access tokens, codes and refresh tokens issued before: users and clients sign in again.

Without it, those links and URLs are built from the request's scheme, host and path base, so a forged `Host` gets a reset link to the attacker's host emailed to a real user. Production refuses to start without it (`FCMS1105`) unless `FluentCms:Security:AllowRequestOrigin` is true, which is for an instance whose `AllowedHosts` or proxy lets through only its own host names; the startup report then lists `public-origin-unset`. Set `AllowedHosts` as well, since the instance still answers any host that host filtering lets through.

## Hosted pages

The pages of the authorization server are Razor Pages under `/account`: login, logout, consent, two-factor, recovery code and two-factor setup, forgot and reset password, accept invitation, change password, external login and error. `AddFluentCms()` registers Razor Pages and `MapFluentCms()` maps them with `MapRazorPages()`, so a host adds nothing. That maps the host's own Razor Pages too, and a host that maps them itself still gets each page once. They carry the `/account` headers and the `auth` rate limit per client address, run no script, and send a `Content-Security-Policy` that allows inline styles, and images from the host or over https only.

Branding is the settings area `branding` (`BrandingSettings` in `FluentCMS.Core.Api.Account`): `Name` (default `FluentCMS`), `LogoUrl` (an https URL or a path of the host, such as `/media/logo.svg`), and the hex colours `PrimaryColor`, `BackgroundColor` and `TextColor`. The pages read the instance defaults, which need `settings.branding.manage` to change. To change more than that, override a page or the layout: add a Razor page at the same path to the host project, such as `Areas/FluentCms/Pages/Account/Login.cshtml` or `Areas/FluentCms/Pages/Shared/_Layout.cshtml`, with `@model FluentCMS.Core.Api.Account.LoginModel` for a page, so that it keeps the page's behaviour. A page of the host wins over the one of the same path in FluentCMS.

A user who forgets the password gets a link by email that works once and for `FluentCms:Users:PasswordResetLifetime`. The page answers the same whether or not the address belongs to a user. An invitation links to `/account/accept-invitation`. Both links are built from the [public origin](#public-origin); without it they follow the request's scheme, host and path base, and a request with a forged `Host` header that `AllowedHosts` lets through gets a link to that host sent to a real user. Logins, failed and successful, are `user.login_failed` and `user.login_succeeded` activity rows with the client address.

A user who lost the authenticator app and every recovery code is reset by an administrator with `POST /api/management/v1/users/{id}/two-factor/reset`. The administrator needs `users.manage`, every permission of the user's roles, and a sign-in with a second factor, so an API key cannot do it. The reset turns two-factor off, ends the user's sessions, and is a `user.two_factor_reset` activity row; a role that requires two-factor asks the user to enrol again at the next sign-in.

The instance keeps at least one active user with every permission, such as a Super Admin: deactivating that user, taking the role from it, or editing its role so that it grants less is refused with `FCMS3052`, also for an API key, and the attempt is a `user.change_refused` or `role.change_refused` activity row. Give the role to another user first.

## Single sign-on

Admin users can sign in with an external OpenID Connect provider on the hosted login page, which shows a button for each provider of `FluentCms:Sso:Providers`. A provider's name is 1 to 64 letters, digits, `-` or `_`; register `https://{host}{path base}/account/sso/{name}` as its redirect URI at the provider. With a [public origin](#public-origin), that URI is built from it, such as `https://cms.example.com/account/sso/corp`; without one, from the request. Microsoft Entra ID, Okta, Keycloak and any other OpenID Connect provider use the same handler, and `Preset` only builds the authority:

| `Preset` | Settings | Authority |
| --- | --- | --- |
| `Oidc` (default) | `Authority` | as given |
| `EntraId` | `TenantId`, one tenant (`common`, `organizations` and `consumers` are refused) | `https://login.microsoftonline.com/{TenantId}/v2.0` |
| `Okta` | `Domain`, and `AuthorizationServerId` for a custom authorization server | `https://{Domain}` or `https://{Domain}/oauth2/{AuthorizationServerId}` |
| `Keycloak` | `BaseUrl`, `Realm` | `{BaseUrl}/realms/{Realm}` |

`Authority` overrides what a preset builds. The sign-in uses the code flow with PKCE, sends `ClientSecret` when it is set, and asks for `openid profile email` and the `Scopes` listed. The authority must be https unless `RequireHttpsMetadata` is off. A host tunes a provider further with `services.Configure<OpenIdConnectOptions>("{name}", ...)`. With environment variables, as in the reference host:

```bash
FluentCms__Sso__Providers__corp__Preset=EntraId
FluentCms__Sso__Providers__corp__TenantId=00000000-0000-0000-0000-000000000000
FluentCms__Sso__Providers__corp__ClientId=fluentcms
FluentCms__Sso__Providers__corp__ClientSecret=...
FluentCms__Sso__Providers__corp__DefaultRole=Author
FluentCms__Sso__Providers__corp__RoleMappings__0__Group=00000000-0000-0000-0000-000000000001
FluentCms__Sso__Providers__corp__RoleMappings__0__Role=Editor
```

A first sign-in of someone with no user creates one when `AutoProvision` is on (the default): its user name and email are the token's `EmailClaim` (`email`), it has no password, its external login is kept in `cms_user_login`, and it gets `DefaultRole` and the roles its groups map to. A role is named by its id or by the exact name of an instance-wide role; a role that does not exist is not given and is logged with `FCMS3071`. `RoleMappings` gives the `Role` of each `Group` found in the token's `GroupsClaim` (`groups`); no group gives a role by its name, so Super Admin is given only by a mapping or a default role that names it. At every later sign-in the mapped roles follow the groups: a mapped role whose group is gone is taken away, while the default role and roles given by hand stay. A token with no groups claim at all takes the mapped roles away and logs a warning; with Entra ID, a user in too many groups gets no `groups` claim, so map app roles or assign groups to the application. Groups that would take Super Admin from the last active user who has it change no role and refuse the sign-in, with a warning that names `FCMS3052`. A token without an email address or with `email_verified` false (`FCMS3072`), or whose address already belongs to a user not linked to the provider (`FCMS3073`), creates no user, since an existing account is not taken over by its address. `LinkExistingUsers` (off by default) changes that for one provider: a first sign-in through it whose address belongs to a user with no login of that provider links that user, when the token says `email_verified` is true. A provider that sends no `email_verified` links no one, so turn it on only for a provider that verifies the addresses it asserts. Sign-ins and refusals are `user.login_succeeded` and `user.login_failed` activity rows with the method `external`.

A provider's `amr` claim does not count as the second factor unless `TrustMfaClaim` is set for it. When it is, a token whose `amr` holds `mfa` signs in as if the user had given the local second factor. Otherwise a user whose role requires two-factor gives the local one after the provider, or sets it up on first use. Set `TrustMfaClaim` only for a provider that enforces multi-factor sign-in itself.

The invitation page offers a button per provider next to the password form. Accepting through a provider links it to the new user when the provider's address for the person is the invitation's and the token does not say `email_verified` is false; the user is named by that address, gets the invitation's roles and is signed in. `DisableLocalLogin` hides the password form and the forgotten-password link, there and on the login page, and refuses every password sign-in, so admin users sign in through a provider only, and invited users accept through one. It needs at least one provider. Without `DisableLocalLogin`, a user created by a provider can still get a password through a reset link.

A user's external logins are listed with `GET /api/management/v1/users/{id}/logins` (`users.manage`) or `GET /api/management/v1/me/logins`, and removed with `DELETE` on `.../logins/{provider}`. Removing a login ends the user's sessions, and the last way to sign in is refused (`FCMS3090`): a login counts while its provider is configured, a password while local login is on. Links and removals are `user.login_linked` and `user.login_unlinked` activity rows.

## Email

Invitations and password resets go through `IEmailSender`. In Development the default writes each message, link included, to the log and the startup report lists `development-email-sender`; anywhere else it refuses to send (`FCMS3008`) until a sender is set up. Setting `FluentCms:Email:Smtp:Host` sends through that SMTP server in every environment, Development included:

| Key | Default | Meaning |
| --- | --- | --- |
| `Smtp:Host` | none | The SMTP server. Without it, the defaults above apply. |
| `Smtp:Port` | `587` | Its port. |
| `Smtp:Security` | `StartTls` | `StartTls` refuses a server that does not offer TLS; `SslOnConnect` is TLS from the first byte, as on port 465; `None` sends in the clear and is for a relay on a trusted network only. |
| `Smtp:UserName`, `Smtp:Password` | none | The account to sign in with. Without a user name the server is not asked to authenticate. Keep the password in a secret store. |
| `Smtp:From` | none, required with a host | The sender, such as `cms@example.com` or `FluentCMS <cms@example.com>`. |

A missing or invalid `From` stops the host with `FCMS1101`. A host or a plugin that registers its own `IEmailSender`, or chooses one through the [provider slots](#provider-slots), replaces all of this.

## SQLite

`cms.UseSqlite(connectionString)` opens a database file in WAL mode, where readers and the writer do not block each other, and gives every connection `synchronous = NORMAL` and a busy timeout of 5 seconds. While the database is open, SQLite keeps two more files beside it, `{name}-wal` and `{name}-shm`. Keep them with the database, and copy all three with the host stopped, since a copy of the database alone misses the commits still in `-wal`. Put the database on a local disk: WAL needs memory shared by every process that opens the file, which a network share such as NFS, SMB or Azure Files does not give, and SQLite's locks are not reliable there either. A file that cannot change mode keeps the rollback journal and `synchronous = FULL`. An in-memory database is left as it is.

To keep the rollback journal, pass `writeAheadLog: false`. Every connection then gets SQLite's defaults, and the file keeps the journal mode it has. EF creates a new database in WAL mode, so run `PRAGMA journal_mode = DELETE` on it once to go back.

```csharp
builder.Services.AddFluentCms(cms => cms.UseSqlite(builder.Configuration.GetConnectionString("cms"), writeAheadLog: false));
```

## Provider slots

A provider slot is a service with one implementation that core fills with a default and that the host or a plugin replaces:

| Slot | Default |
| --- | --- |
| `IEmailSender` | SMTP, the log or a refusal, as in [email](#email) |
| `IDistributedLock` | a lease in the `cms_lock` table |
| `IDistributedCache` | the framework's in-memory cache, one per instance |

Choose an implementation with `cms.Providers.Use<TSlot, TImplementation>()`, which registers it as a singleton; a plugin calls the same `Use` on `plugin.Providers` in its `ConfigureServices`:

```csharp
builder.Services.Configure<RedisCacheOptions>(options => options.Configuration = "redis:6379");
builder.Services.AddFluentCms(cms =>
{
    cms.UseSqlite(builder.Configuration.GetConnectionString("cms"));
    cms.Providers.Use<IDistributedCache, RedisCache>();
});
```

What fills a slot is settled when `AddFluentCms()` ends, in this order:

1. the host's `Use`; of two for one slot, the last;
2. a plugin's `Use`; two plugins that replace one slot stop the host with `FCMS1540`, which names both, unless the host chooses with its own `Use`;
3. a service registered for the slot directly, by the host before `AddFluentCms()` or inside it, or by a plugin;
4. the default.

A `Use` takes out every other registration of its slot, so it wins whatever order the calls and the registrations come in. A registration made after `AddFluentCms()` comes last and wins, as it would for any service. A `Use` of a type that is not a slot stops the host with `FCMS1541`, one of an abstract class or an interface with `FCMS1542`, and one made after `AddFluentCms()` has settled the slots with `FCMS1543`. The startup report and `/system/diagnostics` list each slot under `providerSlots` with the type in use and where it came from (`default`, `host`, `plugin` with the plugin's name, or `services`).

## Webhooks

The webhooks plugin (`FluentCMS.Plugins.Webhooks`, which the reference host adds) sends signed deliveries of content and cache events to the webhooks of a site, under `/api/management/v1/webhooks` with `webhooks.manage`. Its options are in `FluentCms:Webhooks`:

| Key | Default | Meaning |
| --- | --- | --- |
| `AllowPrivateNetworks` | `false` | Lets deliveries reach loopback, private, link-local and the other addresses that are not public. The startup report lists `webhooks-private-networks` while it is on in Production. Turn it on only when every receiver is internal. |
| `Timeout` | `00:00:10` | How long an attempt waits for the receiver's answer. |
| `Retention` | `30.00:00:00` | How long the delivery log keeps an attempt. |
| `SecretOverlap` | `1.00:00:00` | How long the secret a rotation replaces keeps signing beside the new one. `00:00:00` ends it at the rotation. |

A target URL uses `https` outside Development. Deliveries do not follow redirects and do not go through a system proxy. A receiver checks `X-FluentCMS-Signature: t=<unix>,v1=<hex>` by computing HMAC-SHA256 with the webhook's secret over `t + "." + body` and accepting any `v1`, of which there are two while a rotated secret overlaps, refuses a `t` more than five minutes old, and ignores a repeat by `X-FluentCMS-Delivery`. A webhook's header values are secrets: they are stored with Data Protection, like the secret, and no answer returns them. The plugin's [README](../src/plugins/FluentCMS.Plugins.Webhooks/README.md#receivers) has the receiver's code.
