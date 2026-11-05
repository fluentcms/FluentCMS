# Host configuration

A host registers the engine with `AddFluentCms()` and puts its HTTP surfaces on the routes with `MapFluentCms()`:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddFluentCms(cms => cms.UseSqlite(builder.Configuration.GetConnectionString("cms")));

var app = builder.Build();
app.MapFluentCms();
app.Run();
```

`MapFluentCms()` creates the route groups `/api/delivery/v1`, `/api/management/v1`, `/connect`, `/account` and `/health`. They are relative to the request's `PathBase`, so a host on a sub-path calls `app.UsePathBase("/cms")` and then `app.UseRouting()` before it. The builder it returns applies conventions to every FluentCMS endpoint.

## Requests

An endpoint runs as the principal of the `FluentCms` authentication scheme, which `AddFluentCms()` registers: the API key handler when a key is sent, otherwise the bearer scheme set in `FluentCmsAuthenticationOptions.BearerScheme`, and anonymous when neither applies. The handler writes `sub` (the id), `name`, `fcms_kind` (a `PrincipalKind` name), `roles` and `scope` (both space-separated), and `fcms_sites` for an API key bound to sites. A request without an authenticated user runs as the anonymous principal, which holds the Public role of its site, so anonymous Delivery access is what that role grants. A principal without a usable `fcms_kind` or `sub` is refused with 401.

The site is the key's binding, then the `X-Cms-Site` header, then the `site` parameter (an id or a slug), then the default site. A key bound to one site reads that site. A key bound to several reads the one the request names, or the default site when it is among them, and is otherwise refused with `FCMS3122`. A request that names a site outside the binding, or one that does not exist, is refused with `FCMS3121` (403). A header and a parameter that name different sites are refused. The locale is the `locale` parameter, then the `X-Cms-Locale` header, then the site's default; `locale=all` returns every locale of an entry as a map.

## Options

Everything is read from the `FluentCms` section of the host's configuration, so any configuration provider works. Environment variables use `__` for `:`, as in `FluentCms__Database__MigrateOnStartup=false`.

| Section | Holds |
| --- | --- |
| `FluentCms:Database` | `MigrateOnStartup`, `SchemaSync` (see [content migrations](content-migrations.md)) |
| `FluentCms:Security` | `AllowGeneratedSigningKey`, `Cors:Delivery:AllowedOrigins`, `Cors:Management:AllowedOrigins` |
| `FluentCms:Query` | `DefaultPageSize`, `MaxPageSize`, `MaxPopulateDepth`, `MaxFilterNodes`, `MaxPopulatedEntries` |
| `FluentCms:Jobs` | the job runner: `Queues`, `PollInterval`, `LeaseDuration`, retries, retention |
| `FluentCms:Events:Outbox` | the outbox: `BatchSize`, `MaxAttempts`, `LeaseDuration`, retries, retention |
| `FluentCms:Activity` | the activity log: `Enabled`, `Retention`, `SiteRetention`, `PruneSchedule` |

A key the configuration sets wins over what the host set in code inside `AddFluentCms`; a key it leaves out keeps the code's value. The options are checked when the host starts, and an invalid value stops it with an `FCMS` code and the key, for example `FCMS1101: FluentCms:Query:MaxPageSize: The field MaxPageSize must be between 1 and 2147483647.`

A CORS origin is an `http` or `https` origin with no path, such as `https://app.example.com`. The lists are empty by default, which allows no browser origin, and a wildcard is refused.

## Security defaults

Cookie authentication schemes and the antiforgery cookie are `Secure` and `HttpOnly`, and authentication cookies are `SameSite=Lax`. A host that configures a cookie after `AddFluentCms` overrides this.

## Production refusals

In the Production environment some settings that are fine in development stop the host before it touches the database:

| Code | Refusal | Lifted by |
| --- | --- | --- |
| `FCMS1102` | Tokens are signed with a generated key, because no signing certificate is configured | `FluentCms:Security:AllowGeneratedSigningKey = true` |

## API keys

`AddFluentCms()` registers the `ApiKey` authentication scheme. It reads a key from `Authorization: ApiKey <key>` or from the `X-Api-Key` header, and leaves a request with neither to the other schemes. A request with a key and any other credential is refused. The FluentCMS endpoints authenticate with the `FluentCms` scheme, which uses `ApiKey` when the request carries a key and otherwise the bearer scheme of the authorization server. It never uses the host's default scheme, so a cookie is not a credential for the APIs. The host calls `app.UseAuthentication()` before `MapFluentCms()`. A key's `last_used_at` is written at most once every five minutes.
