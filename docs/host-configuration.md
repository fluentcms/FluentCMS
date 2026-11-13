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

`UseFluentCms()` puts the request pipeline in place, in the order it works in: what `FluentCms:ReverseProxy` asks for (forwarded headers, the path base, HSTS and the HTTPS redirect), the exception handler (problem details), routing, security headers, CORS, the gate that holds FluentCMS routes to the path base, a coarse limit per client address, the request limits, authentication, the rate limiter of the endpoints, authorization and the output cache. Call it once, before `MapFluentCms()`, which needs it (the endpoints carry CORS and rate limit metadata that only these middleware read). It includes `UseExceptionHandler()`, `UseRouting()`, `UseAuthentication()`, `UseAuthorization()` and `UseOutputCache()`, so a host does not add them again; a host that still calls `UseAuthorization()` after it runs its policies twice, which changes nothing but the cost. The order matters: CORS comes before the limiters so that a 429 still carries CORS headers, the coarse limit comes before authentication so that a flood is turned away before it reaches a key lookup or the token endpoint, authentication comes before the endpoint limiter because that partitions by API key or principal, and authorization comes before the output cache, so that a policy put on `MapFluentCms()`, as in `app.MapFluentCms().RequireAuthorization("cms")`, holds for a cached answer too. `MapFluentCms()` throws `FCMS1104` at startup when `UseFluentCms()` was not called.

Response compression goes before `UseFluentCms()`, so that the output cache keeps the body as the endpoint wrote it and each answer is encoded for the request it goes to:

```csharp
builder.Services.AddResponseCompression();

var app = builder.Build();
app.UseResponseCompression();
app.UseFluentCms();
app.MapFluentCms();
```

Compression after `UseFluentCms()` would sit inside the output cache, which does not vary on `Accept-Encoding`, so a body compressed for one client would go to every other; FluentCMS does not keep such a copy, and every compressed read then runs the endpoint.

## Requests

An endpoint runs as the principal of the `FluentCms` authentication scheme, which `AddFluentCms()` registers: the API key handler when a key is sent, otherwise the bearer scheme set in `FluentCmsAuthenticationOptions.BearerScheme`, and anonymous when neither applies. The handler writes `sub` (the id), `name`, `fcms_kind` (a `PrincipalKind` name), `roles` and `scope` (both space-separated), and `fcms_sites` for an API key bound to sites. A request without an authenticated user runs as the anonymous principal, which holds the Public role of its site, so anonymous Delivery access is what that role grants. A principal without a usable `fcms_kind` or `sub` is refused with 401.

The site is the key's binding, then the `X-Cms-Site` header, then the `site` parameter (an id or a slug), then the host name of the request when the multi-site plugin is added (see Multi-site), then the default site. A key bound to one site reads that site. A key bound to several reads the one the request names, or the site of the host name, the default site when no site has it, when that site is among them, and is otherwise refused with `FCMS3122`. A request that names a site outside the binding, or one that does not exist, is refused with `FCMS3121` (403). A header and a parameter that name different sites are refused. The locale is the `locale` parameter, then the `X-Cms-Locale` header, then the site's default; `locale=all` returns every locale of an entry as a map.

## Options

Everything is read from the `FluentCms` section of the host's configuration, so any configuration provider works. Environment variables use `__` for `:`, as in `FluentCms__Database__MigrateOnStartup=false`.

