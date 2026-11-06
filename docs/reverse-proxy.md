# Reverse proxy and sub-path hosting

A FluentCMS host often sits behind a proxy that ends TLS, a CDN, or a path prefix such as `/cms`. `app.UseFluentCms()` handles all of it from the `FluentCms:ReverseProxy` section, in this order, before anything else in the pipeline: forwarded headers, the path base, HSTS, the HTTPS redirect. A host does not call `UseForwardedHeaders`, `UsePathBase`, `UseHsts` or `UseHttpsRedirection` itself.

## Options

| Key | Default | Meaning |
| --- | --- | --- |
| `PathBase` | none | The path the host is served under, such as `/cms`. A leading slash, no trailing one. |
| `ForwardedHeaders:Enabled` | `false` | Reads the `X-Forwarded-*` headers of a trusted proxy. |
| `ForwardedHeaders:ForwardFor` | `true` | `X-Forwarded-For` sets the client address. |
| `ForwardedHeaders:ForwardProto` | `true` | `X-Forwarded-Proto` sets the scheme. |
| `ForwardedHeaders:ForwardHost` | `false` | `X-Forwarded-Host` sets the host, when the host is in `AllowedHosts`. |
| `ForwardedHeaders:KnownProxies` | none | Addresses of the proxies to trust, written in full (`10.0.0.1`, not `10.1`). |
| `ForwardedHeaders:KnownNetworks` | none | Networks to trust, in CIDR notation with a full address (`10.0.0.0/8`). |
| `ForwardedHeaders:ForwardLimit` | `1` | How many proxies in a row may add an entry to `X-Forwarded-For`. |
| `HttpsRedirection:Enabled` | `false` | Redirects http requests to https. |
| `HttpsRedirection:Port` | none | The port of the https address in the redirect, such as `443`. Left out, the framework reads `HTTPS_PORT` or `ASPNETCORE_HTTPS_PORT`, then the https address of the server, and redirects nowhere when it finds none, as behind a proxy. |
| `Hsts:Enabled` | on outside Development | Sends `Strict-Transport-Security` on https responses. |
| `Hsts:MaxAge` | 30 days | The `max-age` of the header. |
| `Hsts:IncludeSubDomains` | `false` | Adds `includeSubDomains`. |
| `Hsts:Preload` | `false` | Adds `preload`. |

With no proxy and no network listed, only the loopback addresses are trusted, which is the framework's default. A proxy that is not on the list is ignored: its headers change nothing, and the address the host sees is the proxy's. Listing `0.0.0.0/0` trusts every caller, which lets any client choose its own address and scheme, and the startup report names it as an insecure default.

The client address matters beyond logs: the rate limits partition by it, so a host behind a proxy without forwarded headers puts every caller in one partition.

## Host names and the public origin

Host filtering (`AllowedHosts`) runs before the forwarded headers and checks the `Host` the proxy sent. With `ForwardHost` on, `X-Forwarded-Host` is therefore applied only for a host in `AllowedHosts`, and one outside it is ignored, so a forwarded host cannot get past host filtering. With `AllowedHosts` at `*` or unset, any forwarded host is applied.

