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

`UseFluentCms()` puts the request pipeline in place, in the order it works in: what `FluentCms:ReverseProxy` asks for (forwarded headers, the path base, HSTS and the HTTPS redirect), the exception handler (problem details), routing, security headers, CORS, the gate that holds FluentCMS routes to the path base, the refusal of a hosted page over plain http, a coarse limit per client address, the request limits, authentication, the rate limiter of the endpoints, authorization and the output cache. Call it once, before `MapFluentCms()`, which needs it (the endpoints carry CORS and rate limit metadata that only these middleware read). It includes `UseExceptionHandler()`, `UseRouting()`, `UseAuthentication()`, `UseAuthorization()` and `UseOutputCache()`, so a host does not add them again; a host that still calls `UseAuthorization()` after it runs its policies twice, which changes nothing but the cost. The order matters: CORS comes before the limiters so that a 429 still carries CORS headers, the coarse limit comes before authentication so that a flood is turned away before it reaches a key lookup or the token endpoint, authentication comes before the endpoint limiter because that partitions by API key or principal, and authorization comes before the output cache, so that a policy put on `MapFluentCms()`, as in `app.MapFluentCms().RequireAuthorization("cms")`, holds for a cached answer too. `MapFluentCms()` throws `FCMS1104` at startup when `UseFluentCms()` was not called.

Response compression goes before `UseFluentCms()`, so that the output cache keeps the body as the endpoint wrote it and each answer is encoded for the request it goes to:

```csharp
builder.Services.AddResponseCompression();

var app = builder.Build();
app.UseResponseCompression();
app.UseFluentCms();
app.MapFluentCms();
```

Compression after `UseFluentCms()` would sit inside the output cache, which does not vary on `Accept-Encoding`, so a body compressed for one client would go to every other; FluentCMS does not keep such a copy, and every compressed read then runs the endpoint.

A host needs ICU, as the reference image has: with `InvariantGlobalization` on, every culture of a user's admin preferences (`PUT /me/preferences`) is refused.

## Requests

An endpoint runs as the principal of the `FluentCms` authentication scheme, which `AddFluentCms()` registers: the API key handler when a key is sent, otherwise the bearer scheme set in `FluentCmsAuthenticationOptions.BearerScheme`, and anonymous when neither applies. The handler writes `sub` (the id), `name`, `fcms_kind` (a `PrincipalKind` name), `roles` and `scope` (both space-separated), and `fcms_sites` for an API key bound to sites. A request without an authenticated user runs as the anonymous principal, which holds the Public role of its site, so anonymous Delivery access is what that role grants. A principal without a usable `fcms_kind` or `sub` is refused with 401.

The site is the key's binding, then the `X-Cms-Site` header, then the `site` parameter (an id or a slug), then the host name of the request when the multi-site plugin is added (see Multi-site), then the default site. A key bound to one site reads that site. A key bound to several reads the one the request names, or the site of the host name, the default site when no site has it, when that site is among them, and is otherwise refused with `FCMS3122`. A request that names a site outside the binding, or one that does not exist, is refused with `FCMS3121` (403). A header and a parameter that name different sites are refused. The locale is the `locale` parameter, then the `X-Cms-Locale` header, then the site's default; `locale=all` returns every locale of an entry as a map.

## Options

Everything is read from the `FluentCms` section of the host's configuration, so any configuration provider works. Environment variables use `__` for `:`, as in `FluentCms__Database__MigrateOnStartup=false`.