| Section | Holds |
| --- | --- |
| `FluentCms:Database` | `MigrateOnStartup`, `SchemaSync` (see [content migrations](content-migrations.md)) |
| `FluentCms:Security` | `AllowGeneratedSigningKey`, `AllowRequestOrigin`, `AllowUnencryptedKeyRing` (see [Production refusals](#production-refusals)), `SigningCertificate:Path`, `SigningCertificate:Password`, `Cors:Delivery:AllowedOrigins`, `Cors:Management:AllowedOrigins`, `Cors:Members:AllowedOrigins`, `ReferrerPolicy`, `FrameAncestors` |
| `FluentCms:Users` | `Password`, `Lockout`, `InvitationLifetime` (7 days), `PasswordResetLifetime` (1 hour) (see [hosted pages](#hosted-pages)) |
| `FluentCms:Email` | `Smtp:Host`, `Smtp:Port` (587), `Smtp:Security` (`StartTls`, `SslOnConnect` or `None`), `Smtp:UserName`, `Smtp:Password`, `Smtp:From` (see [email](#email)) |
| `FluentCms:Clients` | `AdminClientId`, `AccessTokenLifetime`, `RefreshTokenLifetime`, `SignInCookieLifetime`, `AbsoluteSessionLifetime`, and `Applications:{clientId}` with `DisplayName`, `Secret`, `RedirectUris`, `PostLogoutRedirectUris`, `Scopes`, `Roles`, `RequireConsent`, `Pool`, `AllowDirectLogin` (see [authorization server](#authorization-server) and [members](#members)) |
| `FluentCms:Sso` | `DisableLocalLogin`, and `Providers:{name}` with `Preset`, `DisplayName`, `Authority`, `TenantId`, `Domain`, `AuthorizationServerId`, `BaseUrl`, `Realm`, `ClientId`, `ClientSecret`, `Scopes`, `AutoProvision`, `RequireVerifiedEmail`, `LinkExistingUsers`, `DefaultRole`, `EmailClaim`, `GroupsClaim`, `RoleMappings`, `TrustMfaClaim`, `RequireHttpsMetadata` (see [single sign-on](#single-sign-on)) |
| `FluentCms:Members` | `AllowRegistration`, `AllowDirectLogin`, `Password`, `Lockout`, `EmailVerificationLifetime` (24 hours), `PasswordResetLifetime` (1 hour), `UnverifiedMemberLifetime` (7 days), `Links:VerifyEmail`, `Links:ResetPassword` (see [members](#members)), and `Providers:{name}` with `Type`, `DisplayName`, `ClientId`, `ClientSecret`, `Scopes`, `Authority`, `RequireHttpsMetadata`, `TeamId`, `KeyId`, `PrivateKey`, `LinkVerifiedEmail` (see [external providers](#member-sign-in-with-an-external-provider)) |
| `FluentCms:PublicOrigin` | the address users reach the instance at, such as `https://cms.example.com` (see [public origin](#public-origin)) |
| `FluentCms:ReverseProxy` | `PathBase`, `ForwardedHeaders`, `HttpsRedirection`, `Hsts` (see [reverse proxy](reverse-proxy.md)) |
| `FluentCms:RateLimits` | `Delivery:PermitLimit`, `Delivery:MaxKeyLimit`, `Management:PermitLimit`, `Auth:PermitLimit`, `PreAuth:PermitLimit`, `Health:PermitLimit` |
| `FluentCms:Limits` | `MaxBodySize` |
| `FluentCms:Delivery:Cache` | `CacheControl`, `OutputCache`, `OutputCacheDuration`, `CacheTagHeader` (see [Delivery caching](#delivery-caching)) |
| `FluentCms:OpenApi` | `Enabled` (see [OpenAPI and the Scalar UI](#openapi-and-the-scalar-ui)) |
| `FluentCms:Query` | `DefaultPageSize`, `MaxPageSize`, `MaxOffset` (10,000 entries a page or offset may skip; read further by cursor), `MaxPopulateDepth`, `MaxFilterNodes`, `MaxPopulatedEntries` |
| `FluentCms:Jobs` | the job runner: `Queues`, `PollInterval`, `LeaseDuration`, retries, retention |
| `FluentCms:Events:Handlers` | the in-process event handlers: `Timeout` (5 seconds), `SlowThreshold` (1 second) (see [event handlers](#event-handlers)) |
| `FluentCms:Events:Outbox` | the outbox: `BatchSize`, `MaxAttempts`, `LeaseDuration`, retries, `OrphanAfter`, `Retention`, `FailedRetention` (off by default), `PruneBatchSize` |
| `FluentCms:Activity` | the activity log: `Enabled`, `Retention`, `SiteRetention`, `PruneSchedule` |
| `FluentCms:Idempotency` | `Window` (24 hours), `InProgressTimeout` (5 minutes) (see [idempotency keys](#idempotency-keys)) |
| `FluentCms:Health` | `JobRunnerHeartbeatTimeout`, `PendingJobAgeLimit` |
| `FluentCms:Transfer` | the bounds of an import: `MaxPackageSize` (1 GiB, the body), `MaxUnpackedSize` (the sum of the sizes the files of a package declare unpacked; `MaxPackageSize` when not set), `MaxEntries` (100,000 files), `MaxJsonSize` (64 MiB for each of `manifest.json`, `schema.json`, `settings.json` and `roles.json`), `MaxReadSize` (the compressed bytes an import reads, a file counting each time it is opened; twice `MaxPackageSize` when not set), `MaxLineLength` (16 MiB for a line of a `.jsonl` file), `MaxReportItems` (1,000 conflicts and errors listed in the report; every item is counted). A package over a bound is refused with `FCMS3200`, and an import that reaches one once it has started stops with it as the last item of its report |

A key the configuration sets wins over what the host set in code inside `AddFluentCms`; a key it leaves out keeps the code's value. The options are checked when the host starts, and an invalid value stops it with an `FCMS` code and the key, for example `FCMS1101: FluentCms:Query:MaxPageSize: The field MaxPageSize must be between 1 and 2147483647.`

A CORS origin is an `http` or `https` origin with no path, such as `https://app.example.com`. The lists are empty by default, which allows no browser origin, and a wildcard is refused.

## Health and telemetry

`/health/live` answers while the process runs, and `/health/ready` also needs the database and the job runner, and with the media plugin its storage. Both need no authentication, answer 200 or 503, and print the status and nothing else. `GET /api/management/v1/system/health` lists every check with its duration and needs the `system.read` permission.

The probes stay reachable without credentials, because the image's `HEALTHCHECK`, a load balancer and an orchestrator call them, so they are kept cheap and bounded. An API key sent to them is never looked up, and each client address gets `FluentCms:RateLimits:Health:PermitLimit` requests a minute (1200), counted before authentication and apart from the coarse limit (see [rate limits](#rate-limits)). That is far above what an orchestrator sends one instance, even with several probes every second; raise it only when many probers share one address, such as health checks that reach the instance through a proxy without forwarded headers. Where the network allows it, still let only the orchestrator and the load balancer reach `/health`, for example with a proxy rule or a network policy.

`FluentCms:Health:JobRunnerHeartbeatTimeout` is how old the last beat of the job runner may be before `/health/ready` is unhealthy. The runner beats every `FluentCms:Jobs:MaintenanceInterval` (15 seconds), so keep this well above it. The default is `00:02:00`. An instance that runs no jobs leaves the runner out of the check with the infinite value, which as a `TimeSpan` string is `-00:00:00.001` (`Timeout.InfiniteTimeSpan` in code).

`FluentCms:Health:PendingJobAgeLimit` is how long the oldest job of a queue that is due and held by no worker may have waited before the `job-runner` check reports Degraded, which keeps the instance in rotation and logs a warning (event 9104). The default is `00:15:00`, and the infinite value reports the ages without a limit. The runner measures the queues on each maintenance turn, over the jobs of every instance; the check carries the ages in its data, and the `fluentcms.job.oldest_pending_age` gauge reports them by queue.

FluentCMS exports nothing. To send traces and metrics to your own tools, subscribe your OpenTelemetry setup to the source and the meter both named `FluentCMS`, with `AddSource("FluentCMS")` and `AddMeter("FluentCMS")`.

### Event handlers

An in-process event handler (`IEventHandler<T>`) runs after the commit, and the request that made the write or the publish waits for it. Each handler gets `FluentCms:Events:Handlers:Timeout` (5 seconds). One that takes longer has its token cancelled, is logged as abandoned (event 9615) and left to end on its own, and the request goes on to the next handler. Handlers run in a scope of their own, not the request's, so one left running cannot disturb the request. A handler that ends after `FluentCms:Events:Handlers:SlowThreshold` (1 second) is logged as slow (event 9616). Every run is recorded in the `fluentcms.event.handler.duration` histogram, by event type, handler and outcome (`succeeded`, `failed` or `timed-out`). Work that may be slow belongs in an outbox handler or a job.

## Security defaults

Cookie authentication schemes and the antiforgery cookie are `Secure` and `HttpOnly`, and authentication cookies are `SameSite=Lax`. A host that configures a cookie after `AddFluentCms` overrides this.

## Rate limits

ASP.NET Core's rate limiter runs three policies, each a token bucket per caller that holds the limit and fills again over a minute. The groups carry their policy as endpoint metadata:

| Policy | Group | Partitioned by | Default per minute |
| --- | --- | --- | --- |
| `delivery` | `/api/delivery/v1` and the groups plugins map with `MapGroup` | the API key's `sub`, or the client address | 600 |
| `management` | `/api/management/v1` | the principal's `sub`, or the client address when there is none | 300 |
| `auth` | `/connect`, `/account`, `/api/members/v1`, checked by path before authentication | the client address | 20 |
| coarse (`PreAuth`) | every FluentCMS route but `/health`, plugin routes included, before authentication | the client address | 6000 |
| `Health` | `/health`, before authentication, apart from the coarse limit | the client address | 1200 |

An API key whose principal has an `fcms_rate_limit` claim gets that many requests per minute instead of the `delivery` default, at most `Delivery:MaxKeyLimit` (5000), which must not be below `Delivery:PermitLimit` nor above `PreAuth:PermitLimit`, the coarse limit that holds every caller behind one address together. `/health` has no policy of the endpoints, only its own limit.

An endpoint a plugin puts under the `caller` policy with `RequireCallerRateLimit()`, such as `/graphql`, serves both kinds of caller: one with the `management` scope is counted as the `management` policy counts it, and any other as `delivery` does, a key's own limit included. Its buckets are apart from those of the REST APIs.

The coarse limit is checked before authentication on every endpoint that `MapFluentCms()` maps, those of plugins included, and on every path under the groups of core, so it also covers requests that no endpoint answers, such as `/connect/token` from the authorization server. A flood of API keys sent to a plugin's route is turned away there too, before any key is looked up. It adds no `RateLimit-*` headers, and neither does the `Health` limit; a refusal is the same 429 problem. The strict `auth` limit is checked there too, for every path under `/connect`, `/account` and `/api/members/v1`, because the authorization server answers `/connect/token`, `/connect/introspect` and `/connect/revoke` without routing; the endpoints of those groups do not count a request a second time.

A caller that has been quiet may send the whole limit at once, and then one request per minute divided by the limit (every tenth of a second at 600), so a burst at the turn of a minute is not let through twice, as a fixed window of one minute would let it. A response carries `RateLimit-Limit`, `RateLimit-Remaining` (the requests that may be sent now) and `RateLimit-Reset` (seconds until the bucket is full again), from the IETF draft. A refused request is a 429 `application/problem+json` with code `FCMS3110` and a `Retry-After` in seconds until the next request is let through.

The key and the principal are those of the `FluentCms` authentication scheme when the host has one (the API key registration adds it, as a policy scheme that forwards to the key scheme when a key is presented and otherwise to the default scheme), so a cookie or token default scheme does not hide a key. Without it they are those of the default scheme.

The client address is the connection's remote address, an IPv4-mapped address counted as its IPv4 address and an IPv6 address by its /64. Behind a proxy, turn on `FluentCms:ReverseProxy:ForwardedHeaders` with the proxies you trust, which `UseFluentCms()` applies before the limiters, or every caller shares the proxy's address.

## CORS

`Cors:Delivery:AllowedOrigins` applies to `/api/delivery/v1`, which allows `GET` and `HEAD`. `Cors:Management:AllowedOrigins` applies to `/api/management/v1` and to `/connect`, so the admin application can sign in from its own origin. `Cors:Members:AllowedOrigins` applies to `/api/members/v1`, which allows `GET`, `HEAD`, `POST`, `PATCH` and `DELETE` with the `Authorization` and `Content-Type` headers and no credentials, and `/connect` answers these origins too, so a front end can sign members in from its own origin. Both allow the `X-Cms-Site` and `X-Cms-Locale` headers, Management also `Idempotency-Key`, and a browser can read `ETag`, `Location`, `Retry-After` and the `RateLimit-*` headers, and `Idempotent-Replayed` on Management. An origin that is not on the list gets no CORS headers, and an empty list, the default, allows none. The lists come from configuration only; a setting that edits them at run time is not part of this release.

## Security headers and request limits

Every FluentCMS response carries `X-Content-Type-Options: nosniff` and `Referrer-Policy` (`FluentCms:Security:ReferrerPolicy`, default `no-referrer`). The `/account` group, which is where the hosted pages are, also carries `Content-Security-Policy: frame-ancestors ...` from `FluentCms:Security:FrameAncestors`, default `'none'`. A FluentCMS response other than a Delivery read that says nothing of caching carries `Cache-Control: no-store`, so a key's secret, recovery codes and `/me` are kept by no browser or proxy. A header that an endpoint sets itself is kept, and `frame-ancestors` is added to an endpoint's own policy when that has none. A host's own endpoints are not touched.

A request to a FluentCMS endpoint, or to any path under its groups such as `/connect/token`, with a body larger than `FluentCms:Limits:MaxBodySize` bytes (default 2 MiB) is refused with 413 and code `FCMS3111`, whether the size is declared or only found out while the body is read. The cap applies before authentication, which is where the authorization server reads its requests. The cap only lowers: a lower limit that the host or Kestrel already set stays, and an endpoint with its own size limit keeps it. The page size, populate depth and filter node caps are the engine's `FluentCms:Query` options; a query over one comes back as a 400 problem with code `FCMS2207`.

## Delivery caching

The options are `FluentCms:Delivery:Cache` (`cms.DeliveryCache` in code). A Delivery response carries an `ETag` and a `Cache-Control`, and varies on `Authorization`, `X-Api-Key`, `X-Cms-Site` and `X-Cms-Locale`. A request with a matching `If-None-Match` gets a 304. There is no `Last-Modified`, because no date in a response moves when an entry leaves a list, a populated entry is unpublished or a role changes what the caller may read, so `If-Modified-Since` is ignored and such a request is answered in full, from the output cache when it holds a copy. `HEAD` is answered with the headers of the `GET`, `Content-Length` included, and no body.

| Key | Default | Meaning |
| --- | --- | --- |
| `CacheControl` | `public, max-age=60, stale-while-revalidate=300` | What browsers and CDNs are told for a published read without credentials. A read with an API key or a token gets the same value as `private`, a draft read gets `no-store`, and an error gets `no-cache,no-store`. |
| `OutputCache` | `true` | Whether the server keeps published reads in the ASP.NET Core output cache. Off, nothing is kept, and no `CacheInvalidatedEvent` is raised unless `CacheTagHeader` is set. |
| `OutputCacheDuration` | `00:01:00` | How long it keeps one, at most. |
| `CacheTagHeader` | unset | The header that carries a published read's cache tags for a CDN, such as `Cache-Tag` or `Surrogate-Key`. Unset or empty, none is sent. A name that is not an HTTP token, or one the response writes itself such as `Cache-Control` or `Vary`, stops the host with `FCMS1101`. |

The server keeps one copy for every request without credentials and one per API key, and keeps nothing for a bearer token, a draft read, credentials it refuses or an error. Publishing, unpublishing, trashing and restoring an entry evict the tags `entry:{id}` and `type:{site}:{type}` (`type:global:{type}` for a global type) through the outbox and raise `CacheInvalidatedEvent` with them, so a CDN can purge the same; a change to a site or its locales evicts `site:{id}` the same way, and so does a change to a role or its grants, since the Public role decides what the shared copy holds and a key's role what its own copy holds: a role of a site evicts that site, and a role of the instance every site. Disabling a plugin in a site evicts the site too, since the answers of its Delivery groups are cached with it. Every read is tagged with its site, its type and every entry in it, populated ones included. A newly published target of a relation shows when the copy expires.

### Purging a CDN by tag

With `CacheTagHeader` set, every published Delivery read, from an API key too, carries its tags in that header: `site:{id}`, `type:{site}:{type}` (`type:global:{type}`) and `entry:{id}` for every entry in the body, and the media route's `site:{id}`, `media:{id}` and `media-presets:{site}`. A draft read carries none. The tags are listed apart by a space for `Surrogate-Key`, as Fastly reads it, and by a comma for any other name, as Cloudflare's `Cache-Tag` and Akamai's `Edge-Cache-Tag` are read. Each `CacheInvalidatedEvent` names tags from the same set, so a CDN that purges by them drops every copy the change made stale. The event is raised while `CacheTagHeader` is set even with `OutputCache` off. CDNs cap the header (16 KB at Cloudflare and Fastly), so a long list page with many populated entries may need a smaller page size. The media plugin reads the option from configuration, as it reads `CacheControl`, so set it there rather than in code when the plugin is added.

The purge goes through the webhooks plugin: a webhook on `cache.invalidated` receives `data.tags`, and a small receiver checks the signature and calls the CDN's purge API with them. With Cloudflare, set `CacheTagHeader` to `Cache-Tag` and have the receiver, a Worker for example, send:

```http
POST https://api.cloudflare.com/client/v4/zones/{zone_id}/purge_cache
Authorization: Bearer {api_token}
Content-Type: application/json

{ "tags": ["entry:0190…", "type:0190…:article"] }
```

Cloudflare takes the `Cache-Tag` header off the response before it reaches a visitor. Fastly's equivalent is `Surrogate-Key` and its purge by key.

`UseFluentCms()` adds the output cache middleware, so a host that caches its own endpoints does not call `UseOutputCache()` again. FluentCMS routes other than Delivery are never cached, even under a base policy of the host's. The store is the [provider slot](#provider-slots) `IOutputCacheStore`, in memory by default, and an eviction reaches only the store of the instance that delivers the outbox event: with several instances on the in-memory store, the others serve their copies until `OutputCacheDuration` runs out. A shared store removes that lag, such as the Redis one of `Microsoft.AspNetCore.OutputCaching.StackExchangeRedis`, which `AddStackExchangeRedisOutputCache()` registers when it is called before `AddFluentCms()`. The key of a copy holds the schema hash, so a shared store never serves an instance a copy made for another schema, during a rolling deployment or after a restart with a new one.

## OpenAPI and the Scalar UI

`MapFluentCmsOpenApi()` maps the OpenAPI 3.1 document of the deployment at `/openapi/v1.json` and the Scalar UI at `/scalar`:

```csharp
app.UseFluentCms();
app.MapFluentCms();
app.MapFluentCmsOpenApi();
```

They are mapped in Development only, because the document names every content type, field, component and block of the schema to whoever can reach it, whatever that caller may read. `FluentCms:OpenApi:Enabled = true` maps them in any environment, and `false` leaves them out in Development too. The builder the method returns applies conventions to both, so a host can require a policy of its own, as in `app.MapFluentCmsOpenApi().RequireAuthorization("docs")`. A browser opening the page sends no bearer token or API key, so such a policy needs a credential the browser carries, such as a cookie of the host's.

Both routes carry the security headers, count against the coarse limit and the Delivery limit of the client address, and never look up an API key sent to them. The document is built once and served from the output cache, which runs after authorization, so a policy on the routes holds for the kept copy too. The Scalar page loads its scripts and styles from beside it, not from a CDN, and its `Content-Security-Policy` allows only those, the inline script that starts it (by a nonce made per request), and requests to its own origin and to `FluentCms:PublicOrigin`. Scalar's CDN fonts, telemetry and agent chat are off.

The document holds the Delivery and Management routes and none of the host's. A content route is written once per content type, such as `/api/delivery/v1/article` and `/api/management/v1/content/article/{id}/publish`, with the type's `entry-{type}` schema for what it reads and `input-{type}` for what it writes. Components and blocks are `component-{name}` and `block-{name}`, and a block list is a `oneOf` of its blocks with `type` as the discriminator. A field is described by its field type's fragment (`IFieldType.OpenApiSchema`), so a plugin's field type is described the way it says. The `filter` parameter of a type (`style: deepObject`) lists the fields it can filter with their operators, and `sort` lists its sort keys. The security schemes are `oauth2`, with the authorization code flow and PKCE against `/connect/authorize` and `/connect/token` and client credentials, and `apiKey`, the `X-Api-Key` header. Examples come from `cms.OpenApi.AddExample<T>(json)`, which the Blog sample calls for its types. The other routes describe their bodies, and their query parameters with their types, defaults and limits.

The server of the document is the request's origin and path base, and so are the URLs of the authorization server. The routes are not held to `FluentCms:ReverseProxy:PathBase` (see [reverse proxy](reverse-proxy.md)). The document is the one named `v1` of `Microsoft.AspNetCore.OpenApi`, so a host that documents its own endpoints adds a document of another name, such as `AddOpenApi("host")`.

## Startup report and diagnostics

Once the migrations have run, the host logs one `StartupReport` (event id 9130, Information) and a warning per insecure default (event id 9131). The report is a public type of the SDK (`FluentCMS.Diagnostics.StartupReport`). It names the environment, the FluentCMS and data provider versions, the FluentCMS assemblies in the process, the plugins, the [provider slots](#provider-slots) and the implementation each resolves to, the pending migrations of the system, plugin and content streams, the schema hash in code against the one last applied, and the insecure defaults: the refusals of the production checks below (a generated signing key even in an environment that does not enforce it), a generated signing key that `AllowGeneratedSigningKey` accepted, the key ring stored unencrypted where it is not refused (`data-protection-keys-unencrypted`), the Development email sender, the HTTPS redirect being off in Production, `AllowedHosts` left at `*` or unset in Production, `FluentCms:PublicOrigin` unset in Production and accepted with `AllowRequestOrigin` (`public-origin-unset`), schema sync running outside Development (see [content migrations](content-migrations.md#schema-sync)), forwarded headers trusted from every address, the direct login of members turned on in Production (`member-direct-login`, see [members](#members)), and those a plugin reports through `IInsecureDefaultSource`, such as `webhooks-private-networks` and `mcp-open-client-registration`. It holds no connection string, key or password. If the report cannot be made, the log says so (event id 9132, with the scrubbed message) and the report is reduced to what needs no database, with `failure` naming the exception type; a request that arrives before any report exists gets a 503 (`FCMS3106`).

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

A wrong, expired or revoked key gets the same 401, `FCMS3016`, so the answer does not tell that a secret was ever a key. The log says why: each refused key is event id 9010 (`ApiKeyRefused`, Information) with its code (`FCMS3016` wrong, `FCMS3017` expired, `FCMS3018` revoked, `FCMS3019` sent with another credential), the client address and the id of the key when one was found, never the key. The meter `FluentCMS` counts them in `fluentcms.api_keys.refused`, tagged `fluentcms.code`; alert on it to see keys being probed. A refused key writes no activity row, since a row per failed key would let a flood of keys write to the database as fast as it is sent; the [rate limit](#rate-limits) before authentication bounds it instead.

A key can be limited to IP addresses and CIDR ranges with `allowedAddresses`, on create or with `PUT /api/management/v1/api-keys/{id}/allowed-addresses`. The address checked is the one the host trusts, so behind a proxy configure the [forwarded headers](reverse-proxy.md) first, or every request comes from the proxy's address. A valid key sent from another address gets `FCMS3016` too; the log says `FCMS3023`, and the activity log has `api_key.address_refused` with the address, at most once per key and address, and for ten addresses per key, every five minutes.

`POST /api/management/v1/api-keys/{id}/rotate` gives a key a new secret and keeps the old one working for an overlap, so clients can change over without a gap. The body `{ "overlapSeconds": n }` asks for a shorter overlap, and `0` ends the old secret at once, as for a leaked key.

| `FluentCms:ApiKeys:` | Default | Meaning |
| --- | --- | --- |
| `RotationOverlap` | `1.00:00:00` | How long the secret a rotation replaces is still accepted beside the new one, at most 30 days. `00:00:00` ends it at the rotation. |

### Preview tokens

`POST /api/management/v1/content/{type}/{id}/preview-token` mints a token that reads the draft of that one entry, optionally of one locale (`{ "minutes", "locale" }`, both optional). The caller needs the `management` scope and the permission to read that draft. A front end sends it as `Authorization: Preview <token>` to `GET /api/delivery/v1/{type}/{id}?status=draft`, and every other read with it is refused with `FCMS3028`. It is signed with the authorization server's key and nothing is stored, so it cannot be revoked: keep the lifetimes short. It stops working at once if its user is deactivated, its key revoked or its role loses the read.

| `FluentCms:PreviewTokens:` | Default | Meaning |
| --- | --- | --- |
| `DefaultLifetime` | `00:10:00` | How long a token lasts when the request names no `minutes`, in whole minutes from one minute to `MaximumLifetime`. |
| `MaximumLifetime` | `01:00:00` | The most `minutes` may ask for, in whole minutes from one minute to one day. |

## Idempotency keys

A client that retries `POST /api/management/v1/content/{type}` or `.../bulk` after a timeout sends the same `Idempotency-Key` (such as a UUID) each time. A repeat of the same request answers the first response, with `Idempotent-Replayed: true`, and creates nothing; the same key with another request is `FCMS3141` (422), and a repeat while the first still runs is `FCMS3142` (409). Keys belong to the caller and the site, and are kept for `FluentCms:Idempotency:Window` (24 hours, at most 30 days); the hourly job `idempotency.retention` removes them. A request whose instance stopped before it answered holds its key for `InProgressTimeout` (5 minutes, at most the window), after which a repeat runs. A refused or failed request keeps nothing, so a retry runs again.

## Authorization server

`AddFluentCms()` adds the OpenIddict server at `/connect/authorize`, `/connect/token`, `/connect/userinfo`, `/connect/introspect`, `/connect/revoke` and `/connect/logout`, with discovery at `/.well-known/openid-configuration` and the keys at `/.well-known/jwks`. It answers HTTPS only; behind a proxy that ends TLS, forward the scheme. Under `FluentCms:ReverseProxy:PathBase` these endpoints, the issuer and the discovery URLs carry the path base, and the endpoints are not answered without it. The issuer and the discovery URLs are built from the [public origin](#public-origin) when it is set, and from the request otherwise. The flows are the authorization code flow with PKCE (`S256`, for every client), refresh tokens that rotate, and client credentials. A used refresh token still works for OpenIddict's 30 seconds of leeway, so two refreshes that cross, as two tabs of one application make, both succeed; used after that, it ends the session and its tokens. Access tokens are signed JWTs with the audience `fluentcms:management`.

Lifetimes are `TimeSpan` values under `FluentCms:Clients`:

| Key | Default | What it is |
| --- | --- | --- |
| `AccessTokenLifetime` | `01:00:00` | How long an access token is valid. |
| `RefreshTokenLifetime` | `14.00:00:00` | How long a refresh token lasts unused; using one gives a new one for as long. |
| `SignInCookieLifetime` | `14.00:00:00` | How long the sign-in on the hosted pages lasts unused; each use slides it on. |
| `AbsoluteSessionLifetime` | unset, no limit | How long a session lasts after the user signed in, however often it is refreshed. Past it the token endpoint refuses the refresh (`invalid_grant`) and the authorize endpoint asks the user to sign in again. |

Each is more than zero and at most 365 days, and `AccessTokenLifetime` is no longer than `RefreshTokenLifetime`; the host stops with `FCMS3021` otherwise. Without `AbsoluteSessionLifetime`, a client that keeps refreshing keeps its session for as long as the user can sign in, so set it where sessions must end, such as `12:00:00` for a working day.

A bearer token is read from the `Authorization` header only, never from the query string or a form. Logout takes the POST of the logout page, which carries its antiforgery token, or a request with a valid `id_token_hint` of the signed-in user; anything else, such as a form of another site that posts to `/connect/logout`, goes to the logout page, which asks the user and posts it. The authorize endpoint asks the user to sign in again on `prompt=login` or when the sign-in is older than the request's `max_age` (with `prompt=none`, it answers `login_required`), and the identity token carries `auth_time`. The token endpoint refuses a code or refresh token of a user who is deactivated, locked out or whose password or security stamp changed since. A recurring job, `oidc.prune` (daily at 04:15 UTC, scheduled a minute after the host starts), removes tokens and sessions that can no longer be used once they are 14 days old.

Clients are registered when the host starts, from `FluentCms:Clients:Applications` or `cms.Clients.Applications` in code, keyed by client id. A client with `RedirectUris` signs users in; one with a `Secret` is confidential and may sign in as a service account with client credentials, holding the instance-wide roles `Roles` names. `Scopes` are what it may ask for: `openid`, `profile`, `email`, `offline_access`, `draft`, `management` and `mcp`, which is the scope of the [MCP server](#mcp). Requests with a token of the client named by `AdminClientId` (`fluentcms-admin` by default) run with the `Admin` origin. A client that was registered from the options and is no longer listed is deleted, with its sessions and tokens, at the next start, with a warning that names it. When the options list no client at all, which is more likely a configuration that failed to load, nothing is deleted and a warning names the clients kept. A client registered some other way is left as it is. The authorize endpoint needs the user signed in on the hosted pages and sends anyone else to `/account/login`. A client with `RequireConsent` is a third-party one: the user allows it on the consent page before it gets a code, once for the scopes it asks; the clients of the host, such as the admin, sign in without that page.

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

A wrong code on the two-factor or the recovery code page counts towards the lockout of `FluentCms:Users:Lockout`, as a wrong password does, and a locked-out user's recovery code is refused and stays unspent.

A user who forgets the password gets a link by email that works once and for `FluentCms:Users:PasswordResetLifetime`. The page answers the same whether or not the address belongs to a user. An invitation links to `/account/accept-invitation`. Both links are built from the [public origin](#public-origin); without it they follow the request's scheme, host and path base, and a request with a forged `Host` header that `AllowedHosts` lets through gets a link to that host sent to a real user. Logins, failed and successful, are `user.login_failed` and `user.login_succeeded` activity rows with the client address.

A user who lost the authenticator app and every recovery code is reset by an administrator with `POST /api/management/v1/users/{id}/two-factor/reset`. The administrator needs `users.manage`, every permission of the user's roles, and a sign-in with a second factor, so an API key cannot do it. The reset turns two-factor off, ends the user's sessions, and is a `user.two_factor_reset` activity row; a role that requires two-factor asks the user to enrol again at the next sign-in.

The instance keeps at least one active user with every permission, such as a Super Admin: deactivating that user, taking the role from it, or editing its role so that it grants less is refused with `FCMS3052`, also for an API key, and the attempt is a `user.change_refused` or `role.change_refused` activity row. Give the role to another user first.

## Single sign-on

Admin users can sign in with an external OpenID Connect provider on the hosted login page, which shows a button for each provider of `FluentCms:Sso:Providers`. A provider's name is 1 to 64 letters, digits, `-` or `_`; register `https://{host}{path base}/account/sso/{name}` as its redirect URI at the provider. With a [public origin](#public-origin), that URI is built from it, such as `https://cms.example.com/account/sso/corp`; without one, from the request. A name is found whatever its case: `Corp` and `corp` in configuration are one provider, and a login kept under one finds its user under the other, so changing only the case of a name keeps its users linked, with the redirect URI registered again in the new case. A name is not that of another authentication scheme in any case, such as `ApiKey`, `Preview`, `FluentCms`, the `member-{name}` of a [member provider](#members) or a scheme the host adds; such a name stops the host with `FCMS3070`, or `FCMS3180` when the member provider's scheme is the one refused. The name is the login provider of its users in `cms_user_login`, so do not rename a provider that has users: their logins would no longer be found. Microsoft Entra ID, Okta, Keycloak and any other OpenID Connect provider use the same handler, and `Preset` only builds the authority:

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

A first sign-in of someone with no user creates one when `AutoProvision` is on (the default): its user name and email are the token's `EmailClaim` (`email`), it has no password, its external login is kept in `cms_user_login`, and it gets `DefaultRole` and the roles its groups map to. A role is named by its id or by the exact name of an instance-wide role; a role that does not exist is not given and is logged with `FCMS3071`. `RoleMappings` gives the `Role` of each `Group` found in the token's `GroupsClaim` (`groups`); no group gives a role by its name, so Super Admin is given only by a mapping or a default role that names it. At every later sign-in the mapped roles follow the groups: a mapped role whose group is gone is taken away, while the default role and roles given by hand stay. A token with no groups claim at all takes the mapped roles away and logs a warning; with Entra ID, a user in too many groups gets no `groups` claim, so map app roles or assign groups to the application. Groups that would take Super Admin from the last active user who has it change no role and refuse the sign-in, with a warning that names `FCMS3052`. A token without an email address, or that does not say `email_verified` is true (`FCMS3072`), or whose address already belongs to a user not linked to the provider (`FCMS3073`), creates no user, since an existing account is not taken over by its address. `RequireVerifiedEmail` (on by default) is what asks for `email_verified` true; turn it off only for a provider that verifies every address it asserts and never sends the claim, and a token that says false is still refused. `LinkExistingUsers` (off by default) changes that for one provider: a first sign-in through it whose address belongs to a user with no login of that provider links that user, when the token says `email_verified` is true. A provider that sends no `email_verified` links no one, so turn it on only for a provider that verifies the addresses it asserts. Sign-ins and refusals are `user.login_succeeded` and `user.login_failed` activity rows with the method `external`.

A provider's `amr` claim does not count as the second factor unless `TrustMfaClaim` is set for it. When it is, a token whose `amr` holds `mfa` signs in as if the user had given the local second factor. Otherwise a user whose role requires two-factor gives the local one after the provider, or sets it up on first use. Set `TrustMfaClaim` only for a provider that enforces multi-factor sign-in itself.

A provider's front-channel logout calls `https://{host}{path base}/account/sso/{name}/signout`; register it at the provider to end the admin session when the user signs out there. It ends a sign-in made through that provider only: a user signed in with a password or another provider stays signed in.

The invitation page offers a button per provider next to the password form. Accepting through a provider links it to the new user when the provider's address for the person is the invitation's and the token does not say `email_verified` is false; the user is named by that address, gets the invitation's roles and is signed in. `DisableLocalLogin` hides the password form and the forgotten-password link, there and on the login page, and refuses every password sign-in, so admin users sign in through a provider only, and invited users accept through one. It needs at least one provider. Without `DisableLocalLogin`, a user created by a provider can still get a password through a reset link.

A user's external logins are listed with `GET /api/management/v1/users/{id}/logins` (`users.manage`) or `GET /api/management/v1/me/logins`, and removed with `DELETE` on `.../logins/{provider}`. Removing a login ends the user's sessions, and the last way to sign in is refused (`FCMS3090`): a login counts while its provider is configured, a password while local login is on. Links and removals are `user.login_linked` and `user.login_unlinked` activity rows.

## Members

Members are the accounts of a site's own visitors, apart from the admin users: their own tables (`cms_member*`), password policy, lockout, sign-in cookie, pages and tokens. The pool is one for the instance, so an address is one member in every site, and the address is the sign-in name. It is always there; what a host decides is under `FluentCms:Members`, or `cms.Members` in code:

| Key | Default | What it is |
| --- | --- | --- |
| `AllowRegistration` | `false` | Lets a visitor register. Off, `POST /api/members/v1/register` and `/account/members/register` answer 404, and members come from the Management API only. |
| `AllowDirectLogin` | `false` | Lets a client that also has `AllowDirectLogin` send a member's address and password to the token endpoint (see below). |
| `Password`, `Lockout` | Identity's, with `Password:RequiredLength` 12 | The policy and the lockout of members, apart from `FluentCms:Users`. A member's lockout is always on. |
| `EmailVerificationLifetime` | `1.00:00:00` | How long the link of a verification mail works. |
| `PasswordResetLifetime` | `01:00:00` | How long the link that sets or resets a password works. |
| `UnverifiedMemberLifetime` | `7.00:00:00` | A member that has not verified its address this long after it was made is removed by a daily job. |
| `Links:VerifyEmail`, `Links:ResetPassword` | unset | URL templates of the front end, see below. |
| `Providers:{name}` | none | The external providers members sign in with, see [below](#member-sign-in-with-an-external-provider). |

A bad value stops the host with `FCMS3160` and the key, and a provider that is not valid with `FCMS3180`.

**Mail and links.** A member's mail (verification, reset, the first password of a member an administrator made, and a note that an address is registered already) goes through `IEmailSender` (see [email](#email)). A link goes to your front end when the template is set: an absolute `http` or `https` URL with `{email}` and `{token}` after the host, such as `https://app.example.com/verify?email={email}&token={token}`; the front end then calls `POST /api/members/v1/verify-email` or `/reset-password` with the two values. Left unset, the link goes to the member page on the [public origin](#public-origin). A link is never built from a request, so with neither a template nor a public origin no mail is sent, the log says so (event id 9071, `FCMS3173`), and creating a member through the Management API is refused with `FCMS3173`. Resending the verification mail and a reset (of a member or an admin user) go out from jobs on the `mail` queue after the answer, so give the queue workers on at least one instance (two by default). An address gets one mail a minute of what its owner asks for and one of what a registration or a provider sign-in sends, kept in `IDistributedCache`: with several instances, register a shared one (see [provider slots](#provider-slots)) so the limit holds across them.

**The API.** `/api/members/v1` takes the `auth` rate limit by client address and answers the browser origins of `FluentCms:Security:Cors:Members:AllowedOrigins`, which `/connect` answers too. `POST /register`, `/resend-verification` and `/forgot-password` answer 202 with no body whether or not the address is a member's. An address that is registered a second time before it is verified keeps the password of neither registration: its owner gets a mail with a link to choose one, and later registrations of it change nothing. `POST /verify-email` and `/reset-password` take the address and the token of the mail; a token works once. With a member's token, `GET` and `PATCH /me` read the member and change its display name, `POST /me/change-password` changes the password, and `GET /me/logins` and `DELETE /me/logins/{provider}` list and remove its external logins. A reset, a change of password and the removal of a login end every session of the member.

**The pages.** `/account/members/login`, `/logout`, `/register`, `/verify-email`, `/forgot-password`, `/reset-password`, `/external-login` and `/logins` look and behave like the [hosted pages](#hosted-pages) and are overridden the same way, such as `Areas/FluentCms/Pages/Account/Members/Login.cshtml` with `@model FluentCMS.Core.Api.Account.MemberLoginModel`. They sign in with a cookie of their own (`.FluentCms.Members`), so an admin session and a member session in one browser neither sign each other in nor out. A member signs in only once its address is verified.

**Clients and tokens.** A client under `FluentCms:Clients:Applications` signs members in when its `Pool` is `Members` (the default is `Admins`). It asks for `openid`, `profile`, `email` and `offline_access` only, names no roles and has no consent step; anything else stops the host with `FCMS3021`. Its users sign in on the member pages with the authorization code flow and PKCE, and its tokens carry the audience `fluentcms:members` and the kind `Member`. A member's token never holds `management`, `draft` or `mcp`: the Management API and `/mcp` refuse it, and on the Delivery API it reads published content only.

```json
{
  "FluentCms": {
    "PublicOrigin": "https://cms.example.com",
    "Members": { "AllowRegistration": true },
    "Clients": {
      "Applications": {
        "shop": {
          "Pool": "Members",
          "RedirectUris": [ "https://shop.example.com/signed-in" ],
          "PostLogoutRedirectUris": [ "https://shop.example.com/" ],
          "Scopes": [ "openid", "profile", "email", "offline_access" ]
        }
      }
    },
    "Security": { "Cors": { "Members": { "AllowedOrigins": [ "https://shop.example.com" ] } } }
  }
}
```

**Direct login.** With `FluentCms:Members:AllowDirectLogin` and `AllowDirectLogin` on the client, both off by default, the client posts `grant_type=member_password` with `client_id`, `username` and `password` to `/connect/token` and gets the member's tokens. The app sees the password, so this is the less secure way in, for first-party apps that cannot open a browser; the startup report lists `member-direct-login` in Production. Every refusal is `invalid_grant` with the same description, a wrong password counts towards the lockout, and the discovery document names the grant only while the option is on.

**What a member reads.** A member holds the Public role of the site, the built-in Member role and the roles of its member groups. A group is a named set of members bound to one role, of the instance or of a site; its name is lower-case letters, digits, `-` and `_`. The groups are read on every request, so a change holds from the next one, and a row rule reads them with `Principal.IsInGroup`. Only the principal a row rule is given carries the groups; in a hook, a job or a plugin's endpoint `IsInGroup` answers false for every name, so do not decide anything by it there.

```csharp
cms.Authorization.AddRowRule<Perk>("perk-tier", ctx => perk => perk.Tier == (ctx.Principal.IsInGroup("gold") ? "gold" : "standard"));
```

**Managing members.** `/api/management/v1/members` and `/member-groups` need the instance-wide permission `members.manage`, which is apart from `users.manage`. An administrator lists members (`search`, `group`, `verified`, `active`), creates one, which mails it a link to choose its password, changes its display name, deactivates, reactivates and deletes it, lists and ends its sessions, and puts it in groups. Binding a role to a group, and putting a member in a group, need every permission of that role, and a role is not deleted while a group is bound to it (`FCMS3044`). Deactivating or deleting a member ends its sessions at once. Each change is an activity row of no site: `member.registered`, `member.verified`, `member.updated`, `member.deactivated`, `member.deleted`, `member.group_changed`, `member.login_linked`, `member.login_unlinked`, `member_group.created`, `member_group.updated` and `member_group.deleted`, and sign-ins are `member.login_succeeded` and `member.login_failed` with the client address.

### Member sign-in with an external provider

Members can sign in with Google, Microsoft, GitHub, Apple or any OpenID Connect provider. The member login and registration pages show a button for each provider of `FluentCms:Members:Providers`, or `cms.Members.Providers` in code. These providers are apart from those of [single sign-on](#single-sign-on): a provider under `FluentCms:Sso` signs in admin users only and has no button on a member page, a member provider has none on the admin login page, and the same name in both sections is two providers.

A provider's name is 1 to 64 lower-case letters, digits or `-`, and not `password` or `direct_login`, which name the other ways to sign in. It is the login provider of its members in `cms_member_login`, kept with every login as it is written, so do not rename a provider that has members: their logins would no longer be found. Its authentication scheme is `member-{name}`, and the redirect URI to register at the provider is `https://{host}{path base}/account/members/sso/{name}`, built from the [public origin](#public-origin) when one is set.

| Key | For | What it is |
| --- | --- | --- |
| `Type` | every provider | `Google`, `Microsoft`, `GitHub`, `Apple` or `Oidc`. |
| `ClientId` | every provider | The client id the provider gave; for Apple, the Services ID. |
| `ClientSecret` | Google, Microsoft, GitHub, and `Oidc` when the client has one | The client secret. Apple has none: see `PrivateKey`. |
| `Scopes` | every provider | Scopes asked for after those the type needs for the address. |
| `DisplayName` | every provider | The text of the button, "Continue with Google". The type's name by default, and the provider's name for `Oidc`. |
| `Authority` | `Oidc` | The issuer, an https URL. `RequireHttpsMetadata` set to `false` allows http, for a provider on the developer's machine. |
| `TeamId`, `KeyId`, `PrivateKey` | Apple | The team id, the id of the Sign in with Apple key, and the key itself: the PEM text of its `.p8` file, not a path. The handler signs the client secret with it. |
| `LinkVerifiedEmail` | every provider but Microsoft | Off by default; see below. |

Secrets come from configuration only: give `ClientSecret` and `PrivateKey` with user secrets, environment variables or a secret store, and never in a file under source control. A provider that is not valid stops the host with `FCMS3180` and the key: a name that is not lower-case, an unknown type, a missing client id or secret, an Apple key that is not a private key, or a name whose scheme is the name of another authentication scheme. With environment variables:

```bash
FluentCms__Members__AllowRegistration=true
FluentCms__Members__Providers__google__Type=Google
FluentCms__Members__Providers__google__ClientId=...
FluentCms__Members__Providers__google__ClientSecret=...
FluentCms__Members__Providers__apple__Type=Apple
FluentCms__Members__Providers__apple__ClientId=com.example.web
FluentCms__Members__Providers__apple__TeamId=...
FluentCms__Members__Providers__apple__KeyId=...
FluentCms__Members__Providers__apple__PrivateKey=...
FluentCms__Members__Providers__corp__Type=Oidc
FluentCms__Members__Providers__corp__Authority=https://id.example.com
FluentCms__Members__Providers__corp__ClientId=...
```

Google and Microsoft run on the ASP.NET Core handlers, GitHub and Apple on the `AspNet.Security.OAuth` handlers, and `Oidc` on the OpenID Connect handler with the code flow and PKCE. A host tunes a provider further with `services.Configure<GoogleOptions>("member-{name}", ...)` and the like. The Apple package writes the client secret it signs, and the access, refresh and id tokens of each sign-in, to the log at `Trace`, all under the category `AspNet.Security.OAuth.Apple`; no category of FluentCMS holds them at any level. FluentCMS holds that category at `Information` or above, for every logging provider, unless the configuration names it or a category under it (`Logging:LogLevel:AspNet.Security.OAuth.Apple`, or the same under one provider), so a default of `Trace` does not reach it, and a provider kept above `Information` stays there. Naming it lower writes the secret and the tokens, and the startup report lists `member-apple-logging` while it is below `Information` in Production. A host that replaces the logger factory, as Serilog's `UseSerilog` does, sets the level of the category in that logger's own configuration. The request log of ASP.NET Core (`Microsoft.AspNetCore.Hosting.Diagnostics`, at `Information`) writes the URL of each request, query included. The callback of a Google, Microsoft or GitHub provider is a GET, so its line holds the one-time `code` and the `state` the provider sent; Apple and `Oidc` providers, and the providers of [single sign-on](#single-sign-on), post their answer, so their line holds the path only. The code is redeemed once, as the callback arrives, and only with the client secret, so the query is left in the log; a host that keeps its logs where others read them keeps that category at `Warning`. A provider's logout paths are not served: signing out at the provider ends no member session.

**Who signs in.** A member linked to the account at the provider signs in, unless it is deactivated or locked out. Anyone else gets a member only while `AllowRegistration` is on: it is named by the provider's address and display name and has no password. With registration off the sign-in is refused. The provider signs in through the hosted pages and the authorization code flow only; the direct login takes no external login.

**Verified addresses.** Whether the address of a new member is verified is the provider's word. Google, Apple and `Oidc` say so with `email_verified`. From GitHub only the primary address is taken, and only when GitHub verified it, which is why `user:email` is always asked for; the address of the public profile is never used, and an account without a verified primary address makes no member. Microsoft does not say, so a member it makes gets the verification mail, which names the provider, and signs in with Microsoft once the address is verified; the same holds for an account of another provider whose address it does not say is verified.

**An address that is a member's already.** A provider never takes over a member by its address: the sign-in is refused, and the member signs in as before and links the provider itself. `LinkVerifiedEmail` on a provider links such a member at its first sign-in through it instead, only when the provider says the address is verified and the member may sign in now, its own address verified. Turn it on only for a provider you trust to verify addresses; it is not offered for Microsoft. A sign-in with an address the provider did not verify is answered the same whether or not the address is a member's. While the address of a member is not verified, nobody has shown whose it is, so a second claim of it removes what the first one left: when the address is registered again, or someone signs in through a provider with it and a login that is not the member's, the member's password and logins go, the links sent so far stop working, and the owner of the mailbox gets a mail with a link to choose a password, which verifies the address too. Nobody is signed in or linked by that sign-in, whatever the provider says of the address. A reset link used before the address was verified removes the member's logins as well.

**Linking and removing.** On `/account/members/logins` a signed-in member links a provider, which asks before it links and names the account at the provider, and removes one. `GET /api/members/v1/me/logins` lists `{ "provider", "displayName" }` and `DELETE /api/members/v1/me/logins/{provider}` removes one; an administrator does the same with `GET` and `DELETE /api/management/v1/members/{id}/logins` and `.../logins/{provider}` (`members.manage`). Removing a login gives the member a new security stamp and ends its sessions, access tokens included. The last way to sign in is not removed (409, `FCMS3184`): a login counts while its provider is configured, and a password while the member has one.

**Failures.** An error at the provider, or a callback that belongs to no sign-in started in that browser, goes back to the member login page as a failed sign-in, with a `member.login_failed` activity row whose method is the provider's name. The reason of a refusal is in the log with its code: `FCMS3181` no usable address, `FCMS3182` the address of a member that is not linked, `FCMS3183` the member could not be made, `FCMS3187` registration is off (event ids 9090 to 9094). A provider's callback counts towards the `auth` [rate limit](#rate-limits) of its client address.

## Email

Invitations, password resets and the mail of [members](#members) go through `IEmailSender`. In Development the default writes each message, link included, to the log and the startup report lists `development-email-sender`; anywhere else it refuses to send (`FCMS3008`) until a sender is set up. Setting `FluentCms:Email:Smtp:Host` sends through that SMTP server in every environment, Development included:

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
| `IDistributedCache` | the framework's in-memory cache, one per instance, which keeps the member mail limit per instance |
| `IOutputCacheStore` | the framework's in-memory output cache store, one per instance, which evicts only its own copies |

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
| `DisableAfter` | unset | Disables a webhook when a delivery to it dies and every attempt to it has failed for at least this long. Unset never disables one. At most `Retention`. |

A target URL uses `https` outside Development. Deliveries do not follow redirects and do not go through a system proxy. A receiver checks `X-FluentCMS-Signature: t=<unix>,v1=<hex>` by computing HMAC-SHA256 with the webhook's secret over `t + "." + body` and accepting any `v1`, of which there are two while a rotated secret overlaps, refuses a `t` more than five minutes old, and ignores a repeat by `X-FluentCMS-Delivery`. A webhook's header values are secrets: they are stored with Data Protection, like the secret, and no answer returns them. The plugin's [README](../src/plugins/FluentCMS.Plugins.Webhooks/README.md#receivers) has the receiver's code.

## MCP

The MCP plugin (`FluentCMS.Plugins.Mcp`, which the reference host adds) serves the Model Context Protocol at `/mcp`, so that an agent or an IDE works as the user or key it signed in with. A caller sends a bearer token with the `mcp` scope or an API key. The plugin's [README](../src/plugins/FluentCMS.Plugins.Mcp/README.md) covers connecting and adding tools. Its options are in `FluentCms:Mcp`:

| Key | Default | Meaning |
| --- | --- | --- |
| `Name` | `fluentcms` | What the server calls itself to a client: 1 to 64 letters, digits, hyphens, underscores and dots. |
| `Title` | `FluentCMS` | Its name for people, which the resource metadata carries too. |
| `ExposedTypes` | empty | The content types the tools reach, by name. Empty is every type the caller may read. A name the schema does not have stops the host. |
| `MaxExposedTypes` | `10` | How many of those types get tools of their own, from 0 to 100. A type over it is reached through the `query` tool, and the log names it once. |
| `UploadTimeout` | `00:00:30` | The longest the `media_upload` tool waits for a URL, up to 10 minutes. |
| `MaxBulkItems` | `100` | The most entries one call of the `bulk` tool names or selects, from 1 to 1000. |
| `Limits:CallsPerMinute` | `120` | The tool calls a client may make in a minute, counted per client id or API key on each instance. A call over it is a tool error (`FCMS1634`) that says when to retry. |
| `Limits:WritesPerMinute` | `30` | The calls of tools that change data a client may make in a minute, at most `CallsPerMinute`. |
| `MaxRequestBytes` | `1048576` | The body of a request to `/mcp`, in bytes, refused with 413 and `FCMS3111` when larger. `FluentCms:Limits:MaxBodySize` still caps it. |
| `MaxResultBytes` | `262144` | The largest list a content tool answers, in bytes of its JSON. A longer one ends at an entry, with `truncated` and the cursor of the rest. |
| `MaxListTextLength` | `500` | The characters of a text a list shows before it shortens it. |

The `{type}_delete` and `bulk` tools answer a plan first and run only with its confirmation token, which is signed with a key that Data Protection wraps. The instances of a host share the key ring, which is in the database ([authorization server](#authorization-server)), so each accepts the tokens of the others and nothing is stored.

An MCP client finds the authorization server through `/.well-known/oauth-protected-resource/mcp`, which the 401 of `/mcp` names. That document and the `resource` parameter the client then sends are built from the [public origin](#public-origin), so set it. A client either has a registration under `FluentCms:Clients:Applications` whose `Scopes` include `mcp`, or registers itself. The endpoint is stateless, so it needs no session affinity behind a load balancer.

### MCP clients that register themselves

With the plugin, the authorization server takes dynamic client registration (RFC 7591) at `POST /connect/register`, which the discovery document names as `registration_endpoint`. Anyone who reaches the instance can call it, so what a client gets is fixed: no secret, the authorization code flow with PKCE and refresh tokens, the user's consent, redirect URIs on `https` or on `http` at `localhost`, `127.0.0.1` or `[::1]`, and the scopes `mcp`, `openid`, `profile`, `email` and `offline_access`. The policy is the settings area `mcp`, which an administrator with `settings.mcp.manage` for the instance changes with `PUT /api/management/v1/settings/mcp/defaults`. The instance defaults are what counts, since the instance has one authorization server:

| Key | Default | Meaning |
| --- | --- | --- |
| `clientRegistration` | `approval` | `approval`: a client waits until an administrator approves it with a role, and gets no token before. `open`: a client is approved at once with `defaultClientRole`. `off`: the endpoint answers 404 and the discovery document does not name it. |
| `defaultClientRole` | unset | The role a client gets under `open`: the id of a role, or the name of an instance-wide role. While it is unset or names no role, a client waits for approval. |
| `maxPendingClients` | `100` | How many clients may wait for approval at once. A registration beyond it is answered 503. |
| `registrationsPerHour` | `10` | How many clients one client address registers in an hour, on each instance. One beyond it is answered 429. |

The role of a client limits its tokens: a call through the client is allowed what both the user's roles and the client's role allow. An administrator with `mcp.manage` for the instance lists the clients at `GET /api/management/v1/mcp/clients`, approves one with `POST /mcp/clients/{id}/approve` and `{ "role" }`, rejects one with `POST /mcp/clients/{id}/reject`, and removes one with its sessions and tokens with `DELETE /mcp/clients/{id}`. A client that is not approved within 7 days is removed by the daily job `mcp.client-cleanup`. The startup report lists `mcp-open-client-registration` while the policy is `open` in Production; it is made once per start, so a change of the setting shows at the next one.

The plugin keeps the guidelines, the type instructions and the stored prompts of a site in two tables of its own, `cms_mcp_instruction` and `cms_mcp_prompt`. They are a [migration stream](plugin-migrations.md) of the plugin, `cms_mcp__migrations`, which the host applies with its other migrations (`FluentCms:Database:MigrateOnStartup`), so a host that already runs the plugin has one migration to apply after it updates.

## Multi-site

The multi-site plugin (`FluentCMS.Plugins.MultiSite`, which the reference host adds) creates and deletes sites and keeps the host names of each site, under `/api/management/v1/sites`, with `sites.manage` for the instance:

- `POST /sites` `{ "name", "slug", "defaultLocale" }` creates a site with its default locale and its Public role, and `DELETE /sites/{id}` deletes one that is not the default site and has no content;
- `GET` and `POST /sites/{id}/hosts` `{ "hostname", "isPrimary" }`, `PATCH /sites/{id}/hosts/{hostname}` `{ "isPrimary": true }` and `DELETE /sites/{id}/hosts/{hostname}` manage its host names.

A request that names no site by a key's binding, the header or the parameter reads the site of its host name, and the default site when no site has that name. A host name is a DNS name without a scheme, port, path or wildcard, and belongs to one site. A site with host names has one primary host, which a front end reads as its canonical name; the backend redirects nothing by it.

List the host names of every site in `AllowedHosts`. Behind a reverse proxy, the host name is the forwarded one only with `FluentCms:ReverseProxy:ForwardedHeaders:ForwardHost`. `FluentCms:PublicOrigin` stays the one address of the authorization server and the hosted pages, so a token works on every host name. Each instance keeps the host names for 30 seconds, so another instance sees a change within that time. The plugin has no options; its README has the codes.

`GET /api/management/v1/me/sites` lists the sites in which the caller holds a grant, with or without the plugin.

## Media

The media plugin (`FluentCMS.Plugins.Media`, which the reference host adds) keeps the assets and folders of each site under `/api/management/v1/media`, answers `/api/delivery/v1/media/{id}`, and serves signed image transforms at `/media/{id}/{fileName}` and original files at `/media/{id}/original/{fileName}`. Its instance options are in `FluentCms:Media`:

| Key | Default | Meaning |
| --- | --- | --- |
| `Library` | `Shared` | `Shared`: every site reads and writes the media library of the default site, and each site's roles still decide who uploads and who reads there. `PerSite`: each site keeps its own library. An instance with several sites that each have assets sets `PerSite`. |
| `Local:Root` | `App_Data/media` | The directory of the local storage, under the content root unless it is absolute. The startup report lists `media-local-storage` in Production. |
| `MaxRequestBodySize` | `268435456` | The largest upload request, in bytes. |
| `RequireSignedUrls` | `true` | `false` serves unsigned transform URLs. The startup report lists `media-unsigned-urls` while it is off in Production. |
| `SigningKey` | none | The base64 of at least 32 bytes that signs transform URLs. Left out, the plugin generates one key at its first start and stores it protected by Data Protection. Changing the key invalidates every URL signed before. |
| `CacheControl` | `public, max-age=86400` | The `Cache-Control` of a transformed image and of the original file of a public asset. |
| `DeletedRetention` | `30.00:00:00` | How long a deleted asset keeps its file and its row before the daily cleanup purges them. |

New uploads go to the local disk unless a storage package replaces it: `media.UseS3(section)` from `FluentCMS.Plugins.Media.S3` (AWS or an S3-compatible service such as MinIO) or `media.UseAzureBlob(section)` from `FluentCMS.Plugins.Media.AzureBlob` (a connection string, or a container URL with a SAS), in `cms.AddPlugin<MediaPlugin>(media => ...)`. The section's keys are in the plugin's [README](../src/plugins/FluentCMS.Plugins.Media/README.md#storage-on-s3-or-azure-blob). Objects stay private and are served through `/media` only, and assets written on the local disk before the move are still served from `Local:Root`. `/health/ready` includes `media-storage`, which is Unhealthy while the bucket or container cannot be reached.

The limits of uploads and transforms are each site's, in the settings area `media`. The default image processor is [SkiaSharp](https://github.com/mono/SkiaSharp) (MIT), with its Linux assets that need no fontconfig, for containers. A host replaces it with `cms.Providers.Use<IImageProcessor, T>()`, for example to use ImageSharp under its own Six Labors license. The plugin's [README](../src/plugins/FluentCMS.Plugins.Media/README.md#image-transforms) has the parameters and the signing.

## GraphQL

The GraphQL plugin (`FluentCMS.Plugins.GraphQL`, which the reference host adds) serves the content schema at `/graphql`, over the same content service, permissions and query model as the REST APIs. Its options are in `FluentCms:GraphQL`:

| Key | Default | Meaning |
| --- | --- | --- |
| `MaxDepth` | `12` | The deepest a query may nest its fields. |
| `MaxFieldCost` | `1000` | The most a query may cost by HotChocolate's cost analysis: 10 for a root field, and 1 for a populated relation or media field for each entry of the page above it. |
| `MaxTypeCost` | `1000` | The most objects a query may return, by the same analysis. |
| `MaxRequestSize` | `262144` | The body of a request, in bytes, refused with 413 and `FCMS3111` when larger. `FluentCms:Limits:MaxBodySize` still caps it. |
| `ExecutionTimeout` | `00:00:30` | How long a request may run. |
| `OpenIntrospection` | unset | Whether any caller may introspect the schema. Unset, it is open in Development, and elsewhere needs a signed-in caller with `schema.read`. |
| `PersistedQueries` | unset | A folder of query files, each named `{id}.graphql`, that a request names by `id`. A relative path is under the content root. |
| `OnlyPersisted` | `false` | Refuses every query that is not in the folder. |

The page size and populate depth limits are those of `FluentCms:Query`. A Delivery key or an anonymous caller reads published entries and runs no mutation; a caller with the `management` scope runs the mutations its roles allow. Introspection names every type and field of the schema, whatever the caller may read, which is why it is closed outside Development ([ADR 0006](decisions/0006-one-graphql-schema-per-instance.md)). A browser on another origin can send `GET` only, since the endpoint answers the Delivery CORS policy. The plugin's [README](../src/plugins/FluentCMS.Plugins.GraphQL/README.md) has the schema, the queries, the mutations and the error codes.

## AI assist

The AI assist plugin (`FluentCMS.Plugins.AiAssist`, which the reference host adds) runs AI actions on the fields a site opted in, under `/api/management/v1/ai` with `ai.use`, and logs their usage. It is off until a site names a provider and opts fields in, in the settings area `ai`. The plugin's [README](../src/plugins/FluentCMS.Plugins.AiAssist/README.md) covers the settings of a site, the actions and a provider of the host's own. Its options are in `FluentCms:Ai`:

| Key | Default | Meaning |
| --- | --- | --- |
| `AllowedEndpoints` | empty | The endpoints a site may name in its settings, each an absolute `http` or `https` URL with no user, query or fragment, such as `FluentCms__Ai__AllowedEndpoints__0=http://ollama:11434`. Empty leaves a site OpenAI and Anthropic at their own endpoints. A value that is not such a URL stops the host. |
| `{Provider}:ApiKey` | unset | The key of `OpenAi`, `AzureOpenAi`, `Anthropic`, `Ollama` or `OpenAiCompatible` for the sites whose settings hold none. Give it through a secret store or the environment. |
| `MaxInputLength` | `20000` | The most characters of text an action takes. |
| `UsageRetention` | `90.00:00:00` | How long the usage log keeps a row. |

The allow-list keeps a site administrator from pointing the server at an address inside the network: an endpoint is used only when the list holds it, it is checked at every call, and no redirect is followed. The list is read when the host starts, so a change to it applies after a restart. The configuration's key for `OpenAi` or `Anthropic` is sent to that provider's own endpoint only; the key for the other providers goes to whichever allowed endpoint a site names for them. A key in the instance defaults of the `ai` settings is sent only to the provider and the endpoint those defaults name, also from a site that kept it when it saved settings of its own; replace such a key at the provider too when you replace it in the defaults. The calls of a principal are counted in memory on each instance, so with several instances a principal can make its `callsPerMinute` on each.
