# Run with Docker

The reference host, `src/FluentCMS.Host`, is FluentCMS as one container: the Delivery and Management APIs, the authorization server and its hosted pages, and health probes. It is configured entirely by environment variables, so nothing has to be compiled to run it.

## Build and run

Build the image from the repository root:

```bash
docker build -f src/FluentCMS.Host/Dockerfile -t fluentcms .
```

The Dockerfile builds for the platform it runs on, and for both `linux/amd64` and `linux/arm64` with `docker buildx build --platform linux/amd64,linux/arm64`. The SDK compiles for the target on the build machine, so no emulation is needed. The image is `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra`: no shell and no package manager, ICU and time zone data for localized content and schedules, and the non-root user `app` (uid 1654).

The image runs in the Production environment, which refuses to start on the defaults that are only safe for a trial. Run with nothing set, it stops before it touches the database, exits with code 1 and prints one line per refusal, each naming the setting and the key that lifts it:

- `FCMS1102`: tokens would be signed with a key the server generates, because no signing certificate is configured.
- `FCMS1105`: links in password reset and invitation emails would follow the request's `Host`, because `FluentCms__PublicOrigin` is not set.
- `FCMS1106`: the Data Protection key ring would be stored in the database unencrypted.

[Before production](#before-production) fixes each of them. To evaluate the image, set the public origin and lift the other two explicitly:

```bash
docker run -d --name fluentcms -p 8080:8080 -v fluentcms-data:/data \
  -e FluentCms__PublicOrigin=https://cms.example.com \
  -e FluentCms__Security__AllowGeneratedSigningKey=true \
  -e FluentCms__Security__AllowUnencryptedKeyRing=true \
  fluentcms
```

`FluentCms__PublicOrigin` is the https address you reach the instance at through the proxy that ends TLS, which sign-in needs (see [before production](#before-production)). With no such address yet, `FluentCms__Security__AllowRequestOrigin=true` in its place builds links from each request. The startup report keeps listing each opt-in, as `generated-signing-key`, `data-protection-keys-unencrypted` or `public-origin-unset`.

It runs on SQLite, in `/data/fluentcms.db`, and applies the migrations on start. Keep `/data` on a volume, or the database goes with the container. `GET http://localhost:8080/health/ready` answers `Healthy` once the database and the job runner are up, and `docker ps` shows the container as healthy: the image's `HEALTHCHECK` runs the host itself with `--probe`, because a chiseled image has no `curl`.

## Content types

A new instance has no content types. Give it some in one of two ways:

- `FLUENTCMS_SAMPLE=blog` adds the Blog sample's types and seeds them with authors, tags and articles, in English and French. It is for trying FluentCMS out.
- `FLUENTCMS_SCHEMA=/config/schema.json` loads a schema file through the fluent builder. Mount it read-only and keep it under version control:

```bash
docker run -d -p 8080:8080 -v fluentcms-data:/data -v ./schema.json:/config/schema.json:ro -e FLUENTCMS_SCHEMA=/config/schema.json fluentcms
```

```json
{
  "collections": [
    {
      "name": "post",
      "localized": true,
      "fields": [
        { "name": "title", "type": "text", "required": true, "maxLength": 200, "localizable": true },
        { "name": "body", "type": "richText", "localizable": true },
        { "name": "publishedOn", "type": "dateTime", "indexed": true }
      ]
    }
  ],
  "singletons": [
    { "name": "site-settings", "fields": [ { "name": "tagline", "type": "text" } ] }
  ]
}
```

The format is the one `GET /api/management/v1/schema/snapshot` returns, so a snapshot of one instance is a schema file for another. A file with an error stops the container with every problem and its code, such as `FCMS1213` for a file that is not valid JSON or has an unknown property and `FCMS1203` for an unknown field type; a path that does not exist stops it with `FCMS3153`. Both may be set: the sample's types and the file's are then one schema.

A schema file has no compiled migrations, so the host creates and extends its tables on start, on SQLite only: a new type, a new optional field and a new index are applied, and a change that would drop or rewrite data, such as removing a field, changing its type or adding a required field, is logged as a warning and left undone. Such a change needs a new database for now; the CLI will generate migrations from a schema file. The startup report lists this as `schema-sync-outside-development`, a known choice of the image rather than a mistake.

## Environment variables

| Variable | Default | Meaning |
| --- | --- | --- |
| `FLUENTCMS_SCHEMA` | none | The path of a schema file in the container. |
| `FLUENTCMS_SAMPLE` | none | `blog` for the Blog sample. Any other value stops the host with `FCMS3152`. |
| `FluentCms__Database__Provider` | `Sqlite` | `Sqlite`, `PostgreSql` or `SqlServer`. Another value stops the host with `FCMS3150`. |
| `ConnectionStrings__FluentCms` | `Data Source=/data/fluentcms.db` | The connection string. PostgreSQL and SQL Server have no default and stop the host with `FCMS3151` without one. |
| `FluentCms__PublicOrigin` | none | The address users reach the instance at, such as `https://cms.example.com`. Password reset and invitation links and the issuer are built from it. See [the checklist](#before-production). |
| `AllowedHosts` | `*` | The host names the instance answers, separated by `;`. See [the checklist](#before-production). |
| `FluentCms__Security__SigningCertificate__Path` and `__Password` | none | The PKCS#12 file tokens are signed with. See [the checklist](#before-production). |
| `FluentCms__DataProtection__Certificate__Path` and `__Password` | none | The PKCS#12 file with an RSA key that the Data Protection key ring is encrypted with. A file that cannot be read stops the host with `FCMS3155`. See [the checklist](#before-production). |
| `FluentCms__Security__AllowGeneratedSigningKey` | `false` | `true` accepts the token signing key the server generates (`FCMS1102` otherwise). |
| `FluentCms__Security__AllowRequestOrigin` | `false` | `true` accepts an unset `FluentCms__PublicOrigin` (`FCMS1105` otherwise). |
| `FluentCms__Security__AllowUnencryptedKeyRing` | `false` | `true` accepts the key ring stored unencrypted (`FCMS1106` otherwise). |
| `FluentCms__Email__Smtp__Host` and the rest of `FluentCms__Email__Smtp__*` | none | The SMTP server that invitations and password resets go through; see [email](host-configuration.md#email). |
| `FluentCms__Sso__Providers__{name}__*` | none | An OpenID Connect provider that admin users sign in with; see [single sign-on](#single-sign-on). |
| `FluentCms__OpenApi__Enabled` | off | `true` serves the OpenAPI document at `/openapi/v1.json` and the Scalar UI at `/scalar`. See [the API description](#the-api-description). |
| `ASPNETCORE_HTTP_PORTS` | `8080` | The port the host listens on, from the base image. |

Every other option is a key of the `FluentCms` section, written with `__` for `:`, as [host configuration](host-configuration.md) lists them.

## The first administrator

A new instance has no users, and an invitation is sent by someone who can manage users. The operator is a confidential client with the Super Admin role, registered from the environment:

```bash
-e FluentCms__Clients__Applications__ops__Secret=<from your secret store>
-e FluentCms__Clients__Applications__ops__Scopes__0=management
-e "FluentCms__Clients__Applications__ops__Roles__0=Super Admin"
```

It gets a token with the client credentials grant at `/connect/token` (`grant_type=client_credentials`, `client_id=ops`, its secret, `scope=management`) and invites the first administrator with `POST /api/management/v1/invitations` `{ "email": "ada@example.com", "roleIds": ["<id of Super Admin from GET /roles>"] }`. The invitation is emailed, so set up SMTP first. The person follows the link, picks a user name and a password on the hosted page, and signs in from then on through the hosted login page with any client registered with `RedirectUris`. Remove the operator's variables, or keep its secret in a secret store, once it is no longer needed.

The Delivery API is closed until a role allows reading: create a role with `content.{type}.read` and a Delivery key bound to it with `POST /api/management/v1/api-keys` `{ "name", "class": "delivery", "roleId" }`, or grant those permissions to the site's Public role for anonymous reads.

## Single sign-on

Admin users can sign in with Microsoft Entra ID, Okta, Keycloak or any other OpenID Connect provider, configured from the environment. Register the image's public address followed by `/account/sso/{name}` as the redirect URI at the provider, such as `https://cms.example.com/account/sso/corp`; with `FluentCms__PublicOrigin` set, the instance sends exactly that. For Entra ID:

```bash
-e FluentCms__Sso__Providers__corp__Preset=EntraId
-e FluentCms__Sso__Providers__corp__TenantId=<directory id>
-e FluentCms__Sso__Providers__corp__ClientId=<application id>
-e FluentCms__Sso__Providers__corp__ClientSecret=<from your secret store>
-e FluentCms__Sso__Providers__corp__DefaultRole=Author
-e FluentCms__Sso__Providers__corp__RoleMappings__0__Group=<object id of the admins group>
-e "FluentCms__Sso__Providers__corp__RoleMappings__0__Role=Super Admin"
```

Okta takes `Preset=Okta` and `Domain` (with `AuthorizationServerId` for a custom authorization server), Keycloak `Preset=Keycloak`, `BaseUrl` and `Realm`, and any other provider `Authority`. A first sign-in creates the user with `DefaultRole` and the roles its groups map to, and those mapped roles follow the groups at every sign-in; a mapping that names Super Admin, as above, gives the first administrator without an invitation. `FluentCms__Sso__DisableLocalLogin=true` hides the password form and refuses password sign-ins; invited users then accept their invitation through the provider, whose address for them must be the one invited. `LinkExistingUsers=true` on a provider links an existing user at its first sign-in through it when the token says the address is verified. A provider's own multi-factor claim counts for a role that requires two-factor only with `TrustMfaClaim=true` on that provider. See [single sign-on](host-configuration.md#single-sign-on) for every setting.

## The API description

The image serves no OpenAPI document and no Scalar UI, because the document names every content type, field, component and block of the schema to anyone who can reach it, whatever that caller may read. `FluentCms__OpenApi__Enabled=true` serves the document of the instance at `/openapi/v1.json` and the Scalar UI at `/scalar/`, which is handy on a private network or while evaluating. Before exposing them more widely, put them behind an authorization policy: a host of your own built on `AddFluentCms()` does that with `app.MapFluentCmsOpenApi().RequireAuthorization("docs")`, and with the image, keep them off or limit who reaches those paths at the proxy. See [OpenAPI and the Scalar UI](host-configuration.md#openapi-and-the-scalar-ui).

## Before production

- **TLS.** The authorization server, and so sign-in, answers HTTPS only, and the image listens on plain HTTP. Put it behind a proxy that ends TLS and forward the scheme with `FluentCms__ReverseProxy__ForwardedHeaders__Enabled=true` and the proxy's address in `FluentCms__ReverseProxy__ForwardedHeaders__KnownProxies__0` or its network in `KnownNetworks__0`; see [reverse proxy](reverse-proxy.md).
- **Public address.** Set `FluentCms__PublicOrigin` to the address users reach the instance at, such as `https://cms.example.com`, with the path base when there is one. Password reset and invitation links and the issuer are then built from it, and tokens issued before it was set stop working; without it they follow the request's host, so a request with a forged `Host` header gets a link to the attacker's host emailed to a real user. That is why the host refuses to start without it (`FCMS1105`) unless `FluentCms__Security__AllowRequestOrigin=true`.
- **Host names.** Set `AllowedHosts` to the host names of the instance, such as `AllowedHosts=cms.example.com;localhost`, so that the instance answers no other host and a proxy's `X-Forwarded-Host` is applied only for those. Keep `localhost` in the list, or the `HEALTHCHECK`, which calls `localhost`, is turned away with 400.
- **Health probes.** `/health/live` and `/health/ready` answer without credentials, because the `HEALTHCHECK` and an orchestrator's probes call them. They never look up an API key and are limited per client address, apart from the API (`FluentCms__RateLimits__Health__PermitLimit`, 1200 a minute, far above what an orchestrator sends), so a flood of probes stays cheap. Still let only the orchestrator and the load balancer reach `/health` where you can, for example with a rule at the proxy or a network policy; the `HEALTHCHECK` calls `localhost` inside the container and needs no route from outside.
- **Signing key.** Mount a PKCS#12 (`.pfx`) file with an RSA key read-only, such as `-v ./certs:/certs:ro`, and set `FluentCms__Security__SigningCertificate__Path=/certs/signing.pfx` and `FluentCms__Security__SigningCertificate__Password` from your secret store. Drop `FluentCms__Security__AllowGeneratedSigningKey` from an evaluation setup, so that settings that lose the certificate stop the host (`FCMS1102`) instead of falling back to the generated key.
- **Key ring.** The Data Protection key ring protects the generated keys, the sign-in cookie and reset tokens, and is kept in the database, so a reader of the database could forge a sign-in. Mount a second PKCS#12 file with an RSA key and set `FluentCms__DataProtection__Certificate__Path=/certs/ring.pfx` and `FluentCms__DataProtection__Certificate__Password` from your secret store; the ring is then encrypted for that certificate, and `FluentCms__Security__AllowUnencryptedKeyRing` can go. Keys written before stay readable. With Azure Key Vault, keep the certificate there and let the platform mount it as that file, such as the Secrets Store CSI driver on AKS or a secret volume on Azure Container Apps. To wrap the ring with a Key Vault key instead, build a host of your own on `AddFluentCms` with `ProtectKeysWithAzureKeyVault`, as [the authorization server](host-configuration.md#authorization-server) shows.
- **Email.** Set `FluentCms__Email__Smtp__Host`, `__Port`, `__UserName`, `__Password` and `__From`; without them invitations and password resets fail with `FCMS3008`.

Together, behind a proxy that ends TLS:

```bash
docker run -d --name fluentcms -p 8080:8080 -v fluentcms-data:/data -v ./certs:/certs:ro \
  -e FluentCms__PublicOrigin=https://cms.example.com \
  -e "AllowedHosts=cms.example.com;localhost" \
  -e FluentCms__ReverseProxy__ForwardedHeaders__Enabled=true \
  -e FluentCms__ReverseProxy__ForwardedHeaders__KnownProxies__0=<address of the proxy> \
  -e FluentCms__Security__SigningCertificate__Path=/certs/signing.pfx \
  -e FluentCms__Security__SigningCertificate__Password=<from your secret store> \
  -e FluentCms__DataProtection__Certificate__Path=/certs/ring.pfx \
  -e FluentCms__DataProtection__Certificate__Password=<from your secret store> \
  -e FluentCms__Email__Smtp__Host=smtp.example.com \
  -e "FluentCms__Email__Smtp__From=FluentCMS <cms@example.com>" \
  fluentcms
```

The startup report, logged at start and served at `GET /api/management/v1/system/diagnostics`, lists each of these while it is still a default or lifted by an opt-in. Keep the database and its backups as secrets all the same: they still hold the key that encrypts codes and refresh tokens, and the sessions.

## Other databases

`FluentCms__Database__Provider=PostgreSql` or `SqlServer` with `ConnectionStrings__FluentCms` runs on that server. Those providers do not migrate on start by default, so run the system migrations before the host starts, with `FluentCms__Database__MigrateOnStartup=true` on one instance or with `fluentcms migrate` from an init container once the CLI ships. They keep content tables on reviewed migrations and never create them from the schema on start, so `FLUENTCMS_SCHEMA` and `FLUENTCMS_SAMPLE` stop the host on them with `FCMS3154`. Until the CLI generates migrations from a schema file, the image runs a schema file on SQLite only, and these providers are tested for real with #2711.

A compose file with PostgreSQL and MinIO comes with #2711 and the media plugin.
