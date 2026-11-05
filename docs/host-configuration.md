# Host configuration

A host registers the engine with `AddFluentCms()` and puts its HTTP surfaces on the routes with `MapFluentCms()`:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddFluentCms(cms => cms.UseSqlite(builder.Configuration.GetConnectionString("cms")));

var app = builder.Build();
app.UseFluentCms();
app.MapFluentCms();
app.Run();
```

`MapFluentCms()` creates the route groups `/api/delivery/v1`, `/api/management/v1`, `/connect`, `/account` and `/health`. They are relative to the request's `PathBase`, so a host on a sub-path calls `app.UsePathBase("/cms")` before `UseFluentCms()`. The builder it returns applies conventions to every FluentCMS endpoint.

`UseFluentCms()` puts the request pipeline in place, in the order it works in: the exception handler (problem details), routing, security headers, CORS, authentication, the rate limiter and the request limits. Call it once, after `UsePathBase` and before `MapFluentCms()`, which needs it (the endpoints carry CORS and rate limit metadata that only these middleware read), and add `UseAuthorization()` after it. It includes `UseExceptionHandler()`, `UseRouting()` and `UseAuthentication()`, so a host does not add them again. The order matters: CORS comes before the rate limiter so that a 429 still carries CORS headers, and authentication comes before it because it partitions by API key or principal.

## Options

Everything is read from the `FluentCms` section of the host's configuration, so any configuration provider works. Environment variables use `__` for `:`, as in `FluentCms__Database__MigrateOnStartup=false`.

| Section | Holds |
| --- | --- |
| `FluentCms:Database` | `MigrateOnStartup`, `SchemaSync` (see [content migrations](content-migrations.md)) |
| `FluentCms:Security` | `AllowGeneratedSigningKey`, `Cors:Delivery:AllowedOrigins`, `Cors:Management:AllowedOrigins`, `ReferrerPolicy`, `FrameAncestors` |
| `FluentCms:RateLimits` | `Delivery:PermitLimit`, `Management:PermitLimit`, `Auth:PermitLimit` |
| `FluentCms:Limits` | `MaxBodySize` |
| `FluentCms:Query` | `DefaultPageSize`, `MaxPageSize`, `MaxPopulateDepth`, `MaxFilterNodes`, `MaxPopulatedEntries` |
| `FluentCms:Jobs` | the job runner: `Queues`, `PollInterval`, `LeaseDuration`, retries, retention |
| `FluentCms:Events:Outbox` | the outbox: `BatchSize`, `MaxAttempts`, `LeaseDuration`, retries, retention |
| `FluentCms:Activity` | the activity log: `Enabled`, `Retention`, `SiteRetention`, `PruneSchedule` |

A key the configuration sets wins over what the host set in code inside `AddFluentCms`; a key it leaves out keeps the code's value. The options are checked when the host starts, and an invalid value stops it with an `FCMS` code and the key, for example `FCMS1101: FluentCms:Query:MaxPageSize: The field MaxPageSize must be between 1 and 2147483647.`

A CORS origin is an `http` or `https` origin with no path, such as `https://app.example.com`. The lists are empty by default, which allows no browser origin, and a wildcard is refused.

## Security defaults

Cookie authentication schemes and the antiforgery cookie are `Secure` and `HttpOnly`, and authentication cookies are `SameSite=Lax`. A host that configures a cookie after `AddFluentCms` overrides this.

## Rate limits

ASP.NET Core's rate limiter runs three policies, each a fixed window of one minute. The groups carry their policy as endpoint metadata:

| Policy | Group | Partitioned by | Default per minute |
| --- | --- | --- | --- |
| `delivery` | `/api/delivery/v1` | the API key's `sub`, or the client address | 600 |
| `management` | `/api/management/v1` | the principal's `sub`, or the client address when there is none | 300 |
| `auth` | `/connect`, `/account` | the client address | 20 |

An API key whose principal has an `fcms_rate_limit` claim gets that many requests per minute instead of the `delivery` default. `/health` is not limited.

A response carries `RateLimit-Limit`, `RateLimit-Remaining` and `RateLimit-Reset` (seconds to the end of the window), from the IETF draft. A refused request is a 429 `application/problem+json` with code `FCMS3110` and a `Retry-After` in seconds.

The client address is the connection's remote address. Behind a proxy, call `app.UseForwardedHeaders()` with the proxies you trust before `UseFluentCms()`, or every caller shares the proxy's address.

## CORS

`Cors:Delivery:AllowedOrigins` applies to `/api/delivery/v1`, which allows `GET` and `HEAD`. `Cors:Management:AllowedOrigins` applies to `/api/management/v1` and to `/connect`, so the admin application can sign in from its own origin. An origin that is not on the list gets no CORS headers, and an empty list, the default, allows none. The lists come from configuration only; a setting that edits them at run time is not part of this release.

## Security headers and request limits

Every FluentCMS response carries `X-Content-Type-Options: nosniff` and `Referrer-Policy` (`FluentCms:Security:ReferrerPolicy`, default `no-referrer`). The `/account` pages, which are the hosted pages, also carry `Content-Security-Policy: frame-ancestors ...` from `FluentCms:Security:FrameAncestors`, default `'none'`. A host's own endpoints are not touched.

A request to a FluentCMS endpoint with a body larger than `FluentCms:Limits:MaxBodySize` bytes (default 2 MiB) is refused with 413 and code `FCMS3111`, whether the size is declared or only found out while the body is read. The page size, populate depth and filter node caps are the engine's `FluentCms:Query` options; a query over one comes back as a 400 problem with code `FCMS2207`.

## Production refusals

In the Production environment some settings that are fine in development stop the host before it touches the database:

| Code | Refusal | Lifted by |
| --- | --- | --- |
| `FCMS1102` | Tokens are signed with a generated key, because no signing certificate is configured | `FluentCms:Security:AllowGeneratedSigningKey = true` |