| Section | Holds |
| --- | --- |
| `FluentCms:Database` | `MigrateOnStartup`, `SchemaSync` (see [content migrations](content-migrations.md)) |
| `FluentCms:Security` | `AllowGeneratedSigningKey`, `AllowRequestOrigin`, `AllowUnencryptedKeyRing` (see [startup refusals](#startup-refusals)), `SigningCertificate:Path`, `SigningCertificate:Password`, `Cors:Delivery:AllowedOrigins`, `Cors:Management:AllowedOrigins`, `Cors:Members:AllowedOrigins`, `ReferrerPolicy`, `FrameAncestors` |
| `FluentCms:Users` | `Password`, `Lockout`, `InvitationLifetime` (7 days), `PasswordResetLifetime` (1 hour) (see [hosted pages](#hosted-pages)) |
| `FluentCms:Email` | `Smtp:Host`, `Smtp:Port` (587), `Smtp:Security` (`StartTls`, `SslOnConnect` or `None`), `Smtp:UserName`, `Smtp:Password`, `Smtp:From` (see [email](#email)) |
| `FluentCms:Clients` | `AdminClientId`, `AccessTokenLifetime`, `RefreshTokenLifetime`, `SignInCookieLifetime`, `AbsoluteSessionLifetime`, and `Applications:{clientId}` with `DisplayName`, `Secret`, `RedirectUris`, `PostLogoutRedirectUris`, `Scopes`, `Roles`, `RequireConsent`, `Pool`, `AllowDirectLogin` (see [authorization server](#authorization-server) and [members](#members)) |
| `FluentCms:Sso` | `DisableLocalLogin`, and `Providers:{name}` with `Preset`, `DisplayName`, `Authority`, `TenantId`, `Domain`, `AuthorizationServerId`, `BaseUrl`, `Realm`, `ClientId`, `ClientSecret`, `Scopes`, `AutoProvision`, `RequireVerifiedEmail`, `LinkExistingUsers`, `DefaultRole`, `EmailClaim`, `GroupsClaim`, `RoleMappings`, `TrustMfaClaim`, `RequireHttpsMetadata` (see [single sign-on](#single-sign-on)) |
| `FluentCms:Members` | `AllowRegistration`, `AllowDirectLogin`, `Password`, `Lockout`, `EmailVerificationLifetime` (24 hours), `PasswordResetLifetime` (1 hour), `UnverifiedMemberLifetime` (7 days), `Links:VerifyEmail`, `Links:ResetPassword` (see [members](#members)), and `Providers:{name}` with `Type`, `DisplayName`, `ClientId`, `ClientSecret`, `Scopes`, `Authority`, `RequireHttpsMetadata`, `TeamId`, `KeyId`, `PrivateKey`, `LinkVerifiedEmail` (see [external providers](#member-sign-in-with-an-external-provider)) |
| `FluentCms:PublicOrigin` | the address users reach the instance at, such as `https://cms.example.com` (see [public origin](#public-origin)) |
| `FluentCms:ReverseProxy` | `PathBase`, `ForwardedHeaders`, `HttpsRedirection`, `Hsts` (see [reverse proxy](reverse-proxy.md)) |
| `FluentCms:RateLimits` | `Delivery:PermitLimit`, `Delivery:MaxKeyLimit`, `Management:PermitLimit`, `Auth:PermitLimit`, `PreAuth:PermitLimit`, `Health:PermitLimit`, `StaticFiles:PermitLimit` |
| `FluentCms:Limits` | `MaxBodySize` |
| `FluentCms:Delivery:Cache` | `CacheControl`, `OutputCache`, `OutputCacheDuration`, `CacheTagHeader` (see [Delivery caching](#delivery-caching)) |
| `FluentCms:OpenApi` | `Enabled` (see [OpenAPI and the Scalar UI](#openapi-and-the-scalar-ui)) |
| `FluentCms:Query` | `DefaultPageSize`, `MaxPageSize`, `MaxOffset` (10,000 entries a page or offset may skip; read further by cursor), `MaxPopulateDepth`, `MaxFilterNodes`, `MaxPopulatedEntries` |
| `FluentCms:Content` | the bounds of a content write: `MaxItems` (1,000, the items of a multiple field or blocks that declares no maximum), `HookTimeout` (30 seconds, at most a day: how long one hook may run inside the write's transaction; one that runs longer fails the write with `FCMS2410`, and the scope writes and commits nothing more, as when a cancelled caller leaves a hook running), `SlowHookThreshold` (1 second, a hook that takes longer is logged), `LockTimeout` (10 seconds, at most a day: how long a write waits for what another write holds, an entry's rows or on SQLite the database, before it fails with `FCMS2411`, 503 with `Retry-After`; on a SQLite file the wait ends at the first 5 second busy timeout past it, so 10 seconds is waited as 10 to 15. Keep it below the command timeout of the connection string, 30 seconds by default: a lower command timeout ends the wait first, with `FCMS3199` (500) on PostgreSQL and SQL Server and with `FCMS2411` on SQLite) |
| `FluentCms:RichText` | `LinkSchemes`, the URL schemes a rich text URL may have, in place of `http`, `https`, `mailto` and `tel`. `javascript`, `data`, `vbscript`, `blob`, `file` and `about` stop the host. A stored URL with any other scheme is left out of reads (spec 4.2, "Link URLs as built") |
| `FluentCms:Jobs` | the job runner: `Queues`, `PollInterval`, `LeaseDuration`, retries, retention: `SucceededRetention` (7 days), `DeadLetteredRetention` (30 days), `AttemptTimeout`, and `MaxAbandonedJobs` (default 4), the jobs past their timeout an instance leaves running before a further one keeps its worker |
| `FluentCms:Events:Handlers` | the in-process event handlers: `Timeout` (5 seconds), `SlowThreshold` (1 second) (see [event handlers](#event-handlers)) |
| `FluentCms:Events:Outbox` | the outbox: `BatchSize`, `MaxAttempts`, `LeaseDuration`, retries, `OrphanAfter`, `Retention`, `FailedRetention` (30 days, for deliveries that ran out of attempts; a delivery left for a missing handler or a disabled plugin is kept until it is retried or removed), `PruneBatchSize` |
| `FluentCms:Activity` | the activity log: `Enabled`, `Retention`, `SiteRetention`, `PruneSchedule` |
| `FluentCms:Idempotency` | `Window` (24 hours), `InProgressTimeout` (5 minutes) (see [idempotency keys](#idempotency-keys)) |
| `FluentCms:Health` | `JobRunnerHeartbeatTimeout`, `PendingJobAgeLimit` |
| `FluentCms:Transfer` | the bounds of an import: `MaxPackageSize` (1 GiB, the body), `MaxUnpackedSize` (the sum of the sizes the files of a package declare unpacked; `MaxPackageSize` when not set), `MaxEntries` (100,000 files), `MaxJsonSize` (64 MiB for each of `manifest.json`, `schema.json`, `settings.json` and `roles.json`), `MaxReadSize` (the compressed bytes an import reads, a file counting each time it is opened; twice `MaxPackageSize` when not set), `MaxLineLength` (16 MiB for a line of a `.jsonl` file), `MaxReportItems` (1,000 conflicts and errors listed in the report; every item is counted). A package over a bound is refused with `FCMS3200`, and an import that reaches one once it has started stops with it as the last item of its report. `ExportSnapshotTimeout` (1 hour, at most 1 day) is the longest an export holds the database snapshot it reads its entries from, which keeps a PostgreSQL transaction, a SQL Server version store or a SQLite WAL from being cleaned up while the package downloads. It counts from the start of the snapshot, not from when a reader stalls, so an export of a large site that is merely slow for that long is cut too: the client sees the connection reset after the 200, with nothing that names the option, so `fluentcms export --url` fails with `FCMS3252` (stopped answering), while `fluentcms export` run in the host fails with a cancellation that names it; neither writes a file, the import refuses what was received with `FCMS3200`, and the server's log has warning 9190, which names the site and the option. Raise it for such exports, as `FluentCms__Transfer__ExportSnapshotTimeout=02:00:00` |

A key the configuration sets wins over what the host set in code inside `AddFluentCms`; a key it leaves out keeps the code's value. The options are checked when the host starts, and an invalid value stops it with an `FCMS` code and the key, for example `FCMS1101: FluentCms:Query:MaxPageSize: The field MaxPageSize must be between 1 and 2147483647.`

A CORS origin is an `http` or `https` origin with no path, such as `https://app.example.com`. The lists are empty by default, which allows no browser origin, and a wildcard is refused.

To keep dead-lettered jobs or failed outbox deliveries until they are retried, set `FluentCms:Jobs:DeadLetteredRetention` or `FluentCms:Events:Outbox:FailedRetention` to an empty value: `""` in `appsettings.json`, or an empty environment variable such as `FluentCms__Jobs__DeadLetteredRetention=` where the platform keeps one. Windows drops an environment variable set to an empty value, so there use `appsettings.json`, or set `cms.Jobs.Options.DeadLetteredRetention = null` and `cms.Events.Outbox.FailedRetention = null` in code, which the configuration overrides only when it names the key. Each run of `jobs.retention` logs how many succeeded and dead-lettered jobs it removed (event id 9512, Information); a retention schedule that cannot be stored is event id 9513 (Error).

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
| coarse (`PreAuth`) | every FluentCMS route but `/health` and the static files a surface marks, plugin routes included, before authentication | the client address | 6000 |
| `Health` | `/health`, before authentication, apart from the coarse limit | the client address | 1200 |
| `StaticFiles` | the static files a surface marks, before authentication, apart from the coarse limit | the client address | 15000 |

An API key whose principal has an `fcms_rate_limit` claim gets that many requests per minute instead of the `delivery` default, at most `Delivery:MaxKeyLimit` (5000), which must not be below `Delivery:PermitLimit` nor above `PreAuth:PermitLimit`, the coarse limit that holds every caller behind one address together. `/health` has no policy of the endpoints, only its own limit.

An endpoint a plugin puts under the `caller` policy with `RequireCallerRateLimit()`, such as `/graphql`, serves both kinds of caller: one with the `management` scope is counted as the `management` policy counts it, and any other as `delivery` does, a key's own limit included. Its buckets are apart from those of the REST APIs.

The coarse limit is checked before authentication on every endpoint that `MapFluentCms()` maps, those of plugins included, and on every path under the groups of core, so it also covers requests that no endpoint answers, such as `/connect/token` from the authorization server. A flood of API keys sent to a plugin's route is turned away there too, before any key is looked up. It adds no `RateLimit-*` headers, and neither do the `Health` and `StaticFiles` limits; a refusal is the same 429 problem. The strict `auth` limit is checked there too, for every path under `/connect`, `/account` and `/api/members/v1`, because the authorization server answers `/connect/token`, `/connect/introspect` and `/connect/revoke` without routing; the endpoints of those groups do not count a request a second time.

A caller that has been quiet may send the whole limit at once, and then one request per minute divided by the limit (every tenth of a second at 600), so a burst at the turn of a minute is not let through twice, as a fixed window of one minute would let it. A response carries `RateLimit-Limit`, `RateLimit-Remaining` (the requests that may be sent now) and `RateLimit-Reset` (seconds until the bucket is full again), from the IETF draft. A refused request is a 429 `application/problem+json` with code `FCMS3110` and a `Retry-After` in seconds until the next request is let through.

The key and the principal are those of the `FluentCms` authentication scheme when the host has one (the API key registration adds it, as a policy scheme that forwards to the key scheme when a key is presented and otherwise to the default scheme), so a cookie or token default scheme does not hide a key. Without it they are those of the default scheme.

The files a surface serves as they are, such as the fingerprinted files of the admin, count under `StaticFiles:PermitLimit` instead of the coarse limit once the surface marks their endpoints, so that an office behind one address can load the admin many times a minute: a cold load is some 230 files, and the default holds forty loads a minute with room to spare. Only an endpoint the surface marks counts there, never a path or an extension the caller sends, so a page such as the admin's `index.html`, its `config.json`, the page its client routes fall back to, a fallback that answers a path matching no file and every API and sign-in request still count under the coarse limit. A key sent with such a file is never looked up, as on `/health`, so the limit lets through no key lookup that the coarse limit would have held back. The admin marks the files of its app, their compressed copies and fingerprinted paths included, and not `index.html`, `config.json`, `/admin/` or the page of its client routes. Raise the limit only when more people than that load a surface behind one address.

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

With `CacheTagHeader` set, every published Delivery read, from an API key too, carries its tags in that header: `site:{id}`, `type:{site}:{type}` (`type:global:{type}`) and `entry:{id}` for every entry in the body, with `media:{id}` and `media-presets:{library}` for every asset the read resolved, and the media route's `site:{id}`, `media:{id}` and `media-presets:{library}`. A draft read carries none. The tags are listed apart by a space for `Surrogate-Key`, as Fastly reads it, and by a comma for any other name, as Cloudflare's `Cache-Tag` and Akamai's `Edge-Cache-Tag` are read. Each `CacheInvalidatedEvent` names tags from the same set, so a CDN that purges by them drops every copy the change made stale. The event is raised while `CacheTagHeader` is set even with `OutputCache` off.

The header's value is held to 2 KB, so that with the other headers it fits the 4 KB in which nginx buffers them by default, well under the 16 KB of Cloudflare and Fastly. A read whose tags would pass it, such as a long list page, carries in place of its entries' tags the tag of every content type it shows entries of, `type:{site}:{type}`, which a change to any of those entries purges: the purge is coarser, but no copy stays stale. A read whose tags pass it even so, such as one that populates some fifty assets, carries only `site:{id}` and is sent with the `private` form of `CacheControl`, so a CDN does not store it; a smaller page size, or fewer populated fields, keeps such a read at the edge. The copy the server keeps holds every tag either way. The media plugin reads the option from configuration, as it reads `CacheControl`, so set it there rather than in code when the plugin is added.

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

The server of the document, and with it the URLs of the authorization server, is `FluentCms:PublicOrigin` when it is set. Without it the server is the request's path base alone, such as `/cms`, or `/`, which a client reads relative to the address it fetched the document from; the `Host` of the request is never in the document, so every host is served one cached copy. The routes are not held to `FluentCms:ReverseProxy:PathBase` (see [reverse proxy](reverse-proxy.md)). The document is the one named `v1` of `Microsoft.AspNetCore.OpenApi`, so a host that documents its own endpoints adds a document of another name, such as `AddOpenApi("host")`.

## Startup report and diagnostics

Once the migrations have run, the host logs one `StartupReport` (event id 9130, Information) and a warning per insecure default (event id 9131). An insecure default seen only once requests arrive, forwarded headers from an address the host does not trust, is logged for each of the first four such addresses (event id 9133) and added to the report, which keeps it whenever the report is made. The report is a public type of the SDK (`FluentCMS.Diagnostics.StartupReport`). It names the environment, the FluentCMS and data provider versions, the FluentCMS assemblies in the process, the plugins, the [provider slots](#provider-slots) and the implementation each resolves to, the pending migrations of the system, plugin and content streams, the schema hash in code against the one last applied, and the insecure defaults: the refusals of the startup checks below (a generated signing key even in Development, which does not enforce it), a generated signing key that `AllowGeneratedSigningKey` accepted, the key ring stored unencrypted where it is not refused (`data-protection-keys-unencrypted`), the Development email sender, the HTTPS redirect being off outside Development, `AllowedHosts` left at `*` or unset outside Development, `FluentCms:PublicOrigin` unset outside Development and accepted with `AllowRequestOrigin` (`public-origin-unset`), schema sync running outside Development (see [content migrations](content-migrations.md#schema-sync)), forwarded headers trusted from every address, forwarded headers sent from an address the host does not trust (`forwarded-headers-untrusted`, see [reverse proxy](reverse-proxy.md#options)), the direct login of members turned on outside Development (`member-direct-login`, see [members](#members)), the member mail limit kept in the in-memory `IDistributedCache` outside Development while an email sender is set up (`member-mail-limit-per-instance`), secret settings stored before they were protected at rest (`settings-secrets-unprotected`) or that the Data Protection key ring cannot unprotect (`settings-secrets-unreadable`; each such value logs a warning with event id 9804, and a check that cannot read the settings logs event id 9805), the previous key ring certificates of the reference host outside Development (`data-protection-previous-certificates`, see [replacing the key ring certificate](#replacing-the-key-ring-certificate)), and those a plugin reports through `IInsecureDefaultSource`, such as `webhooks-private-networks` and `mcp-open-client-registration`. It holds no connection string, key or password. If the report cannot be made, the log says so (event id 9132, with the scrubbed message) and the report is reduced to what needs no database, with `failure` naming the exception type; a request that arrives before any report exists gets a 503 (`FCMS3106`).

`GET /api/management/v1/system/diagnostics` returns the same report as JSON. It needs the instance-wide permission `system.read` (a Super Admin has it), which a role of one site, a grant limited to sites and a key bound to sites do not reach, and which a Delivery or Preview key never gets because it lacks the management scope.

## Startup refusals

In every environment but Development, Staging and one of any other name included, some settings that are fine on a developer machine stop the host before it touches the database. Each refusal names its setting and the key that lifts it, and when several apply the host names them all at once:

| Code | Refusal | Fixed by | Lifted by |
| --- | --- | --- | --- |
| `FCMS1102` | Tokens are signed with a generated key, because no signing certificate is configured | `FluentCms:Security:SigningCertificate` ([authorization server](#authorization-server)) | `FluentCms:Security:AllowGeneratedSigningKey = true` |
| `FCMS1105` | Links in emails follow the request's `Host`, because `FluentCms:PublicOrigin` is not set | `FluentCms:PublicOrigin` ([public origin](#public-origin)) | `FluentCms:Security:AllowRequestOrigin = true` |
| `FCMS1106` | The Data Protection key ring is stored unencrypted, because no XML encryptor is registered | an encryptor ([authorization server](#authorization-server)) | `FluentCms:Security:AllowUnencryptedKeyRing = true` |

A setting that is lifted rather than fixed stays in the startup report, as `generated-signing-key`, `public-origin-unset` or `data-protection-keys-unencrypted`. Development starts with all three, and its report lists the generated key as `FCMS1102` and the unencrypted ring as `data-protection-keys-unencrypted`. `fluentcms doctor` names the same refusals in the same environments.

## API keys

`AddFluentCms()` registers the `ApiKey` authentication scheme. It reads a key from `Authorization: ApiKey <key>` or from the `X-Api-Key` header, and leaves a request with neither to the other schemes. A request with a key and any other credential is refused. The FluentCMS endpoints authenticate with the `FluentCms` scheme, which uses `ApiKey` when the request carries a key and otherwise the bearer scheme of the authorization server. It never uses the host's default scheme, so a cookie is not a credential for the APIs. The host calls `app.UseAuthentication()` before `MapFluentCms()`. A key's `last_used_at` is written at most once every five minutes.

A wrong, expired or revoked key gets the same 401, `FCMS3016`, so the answer does not tell that a secret was ever a key. The log says why: each refused key is event id 9010 (`ApiKeyRefused`, Information) with its code (`FCMS3016` wrong, `FCMS3017` expired, `FCMS3018` revoked, `FCMS3019` sent with another credential), the client address and the id of the key when one was found, never the key. The meter `FluentCMS` counts them in `fluentcms.api_keys.refused`, tagged `fluentcms.code`; alert on it to see keys being probed. A refused key writes no activity row, since a row per failed key would let a flood of keys write to the database as fast as it is sent; the [rate limit](#rate-limits) before authentication bounds it instead.

A key can be limited to IP addresses and CIDR ranges with `allowedAddresses`, on create or with `PUT /api/management/v1/api-keys/{id}/allowed-addresses`. The address checked is the one the host trusts, so behind a proxy configure the [forwarded headers](reverse-proxy.md) first, or every request comes from the proxy's address. A valid key sent from another address gets `FCMS3016` too; the log says `FCMS3023`, and the activity log has `api_key.address_refused` with the address, at most once per key and address, and for ten addresses per key, every five minutes.

`POST /api/management/v1/api-keys/{id}/rotate` gives a key a new secret and keeps the old one working for an overlap, so clients can change over without a gap. The body `{ "overlapSeconds": n }` asks for a shorter overlap, and `0` ends the old secret at once, as for a leaked key.

| `FluentCms:ApiKeys:` | Default | Meaning |
| --- | --- | --- |
| `RotationOverlap` | `1.00:00:00` | How long the secret a rotation replaces is still accepted beside the new one, at most 30 days. `00:00:00` ends it at the rotation. A rotation may ask for a shorter overlap, and the answers of `/api-keys` give this one as `maxOverlapSeconds`, in whole seconds rounded down. |

### Preview tokens

`POST /api/management/v1/content/{type}/{id}/preview-token` mints a token that reads the draft of that one entry, optionally of one locale (`{ "minutes", "locale" }`, both optional). The caller needs the `management` scope and the permission to read that draft. A front end sends it as `Authorization: Preview <token>` to `GET /api/delivery/v1/{type}/{id}?status=draft`, and every other read with it is refused with `FCMS3028`. It is signed with the authorization server's key and nothing is stored, so it cannot be revoked: keep the lifetimes short. It stops working at once if its user is deactivated, its key revoked or its role loses the read.

The settings area `preview` (`settings.preview.manage`) names the front end's page for each content type, per site with instance defaults, such as `{ "templates": { "article": "https://www.example.com/{locale}/articles/{slug}?preview={token}" } }`. A template is an absolute `http` or `https` URL whose placeholders, `{id}`, `{locale}`, `{token}` and a single-value field of the type, stand in its path, query or fragment, with `{token}` as the whole value of one query parameter; a `%` in it is a whole escape such as `%20`, and its path does not start with `//`. The answer then carries `url`, filled in for the draft with the token, and `publishedUrl`, filled in for the published row of the same locale without the token's parameter; each is null when the type has no template or a value is missing, and `publishedUrl` also when that locale was never published.

| `FluentCms:PreviewTokens:` | Default | Meaning |
| --- | --- | --- |
| `DefaultLifetime` | `00:10:00` | How long a token lasts when the request names no `minutes`, in whole minutes from one minute to `MaximumLifetime`. |
| `MaximumLifetime` | `01:00:00` | The most `minutes` may ask for, in whole minutes from one minute to one day. |

## Idempotency keys

A client that retries `POST /api/management/v1/content/{type}` or `.../bulk` after a timeout sends the same `Idempotency-Key` (such as a UUID) each time. A repeat of the same request answers the first response, with `Idempotent-Replayed: true`, and creates nothing; the same key with another request is `FCMS3141` (422), and a repeat while the first still runs is `FCMS3142` (409). Keys belong to the caller and the site, and are kept for `FluentCms:Idempotency:Window` (24 hours, at most 30 days); the hourly job `idempotency.retention` removes them. A request whose instance stopped before it answered holds its key for `InProgressTimeout` (5 minutes, at most the window), after which a repeat runs. While a request runs, its lease is renewed every third of `InProgressTimeout`, so one that runs longer is not taken over. A key past its window is not claimed afresh or removed while a request still holds its lease, so a repeat during that request is `FCMS3142`; but once it has answered, its key is expired, and a repeat runs the request again as a new one. Keep the window longer than a request and its retries. Should renewals fail for a whole lease (event 9144) and a repeat take the key over, the first request is cancelled when its next renewal finds the key in the repeat's hands (event 9145): what it had not committed rolls back and it answers `FCMS3142`, unless its write had already committed, when it answers what it wrote without keeping it. A request whose key expired and was removed after its lease ran out puts it back at its next renewal and goes on holding it (event 9146), unless another request claimed it first, which cancels it as a takeover does. On SQLite neither the renewal nor the takeover writes while a request holds the writer. A request whose work answered before its client went away keeps that answer although the client went away while it was written, so a retry gets it, replayed. A client that goes away while the work still runs leaves the key held, without an answer, until its lease ends, and a retry after that runs the request again: a bulk then reports its remaining items as not run, and that answer is not kept, so the retry runs the bulk again, its items that ran included, and a create whose client went away just after its commit runs again too. Writing an answer after the client left takes `InProgressTimeout` at most (event 9147 when it takes longer, and nothing is kept), so such a key can be held for up to twice `InProgressTimeout`. A refused or failed request keeps nothing, so a retry runs again.

## Authorization server

`AddFluentCms()` adds the OpenIddict server at `/connect/authorize`, `/connect/token`, `/connect/userinfo`, `/connect/introspect`, `/connect/revoke` and `/connect/logout`, with discovery at `/.well-known/openid-configuration` and the keys at `/.well-known/jwks`. It answers HTTPS only; behind a proxy that ends TLS, forward the scheme. Under `FluentCms:ReverseProxy:PathBase` these endpoints, the issuer and the discovery URLs carry the path base, and the endpoints are not answered without it. The issuer and the discovery URLs are built from the [public origin](#public-origin) when it is set, and from the request otherwise. The flows are the authorization code flow with PKCE (`S256`, for every client), refresh tokens that rotate, and client credentials. A used refresh token still works for OpenIddict's 30 seconds of leeway, so two refreshes that cross, as two tabs of one application make, both succeed; used after that, it ends the session and its tokens. Access tokens are signed JWTs with the audience `fluentcms:management`.

Lifetimes are `TimeSpan` values under `FluentCms:Clients`:

| Key | Default | What it is |
| --- | --- | --- |
| `AccessTokenLifetime` | `01:00:00` | How long an access token is valid. |
| `RefreshTokenLifetime` | `14.00:00:00` | How long a refresh token lasts unused; using one gives a new one for as long. |
| `SignInCookieLifetime` | `14.00:00:00` | How long the sign-in on the hosted pages lasts unused; each use slides it on. |
| `AbsoluteSessionLifetime` | unset, no limit | How long a session lasts after the user signed in, however often it is refreshed. Past it the token endpoint refuses the code or the refresh (`invalid_grant`), and the authorize endpoint and the hosted pages that need a signed-in user ask the user to sign in again over the cookie; the logout pages still answer. |

Each is more than zero and at most 365 days, and `AccessTokenLifetime` is no longer than `RefreshTokenLifetime`; the host stops with `FCMS3021` otherwise. Without `AbsoluteSessionLifetime`, a client that keeps refreshing keeps its session for as long as the user can sign in, so set it where sessions must end, such as `12:00:00` for a working day.

A bearer token is read from the `Authorization` header only, never from the query string or a form. Logout takes the POST of the logout page, which carries its antiforgery token, or a request with a valid `id_token_hint` of the signed-in user; anything else, such as a form of another site that posts to `/connect/logout`, goes to the logout page, which asks the user and posts it. The authorize endpoint asks the user to sign in again on `prompt=login` or when the sign-in is older than the request's `max_age` (with `prompt=none`, it answers `login_required`), and the identity token and the access token carry `auth_time`. The token endpoint refuses a code or refresh token of a user who is deactivated or whose password or security stamp changed since. A lockout, which anyone who knows a user name raises, ends no session: the user's codes and refresh tokens are still redeemed ([#3271](https://github.com/fluentcms/FluentCMS/issues/3271)). A recurring job, `oidc.prune` (daily at 04:15 UTC, scheduled a minute after the host starts), removes tokens and sessions that can no longer be used once they are 14 days old.

Clients are registered when the host starts, from `FluentCms:Clients:Applications` or `cms.Clients.Applications` in code, keyed by client id. A client with `RedirectUris` signs users in; one with a `Secret` is confidential and may sign in as a service account with client credentials, holding the instance-wide roles `Roles` names. `Scopes` are what it may ask for: `openid`, `profile`, `email`, `offline_access`, `draft`, `management` and `mcp`, which is the scope of the [MCP server](#mcp). Requests with a token of the client named by `AdminClientId` (`fluentcms-admin` by default) run with the `Admin` origin. A client that was registered from the options and is no longer listed is deleted, with its sessions and tokens, at the next start, with a warning that names it. When the options list no client at all, which is more likely a configuration that failed to load, nothing is deleted and a warning names the clients kept. A client registered some other way is left as it is. The authorize endpoint needs the user signed in on the hosted pages and sends anyone else to `/account/login`. A client with `RequireConsent` is a third-party one: the user allows it on the consent page before it gets a code, once for the scopes it asks; the clients of the host, such as the admin, sign in without that page.

Tokens are signed with `SigningCertificate` (a PKCS#12 file with an RSA key) when it is set. Otherwise the server generates an RSA key and keeps it in `cms_signing_key`, protected with Data Protection, so every instance and every restart uses the same one; the host refuses that outside Development unless `AllowGeneratedSigningKey` is set. On Windows the certificate's private key is kept in memory only, so nothing is written to the user's key folder (`%APPDATA%\Microsoft\Crypto\Keys`). A certificate the host loads itself, such as one for `ProtectKeysWithCertificate` below, is written there unless it is loaded with `X509KeyStorageFlags.EphemeralKeySet`, and its key file is left behind when the process is killed. Kestrel loads its own HTTPS certificate, and TLS on Windows needs its key in that folder, so the file of a PFX that Kestrel serves can be left there the same way. The key that encrypts codes and refresh tokens is always kept there. The Data Protection key ring is kept in `cms_data_protection_key` under the application name `FluentCMS`, unless the host persists it elsewhere, and is loaded on its first use rather than when the host starts. A new key is made under the distributed lock (`IDistributedLock`, kept in `cms_lock` with no configuration), so instances that start together on an empty database share the first one.

The key ring is not encrypted at rest unless the host adds an encryptor, and anyone who can read the database (a backup, a read-only user) can then read it and forge the sign-in cookie the authorize endpoint accepts. Outside Development the host refuses to start without one (`FCMS1106`) unless `FluentCms:Security:AllowUnencryptedKeyRing` is true. Add it with Data Protection's own builder, before `AddFluentCms` so that the ring still loads on its first use, with a certificate from the host's secret store (an RSA key, with its private key):

```csharp
builder.Services.AddDataProtection().ProtectKeysWithCertificate(certificate);
```

or with a key in Azure Key Vault, through `Azure.Extensions.AspNetCore.DataProtection.Keys` and `Azure.Identity`:

```csharp
builder.Services.AddDataProtection()
    .ProtectKeysWithAzureKeyVault(new Uri("https://my-vault.vault.azure.net/keys/fluentcms-ring"), new DefaultAzureCredential());
```

Any other `IXmlEncryptor`, such as DPAPI on Windows, counts the same. Keys written before the encryptor was added stay readable. A value that Data Protection protects stays bound to the ring key that protected it, and an expired key still reads it, so the certificate that protects a key is needed for as long as such a value is; to replace it, see [replacing the key ring certificate](#replacing-the-key-ring-certificate). The [Docker image](docker.md#before-production) reads its certificate from `FluentCms:DataProtection:Certificate:Path` and `Password` instead. Keep `cms_data_protection_key`, `cms_signing_key` and the `cms_oidc_*` tables out of exports, and treat a database backup as holding secrets.

OpenIddict logs whole tokens at the `Trace` level. Do not set the `OpenIddict` log category to `Trace` in production.

### Replacing the key ring certificate

Replacing the certificate does not rotate the keys already in the ring: they stay as they are, encrypted for the old certificate, and the values protected under them stay so too. The old certificate stays as a previous one, which only reads the ring: a host of your own passes it to `UnprotectKeysWithAnyCertificate` beside `ProtectKeysWithCertificate(newCertificate)`, and the reference host loads each entry of `FluentCms:DataProtection:PreviousCertificates`, a list of `Path` and `Password` like `Certificate` (`FluentCms__DataProtection__PreviousCertificates__0__Path` and `__0__Password` in the environment), for reading alone. A new key is always protected with the current certificate. In every environment the reference host stops with `FCMS3155` on an entry with an empty `Path` and a `Password`, one that cannot be read as a PKCS#12 file with its password or holds no RSA private key, and on previous certificates without `FluentCms:DataProtection:Certificate:Path`; the message names the key and the file, never the password. An entry whose `Path` and `Password` are both empty is absent, as an empty `Certificate:Path` is, so an entry of `appsettings.json`, which the environment cannot remove, is turned off by setting both to empty there, and a compose file may pass `${OLD_RING_CERTIFICATE:-}`. While any is set, the startup report lists `data-protection-previous-certificates` outside Development, with their thumbprints. Retiring an older ring key under the same certificate takes steps 2 and 3 alone.

1. Set the new certificate as the current one and the old one as previous, and restart every instance. Keys made from then on are protected with the new certificate, and the older ones are read with the old.
2. Wait until a key made after the change is the default. Data Protection makes the next key two days before the default expires, 90 days after it was made, and every instance has read it by the time it becomes the default. A key made by hand, such as with `IKeyManager.CreateNewKey` in a host of your own, is the default at once only on the instance that made it; the others protect under the old default until they read the ring again, for up to 24 hours, so restart them before the next step.
3. Run `fluentcms settings reprotect` with the same configuration. It protects every stored secret setting again under the default key and names that key's id: check that it is the key of step 2, the row of `cms_data_protection_key` whose `xml` has that id, made after the change. A row with a secret the ring cannot unprotect is left as it is and named by area and scope, and so is a value under a key the class no longer reads as a secret, which a write of the area without that key removes; either makes the command end with `FCMS3308` and exit code 1. Every row of an area no settings class of the host registers is left with every value unchanged and named by area and scope too, because the command cannot tell which of its values are secrets, nor whether the plugin that owns the area is only not loaded in this run; it ends with `FCMS3308` only when such a row holds a protected value that is not under the default key, so run the command in a host that registers the area. Nothing is shown of a value. Run it again until it ends with 0; a second run after a full one changes nothing.
4. Remove the previous certificate once nothing protected under the older keys is still needed. The command covers the secret settings only. Webhook secrets and headers, the media URL signing key (`cms_media_signing_key`), a generated token signing key (`cms_signing_key`), and sign-in cookies, reset links and tokens until they expire stay under the key that protected them. Keep the previous certificate for as long as they are needed, or set the webhook secrets and headers again first; a media signing key the ring cannot read stops signed media URLs until its row is removed or `FluentCms:Media:SigningKey` is set, and either invalidates the URLs signed before.

Once a key under the new certificate exists, going back to the old certificate alone makes that key unreadable, and with it whatever was protected under it; go back by keeping the new certificate as a previous one.

A certificate whose private key may have leaked is not replaced this way: do not wait for a new default key. While the old certificate is mounted as a previous one, and for as long as the older keys are in `cms_data_protection_key`, anyone with its private key and read access to that table can decrypt every older key, expired ones included, and with them forge sign-in cookies, antiforgery and reset tokens that the host accepts, and read the secrets still under those keys. Neither the image nor the `fluentcms` tool has a command to make or revoke a ring key. With the image, set the new certificate and no previous one and restart every instance: the host can then read none of the older keys, so it accepts nothing protected under them, and Data Protection makes a new key under the new certificate on its first use. What was under the older keys is lost: every sign-in on the hosted pages, whose cookie Data Protection protects, pending password reset links and other tokens Data Protection protects, the secret settings, which read as their code defaults (`settings-secrets-unreadable`) until they are set again, and the stored values of [#3584](https://github.com/fluentcms/FluentCMS/issues/3584): webhook secrets and headers, the media signing key and a generated token signing key. Set those again with new values, rotated where they are issued, since a copy of the database and the leaked key read the old ones. A host of your own can instead make a key at once with `IKeyManager.CreateNewKey`, restart the instances, run `fluentcms settings reprotect` and then remove the previous certificate, or revoke the older keys with `IKeyManager.RevokeAllKeys`, which ends the same sessions and tokens.

## Public origin

`FluentCms:PublicOrigin`, or `cms.PublicOrigin` in code, is the address users reach the instance at, such as `https://cms.example.com`. Password reset and invitation links, the issuer, the URLs of the discovery document, and the server and authorization server URLs of the OpenAPI document are built from it, so the `Host` a request names, forged or not, plays no part in them. It is an absolute `https` URL (`http` too in Development) with no user, query or fragment. Its path is empty or the path base: with `FluentCms:ReverseProxy:PathBase` set to `/cms`, `https://cms.example.com` and `https://cms.example.com/cms` both give links under `https://cms.example.com/cms`. Another path, or a path without a path base, stops the host with `FCMS1101`. Tokens carry the issuer, so setting the origin or changing it refuses the access tokens, codes and refresh tokens issued before: users and clients sign in again.

Without it, those links and URLs are built from the request's scheme, host and path base, so a forged `Host` gets a reset link to the attacker's host emailed to a real user. Outside Development the host refuses to start without it (`FCMS1105`) unless `FluentCms:Security:AllowRequestOrigin` is true, which is for an instance whose `AllowedHosts` or proxy lets through only its own host names; the startup report then lists `public-origin-unset`. Set `AllowedHosts` as well, since the instance still answers any host that host filtering lets through.

## Hosted pages

The pages of the authorization server are Razor Pages under `/account`: login, logout, consent, two-factor, recovery code and two-factor setup, forgot and reset password, accept invitation, change password, external login and error. `AddFluentCms()` registers Razor Pages and `MapFluentCms()` maps them with `MapRazorPages()`, so a host adds nothing. That maps the host's own Razor Pages too, and a host that maps them itself still gets each page once. They carry the `/account` headers and the `auth` rate limit per client address, run no script, and send a `Content-Security-Policy` that allows inline styles, and images from the host or over https only.

The pages are served over https only, because their antiforgery cookie is `Secure` (see [security defaults](#security-defaults)). A page asked for over plain http, such as the host's own `http://localhost:5080` behind a tunnel, answers 403 with a page that says so and code `FCMS3108`, in every environment and whether or not the [public origin](#public-origin) is set. It is not redirected, since behind a proxy whose forwarded scheme the host does not trust every request arrives as http and a redirect would loop. Open the page at the https address. Behind a proxy that ends https, turn on `FluentCms:ReverseProxy:ForwardedHeaders` with that proxy among `KnownProxies` or `KnownNetworks` and `ForwardProto` on, so that its `X-Forwarded-Proto: https` makes the request https (see [reverse proxy](reverse-proxy.md)); to send plain http to https, turn on `FluentCms:ReverseProxy:HttpsRedirection`. A host that sets the antiforgery cookie's `SecurePolicy` to `SameAsRequest` after `AddFluentCms` gets the pages rendered over http, but not a sign-in: the sign-in cookies stay `Secure` and `/connect` answers https only.

Branding is the settings area `branding` (`BrandingSettings` in `FluentCMS.Core.Api.Account`): `Name` (default `FluentCMS`), `LogoUrl` (an https URL or a path of the host, such as `/media/logo.svg`), and the hex colours `PrimaryColor`, `BackgroundColor` and `TextColor`. The pages read the instance defaults, which need `settings.branding.manage` to change. To change more than that, override a page or the layout: add a Razor page at the same path to the host project, such as `Areas/FluentCms/Pages/Account/Login.cshtml` or `Areas/FluentCms/Pages/Shared/_Layout.cshtml`, with `@model FluentCMS.Core.Api.Account.LoginModel` for a page, so that it keeps the page's behaviour. A page of the host wins over the one of the same path in FluentCMS.

A wrong code on the two-factor or the recovery code page counts towards the lockout of `FluentCms:Users:Lockout`, as a wrong password does, and a locked-out user's recovery code is refused and stays unspent.

The lockout holds for a new sign-in only, since anyone who knows a user name can raise it. A user who is signed in already still changes the password on `/account/change-password` and still gives a two-factor code to enrol, to replace the recovery codes or to turn two-factor off; what that user's own sessions get wrong there is counted apart, wrong passwords and wrong codes each with the same limit and time. Either at its limit keeps those sessions out of both; a right password starts its count again, and a right code only the count of codes. Each guess takes its place in its count before it is checked, so guesses sent at once check no more than the limit ([#3447](https://github.com/fluentcms/FluentCMS/issues/3447)). Setting up two-factor through the Management API (`POST /me/two-factor/enrol` and `/verify`) needs a token whose sign-in is at most ten minutes old, and answers `FCMS3065` to an older one until the user signs in again.

A user who forgets the password gets a link by email that works once and for `FluentCms:Users:PasswordResetLifetime`. The page answers the same whether or not the address belongs to a user. An invitation links to `/account/accept-invitation`. Both links are built from the [public origin](#public-origin); without it they follow the request's scheme, host and path base, and a request with a forged `Host` header that `AllowedHosts` lets through gets a link to that host sent to a real user. Logins, failed and successful, are `user.login_failed` and `user.login_succeeded` activity rows with the client address, and an end of a session, a logout, a revocation or a password change among them, is a `user.sessions_ended` row with its reason and who ended it. When the row of a sign-in that ended cannot be written, the end still happens: a logout of a user or a member, a provider's remote sign-out, another sign-in over a live one (`replaced`) and a sign-in with the second factor (`second_factor`) go on as they otherwise do, and the server logs event 9012 (`SessionEndNotRecorded`, an error, with the pool, the id, the reason, the type of the failure and, for an error of the database, its `SqlState` and error code) instead. That line is then the only trace of the end, so an operator who audits sign-outs alerts on it. It withholds the exception's message and stack, which with EF Core's sensitive data logging can hold the row; EF Core logs the failed command or save itself, with the exception, under `Microsoft.EntityFrameworkCore.Database.Connection`, `Microsoft.EntityFrameworkCore.Database.Command` and `Microsoft.EntityFrameworkCore.Update`, where the host's log levels decide what is kept.

A user who lost the authenticator app and every recovery code is reset by an administrator with `POST /api/management/v1/users/{id}/two-factor/reset`. The administrator needs `users.manage`, every permission of the user's roles, and a sign-in with a second factor, so an API key cannot do it. The reset turns two-factor off, ends the user's sessions, and is a `user.two_factor_reset` activity row; a role that requires two-factor asks the user to enrol again at the next sign-in.

The instance keeps at least one active user with every permission, such as a Super Admin: deactivating that user, taking the role from it, or editing its role so that it grants less is refused with `FCMS3052`, also for an API key, and the attempt is a `user.change_refused` or `role.change_refused` activity row. Give the role to another user first.

## Admin

`FluentCMS.Admin.Hosting` serves the admin, a Blazor WebAssembly app, from the host at `/admin`, under `FluentCms:ReverseProxy:PathBase` when it is set. The host references the app's project, such as `FluentCMS.Admin.App`, so that its build and its publish compile the app and carry its files as the host's static web assets, and adds two calls:

```csharp
builder.Services.AddFluentCms(cms =>
{
    cms.UseSqlite(builder.Configuration.GetConnectionString("cms"));
    cms.AddAdmin();
});

var app = builder.Build();
app.UseFluentCms();
app.MapFluentCms();
app.MapFluentCmsAdmin();
```

`MapFluentCmsAdmin()` maps the host's static web assets under `/admin` with `MapStaticAssets()`, which serves each with its content type, a compressed copy by `Accept-Encoding`, and fingerprinted files cached for good; they are published at the root of the host's `wwwroot` and mapped under `/admin`, so a host with static web assets of its own finds them under `/admin` too (the reference host emits none). `/admin/` and every client route without a file extension, such as `/admin/system/components` or `/admin/authentication/login-callback`, answer the app's `index.html` with only the `href` of its `<base>` changed to the path base and `/admin/`; `/admin`, and an address in another case such as `/ADMIN/`, redirect to the address that base names, since the app's router takes no other. The page answers `GET` and `HEAD`, and any other method is a 405. A missing file, any path under `_framework/` or `_content/` among them, is a 404. `/admin/config.json` is built from the configuration below, and it and `index.html` are sent with `Cache-Control: no-store`; the app's sample `config.json` and its copies are never served. `MapFluentCmsAdmin()` throws `FCMS1104` before `UseFluentCms()`, and `FCMS1103` without `AddAdmin()`, without the manifest of static web assets, or when the app's `index.html` is missing or has no single `<base href>`, so such a host does not start.

| Key under `FluentCms:Admin` | Default | Meaning |
| --- | --- | --- |
| `Enabled` | `false` | Serves the admin and registers its client. Off, nothing is mapped: `/admin` is a 404 like any unknown path, no client is registered, and the client registered while it was on is deleted at start, which signs every admin user out (see below). It stays off until the admin is released (A-47). |
| `ApiBaseUrl` | the public origin | The `apiBaseUrl` of `config.json`, the origin and path base of the API, such as `https://cms.example.com`. Unset, it is `FluentCms:PublicOrigin` with the path base, or the request's scheme, host and path base in Development. |
| `Branding:Name` | `FluentCMS` | The page title and the name of the shell's logo. |
| `Branding:LogoUrl`, `Branding:FaviconUrl` | the built-in ones | An absolute `http` or `https` URL, whose origin the page's `img-src` then allows, or a path relative to the admin, such as `images/logo.svg`. |
| `Branding:AccentColor` | the theme's | A hex color: `#rgb`, `#rgba`, `#rrggbb` or `#rrggbbaa`. |
| `Cultures:Offered` | every shipped culture | The interface cultures the admin offers, each a culture name such as `fr-CA`, each once. |
| `Cultures:Default` | `en` | The culture the admin starts in, one of `Offered` when that is set. |
| `PreviewOrigins` | none | The origins the preview frame may load, such as `https://www.example.com`, without a path; none turns preview off. They are the page's `frame-src`. The admin's own origin is refused (`FCMS1101`), since a frame of it could reach the admin's session. |

`config.json` also carries `authority`, the issuer of the instance's authorization server exactly as its discovery document writes it (`https://cms.example.com/`, or `https://example.com/cms` under a path base), `clientId`, which is `cms.Clients.AdminClientId` (`FluentCms:Clients:AdminClientId`, `fluentcms-admin` by default), and the scopes `openid profile email offline_access management`. Each value is checked at start with the rules the admin reads `config.json` with, so a value it would refuse stops the host with `FCMS1101` and the key. That holds for the addresses built from `FluentCms:PublicOrigin` and `FluentCms:ReverseProxy:PathBase` too: the admin takes ASCII only, so write an internationalized host name in its `xn--` form, such as `https://xn--mnchen-3ya.example`, and keep the path base to letters, digits and `- . _ ~ /`, with no segment of dots only such as `/..` or `/.`, which a browser would read as a step out of the path. Outside Development the addresses come from `FluentCms:PublicOrigin` only, never from a request's `Host`, so the admin needs it there: `Enabled` without it stops the host with `FCMS1101`, also with [`AllowRequestOrigin`](#public-origin).

While `Enabled` is true, `AddAdmin()` registers the admin as a public client of the admin pool under `cms.Clients.AdminClientId`, which signs in with the code flow and PKCE: the redirect URI `{public origin}/admin/authentication/login-callback`, the post-logout URI `{public origin}/admin/`, and the scopes above. In Development without a public origin it registers those of each address the host listens on, from `urls` (`ASPNETCORE_URLS`) or `http_ports` and `https_ports`, with a wildcard host as `localhost`, and `http://localhost:5000` when none is set. A client that `FluentCms:Clients:Applications` or `cms.Clients.Applications` lists under that id wins whole, as for an admin served from another origin (`RedirectUris`, `PostLogoutRedirectUris` and `Scopes` of its own, with that origin in `FluentCms:Security:Cors:Management:AllowedOrigins`).

Turning the admin off deletes, at the next start, the client `AddAdmin()` registered while it was on, as the host deletes any client it registered and no longer lists, also when no other client is configured: the sessions and tokens of the admin's users end with it, so an admin tab still open is signed out, and turning the admin on again registers the client anew, after which users sign in again. When another client is listed, the host deletes it by the rules of the [authorization server](#authorization-server). When none is, `AddAdmin()` deletes it itself, and takes for its own any client under that id that was registered from the options, is no longer listed and has the display name `FluentCMS admin`, the one the registration gives. A delete the database refuses, as when a token arrives meanwhile from an instance with the admin still on, or fails, as on a database that cannot be written, leaves the client, its sessions and its tokens until a start whose delete succeeds, with a warning (event id 40). A client under that id that `FluentCms:Clients:Applications` or `cms.Clients.Applications` lists is kept, whatever its display name; one registered some other way, or one an operator configured before under another display name, is left to the rules of the authorization server. `Enabled` is off unless the configuration says otherwise, so a configuration that fails to load turns the admin off too and ends its sessions in the same way; users sign in again once it loads.

The page sends the [security headers](#security-headers-and-request-limits), `Cross-Origin-Opener-Policy: same-origin`, and this `Content-Security-Policy`, with the origins of the API, the authority, an absolute logo or favicon and the preview origins filled in:

```text
default-src 'none'; script-src 'self' 'wasm-unsafe-eval'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob: {api} {logo};
font-src 'self'; connect-src 'self' {api} {authority}; frame-src {preview origins}; base-uri 'self'; form-action 'self';
frame-ancestors 'none'
```

`'wasm-unsafe-eval'` runs the .NET runtime, and no other eval is allowed. `style-src` needs `'unsafe-inline'` because the component library writes a `<style>` element for the drawer and `style` attributes on the cells of the data grid and on its icons, which `'self'` blocks (#3148). `connect-src` allows `config.json` on the admin's own origin and the API and the authority on theirs; `img-src` allows the API's origin, where signed asset URLs point. `frame-src` is left out without preview origins, so no frame loads. In Development `connect-src` also allows `ws://localhost:*` and `wss://localhost:*`, the WebSocket of the script that `dotnet watch` adds to the page.

Serve nothing of other parties on the admin's origin. The admin keeps the refresh token and the unsaved drafts in `sessionStorage`, which every page of the same origin in the same tab can read, so a script on another page of that origin could take them; the hosted pages run no script, and the Scalar UI only the scripts it serves itself, each under its own policy. A host that serves its own pages, or files that users upload, on the API's origin serves them on the admin's too; put those on another origin, or serve the admin from one of its own ([an admin on another origin](planning/admin-technical-spec.md#64-an-admin-on-another-origin)).

Two loops run the admin locally, both against the reference host in Development, whose `src/FluentCMS.Host/appsettings.Development.json` turns the admin on, registers `fluentcms-admin` with the redirect URIs of both, and lists the app's origin in `FluentCms:Security:Cors:Management:AllowedOrigins`. The hosted pages need https, so both use the https addresses of the launch profiles, with the ASP.NET Core development certificate (`dotnet dev-certs https --trust`).

- `dotnet watch --project src/FluentCMS.Host` serves the referenced app at `https://localhost:5001/admin/`, rebuilding the host and the app as their files change.
- `dotnet watch --project admin/src/FluentCMS.Admin.App` serves the app alone at `https://localhost:5201/`, with hot reload, and its sample `wwwroot/config.json` points at `https://localhost:5001`, where `dotnet run --project src/FluentCMS.Host` runs the host.

## A surface of a package

A package that serves endpoints of its own from a FluentCMS host, outside the groups that `MapFluentCms()` maps, as `FluentCMS.Admin.Hosting` serves `/admin`, does it with public members of `FluentCMS.Core` (`FluentCmsSurfaceExtensions`, in `FluentCMS.Core.DependencyInjection`):

```csharp
public static IEndpointConventionBuilder MapReports(this IEndpointRouteBuilder endpoints)
{
    endpoints.EnsureFluentCmsPipeline(nameof(MapReports));
    var reports = endpoints.MapGroup("/reports").WithFluentCmsSurface("reports");
    reports.MapGet("/", ReportsPage).WithContentSecurityPolicy(context => "default-src 'self'; frame-ancestors 'none'");
    reports.MapGet("/reports.js", ReportsScript).WithFluentCmsStaticFiles();
    return reports;
}
```

- `WithFluentCmsSurface(name)` makes the endpoints FluentCMS endpoints of the surface `name`: they get the [security headers](#security-headers-and-request-limits), `Cache-Control: no-store` unless a response sets its own, the hold to the path base, the coarse rate limit before authentication, the request limits and the FluentCMS authentication, and `fluentcms.requests` counts them under that surface. The names of core's surfaces (`delivery`, `management`, `members`, `connect`, `account`, `health`, `docs`) are refused.
- `WithContentSecurityPolicy(policy)` sets the `Content-Security-Policy` of their responses, made per request; a response that sets one keeps it. It and `WithFluentCmsStaticFiles()` also take one `EndpointBuilder`, for a convention that tells the endpoints of a mapping apart, as the admin's does for those of `MapStaticAssets()`.
- `WithFluentCmsStaticFiles()` marks endpoints that serve files as they are. A marked endpoint is counted under `StaticFiles:PermitLimit` before authentication instead of the coarse limit (see [rate limits](#rate-limits)), and runs without the FluentCMS authentication: no key sent to it is looked up, and it has no caller. So the mark belongs only on files that read nothing of the instance, and never on a page, a configuration, a fallback or any endpoint that answers per caller; putting it on a dynamic endpoint is the package's risk. The same holds for a plugin's own endpoints that carry it.
- `EnsureFluentCmsPipeline(method)` stops the host with `FCMS1104` (`{method} found no FluentCMS pipeline. Call app.UseFluentCms() before app.{method}().`) when `UseFluentCms()` has not run.
- `services.AddFluentCmsOptions(section, instance, validate)` binds `FluentCms:{section}` onto `instance` as core binds its own [options](#options), and checks them at start; `validate` throws `FluentCmsConfigurationException.InvalidOption(message)`, with a message that starts with the property, and the host stops with `FCMS1101` and the key. `FluentCmsConfigurationException.NotRegistered(message)` is the `FCMS1103` refusal of something missing, such as a file the package serves.
- The `PublicOrigin` service gives the [public origin](#public-origin) as `Base` and the issuer as `Issuer`, and `ClientOptions.FromOptionsProperty` names the property of a client that `cms.Clients` registered.

A script that such a page runs on the admin's origin can read what the admin keeps in `sessionStorage` (see [Admin](#admin)).

## Single sign-on

Admin users can sign in with an external OpenID Connect provider on the hosted login page, which shows a button for each provider of `FluentCms:Sso:Providers`. A provider's name is 1 to 64 letters, digits, `-` or `_`; register `https://{host}{path base}/account/sso/{name}` as its redirect URI at the provider. With a [public origin](#public-origin), that URI is built from it, such as `https://cms.example.com/account/sso/corp`; without one, from the request. A name is found whatever its case: `Corp` and `corp` in configuration are one provider, and a login kept under one finds its user under the other, so changing only the case of a name keeps its users linked, with the redirect URI registered again in the new case. A name is not that of another authentication scheme in any case, such as `ApiKey`, `Preview`, `FluentCms`, the `member-{name}` of a [member provider](#members) or a scheme the host adds; such a name stops the host with `FCMS3070`, or `FCMS3180` when the member provider's scheme is the one refused. The name is the login provider of its users in `cms_user_login`, so do not rename a provider that has users: their logins would no longer be found. Microsoft Entra ID, Okta, Keycloak and any other OpenID Connect provider use the same handler, and `Preset` only builds the authority:

| `Preset` | Settings | Authority |
| --- | --- | --- |
| `Oidc` (default) | `Authority` | as given |
| `EntraId` | `TenantId`, one tenant (`common`, `organizations` and `consumers` are refused) | `https://login.microsoftonline.com/{TenantId}/v2.0` |
| `Okta` | `Domain`, and `AuthorizationServerId` for a custom authorization server | `https://{Domain}` or `https://{Domain}/oauth2/{AuthorizationServerId}` |
| `Keycloak` | `BaseUrl`, `Realm` | `{BaseUrl}/realms/{Realm}` |

`Authority` overrides what a preset builds. The sign-in uses the code flow with PKCE, sends `ClientSecret` when it is set, and asks for `openid profile email` and the `Scopes` listed. The authority must be https unless `RequireHttpsMetadata` is off. A host tunes a provider further with `services.Configure<OpenIdConnectOptions>("{name}", ...)`. Events it sets there, as `Events` or `EventsType`, run after FluentCMS's own, which check the provider's remote sign-out and keep the token's subject and session, and so does a delegate of `options.Events` set in a `PostConfigure`. A `PostConfigure` registered after `AddFluentCms` that replaces `Events` or sets `EventsType` would drop FluentCMS's events, so the host does not start (`FCMS3070`): set the events in `Configure`, wherever it is registered, or register the `PostConfigure` before `AddFluentCms`. A library that post-configures the options of every scheme is called before `AddFluentCms` for the same reason. FluentCMS's own part of an event covers the callback on the [public origin](#public-origin) too, so a delegate of `OnRedirectToIdentityProvider` keeps it. With environment variables, as in the reference host:

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

A provider's front-channel logout calls `https://{host}{path base}/account/sso/{name}/signout`. To end the admin session when the user signs out at the provider, register that URI as the provider's front-channel logout URI with the session required (`frontchannel_logout_session_required`), so that the provider sends `sid` and `iss`, and make sure its id tokens carry `sid`. A request ends a sign-in only when it sends each value once and both are those of the id token the sign-in was made with; anything else ends nothing, and a user signed in with a password or another provider stays signed in. A match also revokes the tokens issued in that session, as a logout here does. Most providers load the URI in an iframe on their own page, and a cross-site iframe does not carry the `SameSite=Lax` sign-in cookie, so such a logout ends nothing here unless the provider is on the same site as the host (a sibling subdomain, say) or the browser reaches the URI in a top-level GET navigation; otherwise the session lasts until the user logs out here or it expires.

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

**Mail and links.** A member's mail (verification, reset, the first password of a member an administrator made, and a note that an address is registered already) goes through `IEmailSender` (see [email](#email)). A link goes to your front end when the template is set: an absolute `http` or `https` URL with `{email}` and `{token}` after the host, such as `https://app.example.com/verify?email={email}&token={token}`; the front end then calls `POST /api/members/v1/verify-email` or `/reset-password` with the two values. Left unset, the link goes to the member page on the [public origin](#public-origin). A link is never built from a request, so with neither a template nor a public origin no mail is sent, the log says so (event id 9071, `FCMS3173`), and creating a member through the Management API is refused with `FCMS3173`. Resending the verification mail and a reset (of a member or an admin user) go out from jobs on the `mail` queue after the answer, so give the queue workers on at least one instance (two by default). An address gets one mail a minute of what its owner asks for and one of what a registration or a provider sign-in sends, kept in `IDistributedCache`: with several instances, register a shared one (see [provider slots](#provider-slots)) so the limit holds across them. An instance cannot tell that it has peers, so outside Development, while an email sender is set up and the cache is the in-memory one, the startup report lists `member-mail-limit-per-instance`, which says the limit holds per instance; one instance needs nothing.

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

**Direct login.** With `FluentCms:Members:AllowDirectLogin` and `AllowDirectLogin` on the client, both off by default, the client posts `grant_type=member_password` with `client_id`, `username` and `password` to `/connect/token` and gets the member's tokens. The app sees the password, so this is the less secure way in, for first-party apps that cannot open a browser; the startup report lists `member-direct-login` outside Development. Every refusal is `invalid_grant` with the same description, a wrong password counts towards the lockout, and the discovery document names the grant only while the option is on.

**What a member reads.** A member holds the Public role of the site, the built-in Member role and the roles of its member groups. A group is a named set of members bound to one role, of the instance or of a site; its name is lower-case letters, digits, `-` and `_`. The groups are read on every request, so a change holds from the next one, and a row rule reads them with `Principal.IsInGroup`. Only the principal a row rule is given carries the groups; in a hook, an event handler, a job or a plugin's endpoint `IsInGroup` throws `InvalidOperationException`, so a decision by group belongs in a row rule.

```csharp
cms.Authorization.AddRowRule<Perk>("perk-tier", ctx => perk => perk.Tier == (ctx.Principal.IsInGroup("gold") ? "gold" : "standard"));
```

**Managing members.** `/api/management/v1/members` and `/member-groups` need the instance-wide permission `members.manage`, which is apart from `users.manage`. An administrator lists members (`search`, `group`, `verified`, `active`), creates one, which mails it a link to choose its password, changes its display name, deactivates, reactivates and deletes it, lists and ends its sessions, and puts it in groups. Binding a role to a group, putting a member in a group or taking one out, and deleting a group need every permission of that role. A group whose role holds no permission is therefore managed with `members.manage` alone, so a row rule that must hold against such administrators, a negated one above all, reads a group whose role holds a permission they lack. A role is not deleted while a group is bound to it (`FCMS3044`). Deactivating or deleting a member ends its sessions at once. Each change is an activity row of no site: `member.registered`, `member.verified`, `member.updated`, `member.deactivated`, `member.deleted`, `member.group_changed`, `member.login_linked`, `member.login_unlinked`, `member_group.created`, `member_group.updated` and `member_group.deleted`, and sign-ins are `member.login_succeeded` and `member.login_failed` with the client address.

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

Google and Microsoft run on the ASP.NET Core handlers, GitHub and Apple on the `AspNet.Security.OAuth` handlers, and `Oidc` on the OpenID Connect handler with the code flow and PKCE. A host tunes a provider further with `services.Configure<GoogleOptions>("member-{name}", ...)` and the like. The events of an `Oidc` provider are kept as those of a single sign-on provider are: a host's own run after FluentCMS's, and replacing them in a `PostConfigure` registered after `AddFluentCms` stops the host (`FCMS3180`), with the same remedy. The events of a Google, Microsoft, GitHub or Apple provider, set as `Events`, as `EventsType` or as a delegate of `options.Events`, wherever and whenever they are set, keep FluentCMS's part of `RemoteFailure` and `TicketReceived`, which goes back to the member login page after an error at the provider and names the provider in the sign-in: that part runs first, then the host's. Every other event is the host's alone. Those of an Apple provider are an `AppleAuthenticationEvents`. The Apple package writes the client secret it signs, and the access, refresh and id tokens of each sign-in, to the log at `Trace`, all under the category `AspNet.Security.OAuth.Apple`; no category of FluentCMS holds them at any level. FluentCMS holds that category at `Information` or above, for every logging provider, unless the configuration names it or a category under it (`Logging:LogLevel:AspNet.Security.OAuth.Apple`, or the same under one provider), so a default of `Trace` does not reach it, and a provider kept above `Information` stays there. Naming it lower writes the secret and the tokens, and the startup report lists `member-apple-logging` while it is below `Information` outside Development. A host that replaces the logger factory, as Serilog's `UseSerilog` does, sets the level of the category in that logger's own configuration. The request log of ASP.NET Core (`Microsoft.AspNetCore.Hosting.Diagnostics`, at `Information`) writes the URL of each request, query included. The callback of a Google, Microsoft or GitHub provider is a GET, so its line holds the one-time `code` and the `state` the provider sent; Apple and `Oidc` providers, and the providers of [single sign-on](#single-sign-on), post their answer, so their line holds the path only. The code is redeemed once, as the callback arrives, and only with the client secret, so the query is left in the log; a host that keeps its logs where others read them keeps that category at `Warning`. A provider's logout paths are not served: signing out at the provider ends no member session.

**Who signs in.** A member linked to the account at the provider signs in, unless it is deactivated or locked out. Anyone else gets a member only while `AllowRegistration` is on: it is named by the provider's address and display name and has no password. With registration off the sign-in is refused. The provider signs in through the hosted pages and the authorization code flow only; the direct login takes no external login.

**Verified addresses.** Whether the address of a new member is verified is the provider's word. Google, Apple and `Oidc` say so with `email_verified`. From GitHub only the primary address is taken, and only when GitHub verified it, which is why `user:email` is always asked for; the address of the public profile is never used, and an account without a verified primary address makes no member. Microsoft does not say, so a member it makes gets the verification mail, which names the provider, and signs in with Microsoft once the address is verified; the same holds for an account of another provider whose address it does not say is verified.

**An address that is a member's already.** A provider never takes over a member by its address: the sign-in is refused, and the member signs in as before and links the provider itself. `LinkVerifiedEmail` on a provider links such a member at its first sign-in through it instead, only when the provider says the address is verified and the member may sign in now, its own address verified. Turn it on only for a provider you trust to verify addresses; it is not offered for Microsoft. A sign-in with an address the provider did not verify is answered the same whether or not the address is a member's. While the address of a member is not verified, nobody has shown whose it is, so a second claim of it removes what the first one left: when the address is registered again, or someone signs in through a provider with it and a login that is not the member's, the member's password and logins go, the links sent so far stop working, and the owner of the mailbox gets a mail with a link to choose a password, which verifies the address too. Nobody is signed in or linked by that sign-in, whatever the provider says of the address. A reset link used before the address was verified removes the member's logins as well.

**Linking and removing.** On `/account/members/logins` a signed-in member links a provider, which asks before it links and names the account at the provider, and removes one. `GET /api/members/v1/me/logins` lists `{ "provider", "displayName" }` and `DELETE /api/members/v1/me/logins/{provider}` removes one; an administrator does the same with `GET` and `DELETE /api/management/v1/members/{id}/logins` and `.../logins/{provider}` (`members.manage`). Removing a login gives the member a new security stamp and ends its sessions, access tokens included. The last way to sign in is not removed (409, `FCMS3184`): a login counts while its provider is configured, and a password while the member has one.

**Failures.** An error at the provider, or a callback that belongs to no sign-in started in that browser, goes back to the member login page as a failed sign-in, with a `member.login_failed` activity row whose method is the provider's name. The reason of a refusal is in the log with its code: `FCMS3181` no usable address, `FCMS3182` the address of a member that is not linked, `FCMS3183` the member could not be made, `FCMS3187` registration is off (event ids 9090 to 9094). A provider's callback counts towards the `auth` [rate limit](#rate-limits) of its client address.

CI tests the providers against stand-ins and calls no provider. [Verifying member sign-in against the real providers](member-sign-in-verification.md) records what the handlers have been run against and holds the walk-through for a run against Google, Entra ID and GitHub.

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

`cms.UseSqlite(connectionString)` opens a database file in WAL mode, where readers and the writer do not block each other, and gives every connection `synchronous = NORMAL` and a busy timeout of 5 seconds. While the database is open, SQLite keeps two more files beside it, `{name}-wal` and `{name}-shm`. Keep them with the database, and copy all three with the host stopped, since a copy of the database alone misses the commits still in `-wal`. Put the database on a local disk: WAL needs memory shared by every process that opens the file, which a network share such as NFS, SMB or Azure Files does not give, and SQLite's locks are not reliable there either. A file in the rollback journal, such as one made by another tool, is switched to WAL when the host starts. A file that cannot change mode keeps the rollback journal and `synchronous = FULL`. An in-memory database, and one opened with `Mode=ReadOnly`, are left as they are.

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
| `ISearchIndex` | none: `q=` is refused with `FCMS2211` until the [search plugin](#search) or the host fills it |

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
| `AllowPrivateNetworks` | `false` | Lets deliveries reach loopback, private, link-local and the other addresses that are not public. The startup report lists `webhooks-private-networks` while it is on outside Development. Turn it on only when every receiver is internal. |
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
| `MaxResultBytes` | `262144` | The largest list a content tool answers, in bytes as sent, its JSON as structured content and again as text. A longer one ends at an entry, with `truncated` and the cursor of the rest. A dry run's plan keeps to it by shortening its titles, or leaving them out. |
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
| `maxClients` | `1000` | How many MCP clients there may be at once, those an administrator registered included. A client's own registration beyond it is answered 503 in every mode; an administrator's is not refused. |
| `neverUsedClientHours` | `24` | After how many hours a client the `open` policy approved is removed when no user has consented to it. |
| `unusedClientDays` | `30` | After how many days a client the `open` policy approved is removed when it has no token or authorization left. |
| `maxConsentsPerUser` | `20` | How many clients the `open` policy approved one user may keep by consenting. A consent keeps such a client from the cleanup, so one that would keep one more is refused. A client counts while the user holds a consent to it, one ended by its session or a password change included until the prune removes it, and for the user who first consented to it while it is there; the user frees a place with `DELETE /api/management/v1/mcp/consents/{id}`. |

The role of a client limits its tokens: a call through the client is allowed what both the user's roles and the client's role allow. An administrator with `mcp.manage` for the instance lists the clients at `GET /api/management/v1/mcp/clients`, approves one with `POST /mcp/clients/{id}/approve` and `{ "role" }`, rejects one with `POST /mcp/clients/{id}/reject`, and removes one with its sessions and tokens with `DELETE /mcp/clients/{id}`. `POST /mcp/clients/remove` with `{ "registeredFrom", "registeredBefore" }`, each a time with an offset, removes the clients the `open` policy approved that registered in that span, 100 in a call, and none an administrator approved, registered or gave a role; it answers `{ "removed", "more" }`, and is sent again while `more` is true. For an agent that does not register itself, `POST /mcp/clients` with `{ "name", "redirectUris", "role" }` registers a client under the same rules, approved at once, whatever the policy; `PUT /mcp/clients/{id}/role` with `{ "role" }` changes the role of an approved client and ends its tokens and the users' consents to it, so that users sign in through it and consent again, and its next token carries the new role. A client that is not approved within 7 days is removed by the hourly job `mcp.client-cleanup`, and so is a client the `open` policy approved that no user consented to `neverUsedClientHours` after it registered, or that has no token or authorization `unusedClientDays` after it registered; one an administrator approved, registered or gave a role stays, and naming the role a client has is enough. The startup report lists `mcp-open-client-registration` while the policy is `open` outside Development; it is made once per start, so a change of the setting shows at the next one.

The guidelines, the type instructions and the stored prompts of a site reach only the agents of that site: a caller that may manage and holds some grant there, so not a Delivery key and not a principal whose roles are all in other sites. The plugin keeps them in two tables of its own, `cms_mcp_instruction` and `cms_mcp_prompt`. They are a [migration stream](plugin-migrations.md) of the plugin, `cms_mcp__migrations`, which the host applies with its other migrations (`FluentCms:Database:MigrateOnStartup`), so a host that already runs the plugin has one migration to apply after it updates.

## Multi-site

The multi-site plugin (`FluentCMS.Plugins.MultiSite`, which the reference host adds) creates and deletes sites and keeps the host names of each site, under `/api/management/v1/sites`, with `sites.manage` for the instance:

- `POST /sites` `{ "name", "slug", "defaultLocale" }` creates a site with its default locale and its Public role, and `DELETE /sites/{id}` deletes one that is not the default site and has no content;
- `GET` and `POST /sites/{id}/hosts` `{ "hostname", "isPrimary" }`, `PATCH /sites/{id}/hosts/{hostname}` `{ "isPrimary": true }` and `DELETE /sites/{id}/hosts/{hostname}` manage its host names.

A request that names no site by a key's binding, the header or the parameter reads the site of its host name, and the default site when no site has that name. A host name is a DNS name without a scheme, port, path or wildcard, and belongs to one site. A site with host names has one primary host, which a front end reads as its canonical name; the backend redirects nothing by it.

List the host names of every site in `AllowedHosts`. Behind a reverse proxy, the host name is the forwarded one only with `FluentCms:ReverseProxy:ForwardedHeaders:ForwardHost`. `FluentCms:PublicOrigin` stays the one address of the authorization server and the hosted pages, so a token works on every host name. Each instance keeps the host names for 30 seconds, so another instance sees a change within that time. The plugin has no options; its README has the codes.

`GET /api/management/v1/me/sites` lists the sites in which the caller holds a grant, with or without the plugin.

## Media

The media plugin (`FluentCMS.Plugins.Media`, which the reference host adds) keeps the assets and folders of each site under `/api/management/v1/media`, answers `/api/delivery/v1/media/{id}`, and serves signed image transforms at `/media/{id}/{fileName}`, original files at `/media/{id}/original/{fileName}` and the thumbnails its Management reads link to at `/media/{id}/thumbnail/{fileName}`. Its instance options are in `FluentCms:Media`:

| Key | Default | Meaning |
| --- | --- | --- |
| `Library` | `Shared` | `Shared`: every site reads and writes the media library of the default site, and each site's roles still decide who uploads and who reads there. `PerSite`: each site keeps its own library. An instance with several sites that each have assets sets `PerSite`. With `Shared`, the startup report lists `media-shared-library-hides-sites` and names those sites while a site other than the default still has assets of its own. |
| `Local:Root` | `App_Data/media` | The directory of the local storage, under the content root unless it is absolute. The startup report lists `media-local-storage` outside Development. |
| `MaxRequestBodySize` | `268435456` | The largest upload request, in bytes. |
| `RequireSignedUrls` | `true` | `false` serves unsigned transform URLs. The startup report lists `media-unsigned-urls` while it is off outside Development. |
| `SigningKey` | none | The base64 of at least 32 bytes that signs transform URLs. Left out, the plugin generates one key at its first start and stores it protected by Data Protection. Changing the key invalidates every URL signed before. |
| `CacheControl` | `public, max-age=86400` | The `Cache-Control` of a transformed image and of the original file and the thumbnail of a public asset. |
| `DeletedRetention` | `30.00:00:00` | How long a deleted asset keeps its file and its row before the daily cleanup purges them. |
| `MaxConcurrentDecodes` | `4` | How many images the instance decodes at once to make variants, thumbnails and copies, from 1 to 256. A decode at the default `maxSourcePixels` may take about 200 MB; requests beyond the limit wait. |

New uploads go to the local disk unless a storage package replaces it: `media.UseS3(section)` from `FluentCMS.Plugins.Media.S3` (AWS or an S3-compatible service such as MinIO) or `media.UseAzureBlob(section)` from `FluentCMS.Plugins.Media.AzureBlob` (a connection string, or a container URL with a SAS), in `cms.AddPlugin<MediaPlugin>(media => ...)`. The section's keys are in the plugin's [README](../src/plugins/FluentCMS.Plugins.Media/README.md#storage-on-s3-or-azure-blob). Objects stay private and are served through `/media` only, and assets written on the local disk before the move are still served from `Local:Root`. `/health/ready` includes `media-storage`, which is Unhealthy while the bucket or container cannot be reached.

The limits of uploads and transforms are each site's, in the settings area `media`. The default image processor is [SkiaSharp](https://github.com/mono/SkiaSharp) (MIT), with its Linux assets that need no fontconfig, for containers. A host replaces it with `cms.Providers.Use<IImageProcessor, T>()`, for example to use ImageSharp under its own Six Labors license. The plugin's [README](../src/plugins/FluentCMS.Plugins.Media/README.md#image-transforms) has the parameters and the signing.

## GraphQL

The GraphQL plugin (`FluentCMS.Plugins.GraphQL`, which the reference host adds) serves the content schema at `/graphql`, over the same content service, permissions and query model as the REST APIs. Its options are in `FluentCms:GraphQL`:

| Key | Default | Meaning |
| --- | --- | --- |
| `MaxDepth` | `12` | The deepest a query may nest its fields. |
| `MaxFieldCost` | `1000` | The most a query may cost by HotChocolate's cost analysis: 10 for a root field, and 1 for a populated relation or media field for each entry of the page above it. |
| `MaxTypeCost` | `1000` | The most objects a query may return, by the same analysis. |
| `MaxRequestSize` | `262144` | The body of a request, in bytes, refused with 413 and `FCMS3111` when larger, also when it is sent in chunks. `FluentCms:Limits:MaxBodySize` still caps it. |
| `ExecutionTimeout` | `00:00:30` | How long a request may run. |
| `OpenIntrospection` | unset | Whether any caller may introspect the schema. Unset, it is open in Development, and elsewhere needs a signed-in caller with `schema.read`. |
| `PersistedQueries` | unset | A folder of query files, each named `{id}.graphql`, that a request names by `id`. A relative path is under the content root. |
| `OnlyPersisted` | `false` | Refuses every query that is not in the folder. |

The page size and populate depth limits are those of `FluentCms:Query`. A Delivery key or an anonymous caller reads published entries and runs no mutation; a caller with the `management` scope runs the mutations its roles allow. Introspection names every type and field of the schema, whatever the caller may read, which is why it is closed outside Development ([ADR 0006](decisions/0006-one-graphql-schema-per-instance.md)). A browser on another origin can send `GET` only, since the endpoint answers the Delivery CORS policy. The plugin's [README](../src/plugins/FluentCMS.Plugins.GraphQL/README.md) has the schema, the queries, the mutations and the error codes.

## AI assist

The AI assist plugin (`FluentCMS.Plugins.AiAssist`, which the reference host adds) runs AI actions on the fields a site opted in, under `/api/management/v1/ai` with `ai.use`, and logs their usage. It is off until a site names a provider and opts fields in, in the settings area `ai`. The plugin's [README](../src/plugins/FluentCMS.Plugins.AiAssist/README.md) covers the settings of a site, the actions and a provider of the host's own. Its options are in `FluentCms:Ai`:

| Key | Default | Meaning |
| --- | --- | --- |
| `AllowedEndpoints` | empty | The endpoints a site may name in its settings, each an absolute `http` or `https` URL with no user, query or fragment, such as `FluentCms__Ai__AllowedEndpoints__0=http://ollama:11434/v1`. Empty leaves a site OpenAI at its own endpoint. A value that is not such a URL stops the host. |
| `{Provider}:ApiKey` | unset | The key of `OpenAi`, `AzureOpenAi` or `OpenAiCompatible` for the sites whose settings hold none. Give it through a secret store or the environment. A section `Anthropic` or `Ollama`, providers the plugin no longer has, stops the host (`FCMS1692`). |
| `{Provider}:Endpoint` | OpenAI's own for `OpenAi` | The endpoint the key of the provider is sent to, and to no other, such as `FluentCms__Ai__OpenAiCompatible__Endpoint=https://openrouter.ai/api/v1`. `AzureOpenAi` and `OpenAiCompatible` need it beside a key: a key without it, or a value that is not an endpoint, stops the host. |
| `SiteBilledEndpoints` | empty | The endpoints of `AllowedEndpoints` that bill the holder of the key, such as a public provider's, where a key a site sets is its own account. OpenAI's own endpoint is one without being named. Leave out every endpoint you run, such as Ollama, so that a key a site types there stays under `DailyTokenBudget`. |
| `DailyTokenBudget` | `0` | The prompt and completion tokens a day, from midnight UTC, of each site for its calls with the operator's credential: the key of the configuration or of the instance defaults, a keyless endpoint, any key at an endpoint not in `SiteBilledEndpoints`, or a factory of the host's own that does not say the credential is the site's. A site's `dailyTokenBudget` may lower it and cannot raise it or turn it off. 0 sets none. |
| `MaxInputLength` | `20000` | The most characters of text an action takes. |
| `UsageRetention` | `90.00:00:00` | How long the usage log keeps a row. |

The allow-list keeps a site administrator from pointing the server at an address inside the network: an endpoint is used only when the list holds it, it is checked at every call, and no redirect is followed. The list is read when the host starts, so a change to it applies after a restart. The configuration's key of a provider is sent to its `{Provider}:Endpoint` only, OpenAI's own for `OpenAi` unless another is named; a site that names another endpoint, even an allowed one, holds a key of its own, or is called with no key for `openAiCompatible`. Ollama is reached as `openAiCompatible` at its `/v1` endpoint, which needs no key. A key in the instance defaults of the `ai` settings is sent only to the provider and the endpoint those defaults name, also from a site that saved settings of its own without a key, which inherits it; a key replaced in the defaults reaches every site that inherits it. The calls of a principal are counted in memory on each instance, so with several instances a principal can make its `callsPerMinute` on each. The daily token budget counts the usage rows, which every instance writes, and the estimates of the calls in flight on the instance. A call with the operator's credential that ends before its provider reports its usage, or whose provider reports none, is written at its estimate, a token for each UTF-8 byte of the text, 25,600 for an image and `maxOutputTokens`, so a site passes its budget by at most one call of that estimate on each instance, as long as the provider counts no more than that. The image figure covers the models whose formula is published (gpt-4o-mini at high detail counts the 1024 by 1024 copy as 25,501); it is an estimate for them, not a bound for any model.

**Upgrading from 1.0.0-preview.1.** A host whose configuration holds `FluentCms:Ai:AzureOpenAi:ApiKey` or `FluentCms:Ai:OpenAiCompatible:ApiKey` no longer starts until it names the endpoint of that key in `FluentCms:Ai:AzureOpenAi:Endpoint` or `FluentCms:Ai:OpenAiCompatible:Endpoint` (#3300). A site that named another endpoint for the provider no longer gets that key: give it a key of its own. `FluentCms:Ai:OpenAi:ApiKey` needs nothing, since its endpoint is OpenAI's own by default. A key a site sets itself at an allowed endpoint other than OpenAI's now counts against `FluentCms:Ai:DailyTokenBudget` unless that endpoint is also in `FluentCms:Ai:SiteBilledEndpoints`, which holds only endpoints of `AllowedEndpoints`.

## Search

The search plugin (`FluentCMS.Plugins.Search`, which the reference host adds) fills the `ISearchIndex` slot, so `q=` on the Delivery and Management lists finds entries by the text of their fields marked `[Searchable]`: every word, the last as the start of a word, on SQLite (FTS5) and PostgreSQL (full-text search with a GIN index), and every word as a part of the text on SQL Server, which uses no full-text catalogue. The outbox keeps the index current from the entry events. It has no options. Run `POST /api/management/v1/search/rebuild` (permission `search.manage`) once when you add the plugin to a host that has content, and after a field becomes searchable or stops being. The plugin's [README](../src/plugins/FluentCMS.Plugins.Search/README.md) has the details.