Set `FluentCms:PublicOrigin` to the address users reach the instance at, such as `https://cms.example.com`, so that password reset and invitation links and the issuer do not depend on the host at all; see [public origin](host-configuration.md#public-origin). Its path is empty or `PathBase`, which is added when it is left out.

Environment variables use `__`, for example `FluentCms__ReverseProxy__ForwardedHeaders__Enabled=true`. ASP.NET Core adds its own forwarded headers middleware when `ASPNETCORE_FORWARDEDHEADERS_ENABLED` is set, and that trusts every address; leave it unset and use the options above.

## TLS and the redirect

When the proxy ends TLS, every request reaches the host as http. Turn on `ForwardedHeaders` with `ForwardProto` so the host sees https, and either leave `HttpsRedirection` off and let the proxy redirect, or turn it on once the scheme is forwarded. Turning the redirect on without forwarding the scheme redirects every request, including the ones that came in over https, in a loop. The startup report lists the redirect being off in Production as an insecure default, so a proxy that redirects for the host is a known and visible choice.

HSTS is sent only on https responses, so it needs the forwarded scheme as well. Browsers remember it for `MaxAge`: start short, and set `Preload` only when `IncludeSubDomains` is on, the age is at least a year, and the whole domain is ready to stay on https.

## Sub-path hosting

Set `PathBase` and keep the prefix when the proxy forwards the request:

```bash
FluentCms__ReverseProxy__PathBase=/cms
```

Every route of FluentCMS is then under it (`/cms/api/delivery/v1`, `/cms/api/management/v1`, `/cms/connect`, `/cms/account`, `/cms/health/live`, and any group added later) and the same routes without the prefix answer a 404 problem. Routes the host maps itself, such as its own endpoints, `/openapi/v1.json`, `/scalar` or the media routes, are not held to it: they answer under the prefix and without it. Read under the prefix, the OpenAPI document names it in its server and in the URLs of the authorization server. A proxy that strips the prefix before forwarding does not need the option, but then the host cannot build links that include it, and `FluentCms:PublicOrigin` takes a path only when it is `PathBase`. Health probes that call the host directly must use the prefixed path.

## Nginx

Nginx sets no forwarded headers by default:

```nginx
location / {
    proxy_pass http://127.0.0.1:5000;
    proxy_set_header Host $host;
    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Proto $scheme;
}
```

```json
{ "FluentCms": { "ReverseProxy": { "ForwardedHeaders": { "Enabled": true, "KnownProxies": ["127.0.0.1"] } } } }
```

Sub-path: use `location /cms/` and `proxy_pass http://127.0.0.1:5000;` with no URI after the port, which keeps the prefix, and set `PathBase` to `/cms`. A `proxy_pass` with a trailing slash strips it.

## Caddy

`reverse_proxy` sends `X-Forwarded-For`, `X-Forwarded-Proto` and `X-Forwarded-Host` itself:

```text
example.com {
    reverse_proxy 127.0.0.1:5000
}
```

Trust the address Caddy connects from, such as `127.0.0.1` or the container network, in `KnownProxies` or `KnownNetworks`. Sub-path: `handle /cms/* { reverse_proxy 127.0.0.1:5000 }` keeps the prefix (`handle_path` strips it), with `PathBase` set to `/cms`.

## YARP

YARP adds `X-Forwarded-For`, `X-Forwarded-Proto`, `X-Forwarded-Host` and `X-Forwarded-Prefix` by default. Trust the address of the YARP host:

```json
{ "FluentCms": { "ReverseProxy": { "ForwardedHeaders": { "Enabled": true, "KnownNetworks": ["10.0.0.0/8"] } } } }
```

Sub-path: match `/cms/{**catch-all}` in the route without a `PathRemovePrefix` transform, so the prefix reaches the host, and set `PathBase` to `/cms`.

## Azure Front Door

Front Door sends `X-Forwarded-For`, `X-Forwarded-Proto` and `X-Forwarded-Host`, and `X-Azure-FDID` with the id of the profile. Its requests come from the `AzureFrontDoor.Backend` service tag, whose current ranges Microsoft publishes; put them in `KnownNetworks`. Anyone who can reach the origin directly could otherwise send those headers, so also restrict the origin to that tag, or reject requests whose `X-Azure-FDID` is not your profile in the proxy or a middleware of the host. If another proxy sits between Front Door and the host, add it to `KnownProxies` and raise `ForwardLimit` to the number of hops. Set `ForwardHost` only when the origin is meant to see the public host name.

## Cloudflare

Cloudflare sends `X-Forwarded-For` (the client address first), `X-Forwarded-Proto`, `CF-Connecting-IP` and `CF-Visitor`; it keeps the original `Host`, so `ForwardHost` stays off. Cloudflare publishes its ranges at `https://www.cloudflare.com/ips/`; put them in `KnownNetworks`:

```json
{ "FluentCms": { "ReverseProxy": { "ForwardedHeaders": { "Enabled": true, "KnownNetworks": ["173.245.48.0/20", "103.21.244.0/22"] } } } }
```

Those two are the first entries of the published list, not the whole list. With a proxy of your own between Cloudflare and the host, add that proxy to `KnownProxies` and set `ForwardLimit` to `2`. A host reachable only through Cloudflare should also refuse other callers at the firewall.
