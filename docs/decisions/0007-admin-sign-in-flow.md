# 0007 The admin's own code flow with PKCE, without the framework's authentication package

## Status

Accepted.

## Date

2026-11-16

## Context

The admin is the public OAuth client `fluentcms-admin` of the authorization server in the API host (decisions 7 and 30). It signs in with the authorization code flow and PKCE, renews with rotating refresh tokens, signs out at the server, and keeps an editor's unsaved form when a session ends (row 3.3). Section 2.4 of the [admin technical specification](../planning/admin-technical-spec.md) and its AQ2 and AQ3 set the design; [#3145](https://github.com/fluentcms/FluentCMS/issues/3145) builds it.

Options considered:

- **A. `Microsoft.AspNetCore.Components.WebAssembly.Authentication`.** The framework's package runs the flow through `oidc-client-js`, a script of its own that it loads into the page. It renews the access token in a hidden iframe with `prompt=none`, and it keeps its state in `sessionStorage` under its own keys.
- **B. The flow written in the admin, in C#.** Discovery, the authorize redirect, the code exchange, the refresh, the revocation and the end-session redirect, each an HTTP request or a whole-page navigation, over the admin's own `HttpClient` and `NavigationManager`.

Option A would cost:

- **Renewal that cannot work.** The hosted pages send `frame-ancestors 'none'` (`FluentCms:Security:FrameAncestors`), so the hidden iframe is refused and the session would end when the first access token expires. Turning the header off for the admin would open the login page to clickjacking.
- **A script outside the allow-list.** Spec 3.1 allows JavaScript only in named types and modules, and a strict `Content-Security-Policy` on the admin page (AQ3). The package's script, its `AuthenticationService.js`, would be a second script of the sign-in to review, pin and allow.
- **Behaviour the admin does not control.** Where the tokens are kept, what is refreshed when, and what happens on a refused refresh are the package's. The admin needs a refresh on a 401 inside its own handler chain, single flight within a tab, and the unsaved forms written before the redirect when the session ends; the package offers none of the three.
- **Size.** The package and its script add to the first download, which has a budget (spec 7).

Option B needs SHA-256 and random bytes for PKCE, which the runtime supports on WebAssembly (`SHA256.HashData`, `RandomNumberGenerator`), and no new package.

## Decision

We will write the sign-in in the admin, in C#, as `AdminSession` in `FluentCMS.Admin`, and not use `Microsoft.AspNetCore.Components.WebAssembly.Authentication`. It renews with refresh tokens only, keeps the access token in memory and the refresh token in the tab's `sessionStorage`, and is tested against a fake token endpoint in the component tests and against `FluentCmsTestHost` in the API tests.

## Consequences

- The flow has no iframe and no script of its own; its only interop is `SessionStorageInterop`, which joins the allow-list of spec 3.1.
- The id token's signature is not checked. The .NET runtime on WebAssembly has no RSA (it has the SHA family, HMAC, AES-CBC, PBKDF2, HKDF and the random generator); the browser's SubtleCrypto has it, at the cost of more interop. The token comes from the token endpoint over TLS in answer to the tab's own code and PKCE verifier, which OpenID Connect Core 3.1.3.7 accepts in place of the signature. Its issuer, nonce, expiry and subject are checked, and its audience is the client alone, or one of several only with the client as `azp`. A use of the id token beyond naming the signed-in user needs the signature and a library that checks it.
- The admin owns what a library would have tested for it: state and nonce handling, the return route, single flight, the replay of a code. Its tests cover each.
- A tab holds its own refresh token. A new tab signs in through the authorize redirect, which the server's session answers without a prompt; a tab duplicated by the browser copies `sessionStorage`, and the refresh of one after the other's is a reuse of the refresh token. Outside the 30 seconds of reuse leeway the server refuses it and revokes the whole chain of the authorization, so both tabs lose their session and sign in again.
- Sign-out sends no `id_token_hint`, which would put the id token in the URL, so the hosted logout page asks the user to confirm.
- A `config.json` whose `scopes` lack `offline_access` gets no refresh token: the session lasts as long as the access token (an hour by default), a reload or a new tab signs in through the authorize redirect, a 401 is the answer, and at the expiry the unsaved forms are kept and the page signs in again, however short the lifetime of the access token.
- A session the server ends within a minute of its sign-in, by refusing its refresh token, is not signed in again on its own, so that a refusing server cannot cause a loop of redirects; the user signs in again with "Try again".
- What this record does not change:
  - Drafts are in `sessionStorage` in clear text until they are restored and saved, discarded, removed at sign-out, or the tab closes.
  - A wrong `authority` in `config.json` sends the code, the PKCE verifier and a stored refresh token to that origin; `config.json` is as trusted as `index.html` (spec 2.5).
  - Every page of the same origin, the hosted pages and the API reference among them, can read the tab's `sessionStorage`.
  - A logout the user does not confirm on the hosted page leaves the server's session alive, so opening the admin again signs in without a prompt.
- The browser end-to-end tests of A-11 prove the flow in a browser against the reference host; the component and API tests cannot show the whole-page redirects, `sessionStorage` itself, or the Content-Security-Policy.
