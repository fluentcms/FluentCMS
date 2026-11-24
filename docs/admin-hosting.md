# Hosting the admin

The admin is a Blazor WebAssembly app that runs in the browser and calls the Management API. The reference host serves it at `/admin` ([host configuration](host-configuration.md#admin)). This page is for the other way: the prebuilt static build, served by any static host or CDN from an origin of its own (admin spec 6.3 and 6.4).

Each GitHub release attaches the build as `fluentcms-admin-{version}.zip`. It holds the published files of `FluentCMS.Admin.App` with the first-party admin halves, trimmed, with a Brotli (`.br`) and a gzip (`.gz`) copy beside the files the browser downloads but `index.html` and `config.json`, which an operator edits, the sample `config.json`, `LICENSE`, `THIRD-PARTY-NOTICES.md` and a `README.txt` that points here. Every name under `_framework/` carries a fingerprint, such as `FluentCMS.Admin.App.5kku02qygn.wasm`, but `blazor.webassembly.js` and `dotnet.js`, which name the others.

## Edit the build

Unzip it into the folder the host serves, and edit two files:

- **`config.json`**: `apiBaseUrl`, the origin and path base of the API host, such as `https://cms.example.com`, and `authority`, the issuer exactly as the API host's discovery document writes it, which for the reference host is `apiBaseUrl` with a trailing slash (`https://cms.example.com/`). `clientId`, `branding`, `cultures` and `previewOrigins` are as in section 2.5 of the [admin technical specification](planning/admin-technical-spec.md#25-runtime-configuration-configjson).
- **`index.html`**: the `href` of its one `<base>` element, the path the admin is served from with a trailing slash: `/` at the root of its own host name, `/admin/` under `https://example.com/admin/`. Every address of the app resolves under it.

Edit nothing else: the names of the other files and their copies are fixed by the build. The admin reads `config.json` from its own base with `cache: no-store`, and shows a page that names a missing or wrong member before it calls the API.

## What any host must do

1. **Fall back to `index.html`.** A path under the base that is no file answers `index.html` with `200`, so that a reload or a link to a client route such as `/admin/content/article` opens the app. A missing file under `_framework/` or `_content/` answers `404` where the host can tell it apart; a host that falls back there too only hides a broken upload. No client route of the admin has a dot in its last segment (spec 4.1), so a host that can only tell a route from a file by its name may take a last segment without a dot for a route.
2. **Never store `index.html` and `config.json`.** Both, and every answer of the fallback, carry `Cache-Control: no-store`, so that an edit or a new release shows at the next load.
3. **Keep the fingerprinted files for good.** The names under `_framework/` but `blazor.webassembly.js` and `dotnet.js` carry `Cache-Control: public, max-age=31536000, immutable`, on a `200` only: a `404` kept for a year would outlive the fix. Every other file revalidates: `Cache-Control: no-cache`, or `no-store`, which costs a download per load.
4. **Send the right types.** `.wasm` is `application/wasm`, which the browser needs to compile it as it downloads; `.js` is `text/javascript`, `.json` `application/json`, `.css` `text/css`, `.svg` `image/svg+xml`, and `.dat`, the culture data, `application/octet-stream`.
5. **Serve the compressed copies by `Accept-Encoding`**, where the host can: the `.br` or `.gz` beside a file, with `Content-Encoding`, the type of the file itself and `Vary: Accept-Encoding`. The download size the admin is held to (spec 7) is its Brotli size. A host or CDN that compresses on its own may leave the copies out.
6. **Send the page's headers** on every answer:

   ```text
   Content-Security-Policy: default-src 'none'; script-src 'self' 'wasm-unsafe-eval'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob: {api}; font-src 'self'; connect-src 'self' {api} {authority}; frame-src {preview origins}; base-uri 'self'; form-action 'self'; frame-ancestors 'none'
   X-Content-Type-Options: nosniff
   Referrer-Policy: no-referrer
   Cross-Origin-Opener-Policy: same-origin
   ```

   `{api}` is the origin of `apiBaseUrl`, such as `https://cms.example.com`, where the signed URLs of images point; `{authority}` the origin of `authority` when it is another; add the origin of an absolute `logoUrl` or `faviconUrl` to `img-src`; and leave `frame-src` out without preview origins. This is the policy the reference host sends, with the API on another origin ([host configuration](host-configuration.md#admin) says why each source is there).
7. **Serve it over https** and serve nothing else on its origin. The sign-in keeps its refresh token and the unsaved drafts in `sessionStorage`, which any page of the same origin in the same tab can read, and outside `localhost` a browser gives the sign-in's cryptography to https pages only.

## The API host

An admin on another origin needs two settings on the API host (spec 6.4), here for an admin at `https://admin.example.com/admin/`:

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

The client is public, signs in with the code flow and PKCE on the API host's hosted pages, and gets no consent page. A client configured under `fluentcms-admin` wins whole over the one the hosting package registers, so an API host that serves the admin at `/admin` as well lists that admin's addresses beside these.

## After deploying

With `{base}` the address the admin is served at, such as `https://admin.example.com/admin/`, and an assembly's name from `_framework/`:

```bash
curl -sI {base}content/article                     # 200, text/html, Cache-Control: no-store, the headers of step 6
curl -sI {base}config.json                         # 200, application/json, Cache-Control: no-store
curl -sI {base}_framework/dotnet.js                # Cache-Control: no-cache
curl -sI -H 'Accept-Encoding: br, gzip' {base}_framework/FluentCMS.Admin.App.{fingerprint}.wasm
                                                   # application/wasm, max-age=31536000, immutable, Content-Encoding and Vary
curl -sI {base}_framework/missing.wasm             # 404, where the host can tell it apart
```

Then sign in: the browser's console shows no `Content-Security-Policy` violation, and an image of the media library shows.

## Recipes

Each recipe serves the build for the API at `https://cms.example.com`; put the API's origin of your own in the policy. A recipe marked as run served the unzipped build, with `config.json` and `<base href>` edited, in a container on one machine; the sign-in from another origin is proven by the end-to-end case of the static build, which serves it with a file server of its own. One that was not run needs an account that the project's CI has not, and says so. Report what you find on one that was not run.

### nginx

Run on 24 Nov 2026 with the official image `nginx:alpine` (nginx 1.31.10), the build at `/admin/`, on port 8080 in place of the TLS lines: the checks above passed, and the app started in a browser and stopped at the sign-in server of the example, which does not exist. The official `nginx` serves the `.gz` copies (`gzip_static`) and not the `.br` ones, which need the `ngx_brotli` module.

`/etc/nginx/fluentcms-admin-headers.conf`, which each location includes, since a location with an `add_header` of its own drops those of the server:

```nginx
add_header Content-Security-Policy "default-src 'none'; script-src 'self' 'wasm-unsafe-eval'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob: https://cms.example.com; font-src 'self'; connect-src 'self' https://cms.example.com; base-uri 'self'; form-action 'self'; frame-ancestors 'none'" always;
add_header X-Content-Type-Options "nosniff" always;
add_header Referrer-Policy "no-referrer" always;
add_header Cross-Origin-Opener-Policy "same-origin" always;
```

The site, with the build unzipped into `/srv/www/admin`:

```nginx
server {
    listen 443 ssl;
    server_name admin.example.com;
    ssl_certificate /etc/nginx/tls/admin.example.com.crt;
    ssl_certificate_key /etc/nginx/tls/admin.example.com.key;
    root /srv/www;

    gzip_static on;
    gzip_vary on;

    location = /admin {
        return 301 /admin/;
    }

    location /admin/ {
        include /etc/nginx/fluentcms-admin-headers.conf;
        add_header Cache-Control "no-cache" always;
        try_files $uri /admin/index.html;
    }

    location = /admin/index.html {
        include /etc/nginx/fluentcms-admin-headers.conf;
        add_header Cache-Control "no-store" always;
    }

    location = /admin/config.json {
        include /etc/nginx/fluentcms-admin-headers.conf;
        add_header Cache-Control "no-store" always;
    }

    location /admin/_framework/ {
        include /etc/nginx/fluentcms-admin-headers.conf;
        add_header Cache-Control "public, max-age=31536000, immutable";
        try_files $uri =404;
    }

    location ~ ^/admin/_framework/(blazor\.webassembly|dotnet)\.js$ {
        include /etc/nginx/fluentcms-admin-headers.conf;
        add_header Cache-Control "no-cache" always;
    }

    location /admin/_content/ {
        include /etc/nginx/fluentcms-admin-headers.conf;
        add_header Cache-Control "no-cache" always;
        try_files $uri =404;
    }
}
```

The `mime.types` of nginx gives `.wasm` its type; `.dat` takes the `default_type` of `nginx.conf`, `application/octet-stream`. The `Cache-Control` of `_framework/` has no `always`, so that a 404 there is not kept for a year.

### Caddy

Run on 24 Nov 2026 with the official image `caddy:alpine` (Caddy 2.11.9), the build at `/admin/`, with the site address `:8080` in place of the host name: the checks above passed, and the app started in a browser as with nginx. Caddy gets the certificate of the host name itself, and serves both copies (`precompressed br gzip`).

```caddyfile
admin.example.com {
    root * /srv/www
    redir /admin /admin/ 308

    handle /admin/* {
        header {
            Content-Security-Policy "default-src 'none'; script-src 'self' 'wasm-unsafe-eval'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob: https://cms.example.com; font-src 'self'; connect-src 'self' https://cms.example.com; base-uri 'self'; form-action 'self'; frame-ancestors 'none'"
            X-Content-Type-Options nosniff
            Referrer-Policy no-referrer
            Cross-Origin-Opener-Policy same-origin
        }

        route {
            @missing {
                path /admin/_framework/* /admin/_content/*
                not file
            }
            respond @missing 404

            try_files {path} /admin/index.html
            header Cache-Control no-cache
            header /admin/_framework/* Cache-Control "public, max-age=31536000, immutable"
            header /admin/_framework/blazor.webassembly.js Cache-Control no-cache
            header /admin/_framework/dotnet.js Cache-Control no-cache
            header /admin/ Cache-Control no-store
            header /admin/index.html Cache-Control no-store
            header /admin/config.json Cache-Control no-store
            file_server {
                precompressed br gzip
            }
        }
    }
}
```

Inside `route` the directives run in the order written, so the `Cache-Control` of `index.html` is the one of the path that `try_files` rewrote a client route to; the base itself, which `file_server` answers with its `index.html` when `try_files` takes the folder, is named too. A missing file under `_framework/` is answered by `respond` before any `Cache-Control` is set.

### Azure Static Web Apps

Not run: it needs an Azure subscription. The app serves from the root of its host name, so `<base href>` stays `/`. Put `staticwebapp.config.json` at the root of the build before deploying it, for example with `swa deploy`:

```json
{
  "navigationFallback": {
    "rewrite": "/index.html",
    "exclude": ["/_framework/*", "/_content/*"]
  },
  "routes": [
    { "route": "/_framework/blazor.webassembly.js", "headers": { "Cache-Control": "no-cache" } },
    { "route": "/_framework/dotnet.js", "headers": { "Cache-Control": "no-cache" } },
    { "route": "/_framework/*", "headers": { "Cache-Control": "public, max-age=31536000, immutable" } },
    { "route": "/_content/*", "headers": { "Cache-Control": "no-cache" } }
  ],
  "globalHeaders": {
    "Content-Security-Policy": "default-src 'none'; script-src 'self' 'wasm-unsafe-eval'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob: https://cms.example.com; font-src 'self'; connect-src 'self' https://cms.example.com; base-uri 'self'; form-action 'self'; frame-ancestors 'none'",
    "X-Content-Type-Options": "nosniff",
    "Referrer-Policy": "no-referrer",
    "Cross-Origin-Opener-Policy": "same-origin",
    "Cache-Control": "no-store"
  },
  "mimeTypes": {
    ".wasm": "application/wasm",
    ".dat": "application/octet-stream"
  }
}
```

`no-store` is the global `Cache-Control`, so that the base, `index.html`, `config.json` and the fallback's answers carry it whichever route the platform matches them by; the few small files at the root are not stored either, which step 3 allows. What was not run is whether a route's headers also reach a `404` it answers: check with the commands above that a missing file under `_framework/` answers `404` without `immutable`, and where it carries it, take the `/_framework/*` route out, so that the framework revalidates.

### Amazon S3 with CloudFront

Not run: it needs an AWS account. A private bucket behind a CloudFront distribution with origin access control; the build under the key prefix `admin/` is served at `https://admin.example.com/admin/`. CloudFront compresses on its own (**Compress objects automatically**), so the copies are left out of the upload, and each object carries its `Cache-Control` from the upload:

```bash
cd fluentcms-admin
aws s3 sync . s3://example-admin/admin/ --delete --exclude "*.br" --exclude "*.gz" --cache-control "no-cache"
aws s3 cp _framework s3://example-admin/admin/_framework/ --recursive --exclude "*.br" --exclude "*.gz" \
  --exclude "blazor.webassembly.js" --exclude "dotnet.js" --cache-control "public, max-age=31536000, immutable"
aws s3 cp _framework s3://example-admin/admin/_framework/ --recursive --exclude "*" --include "*.wasm" \
  --exclude "*.br" --exclude "*.gz" --content-type "application/wasm" --cache-control "public, max-age=31536000, immutable"
aws s3 cp index.html s3://example-admin/admin/index.html --cache-control "no-store"
aws s3 cp config.json s3://example-admin/admin/config.json --cache-control "no-store"
```

The fallback is a CloudFront Function on the viewer request of the distribution's behavior for `/admin/*`, which takes a last segment without a dot for a route (step 1):

```js
function handler(event) {
  var request = event.request;
  var uri = request.uri;
  var last = uri.substring(uri.lastIndexOf("/") + 1);
  if (uri.startsWith("/admin/") && !uri.startsWith("/admin/_framework/") && !uri.startsWith("/admin/_content/") && last.indexOf(".") === -1) {
    request.uri = "/admin/index.html";
  }
  return request;
}
```

A response headers policy on the same behavior adds the headers of step 6 as custom headers, without overriding the `Cache-Control` of the origin; the cache policy's minimum TTL is 0, so that `no-store` holds.

### Cloudflare Pages

Not run: it needs a Cloudflare account. A Pages project serves from the root of its host name, so `<base href>` stays `/`, and a project without a top-level `404.html`, as the build is, answers `index.html` for every path that is no file. Put `_headers` at the root of the build before uploading it, for example with `wrangler pages deploy`; a rule's `!` line drops a header an earlier rule set:

```text
/*
  Content-Security-Policy: default-src 'none'; script-src 'self' 'wasm-unsafe-eval'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob: https://cms.example.com; font-src 'self'; connect-src 'self' https://cms.example.com; base-uri 'self'; form-action 'self'; frame-ancestors 'none'
  X-Content-Type-Options: nosniff
  Referrer-Policy: no-referrer
  Cross-Origin-Opener-Policy: same-origin
  Cache-Control: no-store

/_framework/*
  ! Cache-Control
  Cache-Control: no-cache

/_content/*
  ! Cache-Control
  Cache-Control: no-cache
```

Pages compresses on its own. This recipe cannot meet steps 1 and 3 together: Pages answers a missing file under `_framework/` with `index.html` and a `200`, which the checks above show where a `404` is expected, and a rule that kept `_framework/*` for a year would keep that answer a year as well, under a fingerprinted name that the next deployment of the same build asks for again. So the framework revalidates here (`no-cache`), which costs a conditional request per file on each load.
