# Verifying member sign-in against the real providers

CI tests the [member providers](host-configuration.md#member-sign-in-with-an-external-provider) against `FakeMemberProviders` and `FakeOidcProvider`, stand-ins on a stubbed back channel, so no test calls a provider. This page records what has been run against what, and holds the walk-through for a run against the real providers. For M7 the plan verifies Google, Entra ID (for the Microsoft handler and for generic OIDC) and GitHub ([spec](planning/backend-technical-spec.md) 10.4, H3).

| Provider | Status |
| --- | --- |
| Stand-ins in the tests | Every CI run; see [On the wire](#on-the-wire). |
| Google | Run on 15 Nov 2026; see [Real providers](#real-providers-15-nov-2026). |
| Microsoft, through Entra ID | Run on 15 Nov 2026 with a personal account; the deny of the consent could not be exercised, since Microsoft asks for no consent for `user.read`. |
| GitHub | Run on 15 Nov 2026 with a personal account and an enterprise-managed one. |
| Generic OIDC, against Entra ID | Run on 15 Nov 2026 with a personal account and a user of the tenant; both were refused, as specified, and no sign-in through generic OIDC completed ([#3160](https://github.com/fluentcms/FluentCMS/issues/3160)). It found that Entra ID sends no `email` for a user without a mail attribute, even with the optional claim on. |
| Apple | Dropped ([#3709](https://github.com/fluentcms/FluentCMS/issues/3709)): FluentCMS does not offer sign-in with Apple, so the run ([#3165](https://github.com/fluentcms/FluentCMS/issues/3165)) will not be made. |

## On the wire

`MemberProviderTests`, with its `.Security` and `.Linking` parts, runs the handlers against `FakeMemberProviders`, which answers the token and user information endpoints of Google, Microsoft and GitHub, and `FakeOidcProvider` for generic OIDC; `MemberProviderStartupTests` builds hosts of its own and checks the options and the schemes the providers register:

- Google's user information carries `email_verified`, which the handler maps to the claim the sign-in reads; `false` or a missing flag makes an unverified member, who gets the verification mail.
- Microsoft's `me` carries `mail` and `userPrincipalName` and no flag, so its member is never verified by the provider.
- GitHub's `user` carries the public address, which is never used; `user/emails`, asked for with `user:email`, lists the addresses with `primary` and `verified`, and only a primary verified one names a member. An account without one is refused with `FCMS3181`, and a `user/emails` that fails is a sign-in with no address (event 9094).
- Generic OIDC carries `sub`, `email`, `email_verified` and `name` in the id token, the flag as a boolean or as text (`"true"`), as some providers send it; the user information endpoint is not read.
- The `redirect_uri` sent to the provider, and again when the code is redeemed, is on `FluentCms:PublicOrigin` when one is set, for every handler.
- An `error` from the provider, and a callback whose `state` belongs to no sign-in of that browser, go back to `/account/members/login?Status=external-failed` with a `member.login_failed` row; one started from the logins page goes back to `/account/members/logins?Status=external-failed`.

A stand-in cannot tell what a real provider does, so a real run checks in particular:

- whether Google sends `email_verified` in the user information, and as a boolean;
- what Entra ID puts in `email` for a personal account and for a work account, with and without the optional claim, and that no `email_verified` comes;
- whether GitHub's `user/emails` answers with the `user:email` scope alone, and how an account whose primary address is not verified is listed;
- whether a cancelled consent comes back as `error=access_denied` on the callback, and whether the provider sends the `state` back with it;
- whether the `redirect_uri` the provider sees is the public origin, behind the tunnel.

## Real providers, 15 Nov 2026

The [walk-through](#walk-through) on the Release build of the host: SQLite in the directory of the run, Development, `Logging__LogLevel__Default=Debug`, behind an ngrok static domain with the forwarded headers on and `FluentCms__PublicOrigin` set to it, Mailpit in Docker, and the providers `google`, `microsoft`, `entra` (`Oidc` against the tenant of the registration, with the optional claim `email` on the id token) and `github`. One Chrome profile walked every step, signed out of the host between them at `/account/members/logout`. The accounts: a Google account that is a test user of a consent screen in Testing, a personal Microsoft account, a user made in the tenant for the run, a GitHub account with three verified addresses, and an enterprise-managed GitHub account. The personal Microsoft account and the GitHub account shared an address at first, so some steps ran as the collision of an address that is a member's; the record says so where they did.

| Step | What the provider sent | What the host decided |
| --- | --- | --- |
| 1. Google, a new visitor | The account chooser, then the consent, whose address bar held the `redirect_uri` on the public origin, `scope=openid profile email`, PKCE S256 and the `state`. The callback is a GET with `state`, `iss=https://accounts.google.com`, `code`, `scope` (the granted scopes by their long names, `.../auth/userinfo.email` and `.../auth/userinfo.profile`, with `openid`), `authuser` and `prompt`. | Event 9090, "its address is verified: True"; the rows `member.registered`, `member.verified`, `member.login_linked` and `member.login_succeeded` with method `google`; no mail. |
| 2. The code flow | `/connect/authorize` sent the signed-in browser to `/signed-in?code=...&state=verify&iss=https://<origin>/`; `/connect/token` answered `access_token`, `id_token` and `scope` `openid profile email`. | The id token's `iss` is `https://<origin>/`, its `aud` is `verify`, and it carries `name` and `email`; `/api/members/v1/me` says `emailVerified: true` and `/me/logins` lists `google`. |
| 3. Microsoft, a new visitor | Asked for `https://graph.microsoft.com/user.read`; the callback is a GET with `code` and `state` only. A personal account gets no consent screen after the first time, not even after the app is removed at `account.live.com`. | Event 9090, "its address is verified: False"; `member.login_failed` with reason `not_allowed`; the mail "Verify your email address" names Microsoft, its link is on the origin, and the page says a mail is on its way. The link verified the address, and the login page then said "Your email address is verified. Sign in with your password"; "Continue with Microsoft" signed in, `member.login_succeeded` with method `microsoft`. |
| 4. GitHub, a verified primary address | A page to choose between the signed-in accounts, `scope=user:email` and nothing else; the callback is a GET with `code`, `iss=https://github.com/login/oauth` and `state`. The account's primary address differed from the public one of the profile. | Event 9090, "its address is verified: True", a member with the primary address and not the public one. A sign-in through GitHub with a login linked to no member and a primary address that is a member's was refused with `FCMS3182` and no mail, and the page "Sign-in not completed" pointed at the sign-in methods; a login that is linked signs its member in whatever its address. |
| 5. GitHub, no verified primary address | The enterprise-managed account: `user/emails` listed no primary verified address. | Event 9092 with `FCMS3181`, no member, `member.login_failed` with reason `not_allowed`, and the page "Sign-in not completed". |
| 6. Generic OIDC, Entra ID | The challenge goes to `login.microsoftonline.com/<tenant>/oauth2/v2.0/authorize` with `response_mode=form_post`, a nonce, PKCE and `scope=openid profile email`; the callback is a form post of 2 to 3 KB and no query. The personal account's `email` is the address of the account, with no `email_verified`. A user of the tenant without a mail attribute gets no `email` at all, with the optional claim on. A work account of another tenant is refused by Entra ID on its own page ("does not exist in tenant"), and the host is not involved. | The personal account's address was the Microsoft member's: `FCMS3182`, the mail "You already have an account" and the page of the mail. The user without an address: event 9092 with `FCMS3181`, no member, "Sign-in not completed". No member was made or signed in through generic OIDC: a user of the tenant with a mail attribute was not run ([#3160](https://github.com/fluentcms/FluentCMS/issues/3160)). |
| 7. Linking, listing and removing | The link started on `/account/members/logins`, went to GitHub's account chooser and came back to the page, which named the account's address before the second post. | `Status=linked`; `GET /api/members/v1/me/logins` listed `github` and `google`; removing GitHub answered `Status=removed`, wrote `member.login_unlinked`, and the access token of step 2 was refused (`FCMS3102`, 401) from then on while the page kept its cookie; removing Google was refused on the page as the last way to sign in. |
| 8. A cancelled consent and a stale callback | Cancel on Google's consent sends the callback `error=access_denied&state=...`, with the `state`. The deny on Microsoft was not reachable (step 3). The callback with `state=stale` was opened by hand. | Each went back to `/account/members/login?Status=external-failed` with a `member.login_failed` row of method `google` and reason `invalid_credentials`; event 9091 said "Access was denied by the resource owner or by the remote server" for the cancel and "The oauth state was missing or invalid" for the stale state. The `redirect_uri` edited on the provider's page was not run. |
| 9. Behind the reverse proxy | The `redirect_uri` on each provider's page, the links of the mails and the `issuer` of the discovery document were all on the origin. Started again without `FluentCms__PublicOrigin`, the host sent Google a `redirect_uri` on the same https origin, built from the forwarded headers, and the `issuer` was the same. | The `member.login_succeeded` and `member.login_failed` rows carry the browser's address and not the tunnel's. The request log writes `http://` on the "Request starting" line and `https://` on "Request finished", since the forwarded headers are read after the first. |
| 10. The log | 340,611 lines of which 126,372 at debug. | No client secret. `Bearer` on four lines: the `token_type` of the token response, in which OpenIddict writes `access_token` and `id_token` as `[redacted]`, and the names of its events. `code=` on the callback lines of Google, Microsoft and GitHub, twice each, and on the `/signed-in` of step 2, which is on this host only because the client of the run lives here. |

The five points of [On the wire](#on-the-wire), from the run:

- Google's flag was read as verified; the handler maps `email_verified` to a claim and `MemberExternalSignIn.ReadAsync` compares it as text, so it does not tell a boolean from `"true"`.
- Entra ID puts the address of a personal account in `email`, and nothing at all for a user of the tenant without a mail attribute, whatever the optional claim says; no `email_verified` came in either case. A work account lives in its own tenant, which the `Authority` of the provider does not admit unless the account is a guest there.
- GitHub's `user/emails` answers with `user:email` alone, and a primary address that is not verified names no member. The enterprise-managed account listed no verified primary address.
- A cancelled consent comes back as `error=access_denied` with the `state`, on Google. Microsoft asks a personal account for no consent for `user.read`, so there was nothing to deny.
- The `redirect_uri` behind the tunnel is the public origin, and still is when the origin is not set and the forwarded headers build it.

What the stand-ins now do as the providers did: `FakeMemberProviders` answers Google's callback with `iss`, the granted scopes by their long names, `authuser` and `prompt`, and its token response with the same scopes, and GitHub's callback with `iss`. The `Oidc` case of `MemberProviderTests.AnAppleProviderAndAGenericOidcProviderSignAMemberIn` now also signs in with an id token that has no `email`, as Entra ID issued it, and holds the refusal with `FCMS3181`.

Not fixed, for issues of their own:

- The login page after the verification link says "Sign in with your password", and the mail "You already have an account" says to sign in with a password or reset it, to a member that a provider made and that has no password; the page that sent that mail said to follow it, and it holds no link. The answer is the same whether or not the address is a member's by design (spec 10.4), so the words, not the paths, are what to change ([#3158](https://github.com/fluentcms/FluentCMS/issues/3158)).
- A hosted page with a form asked for over plain http, such as `http://localhost:5080`, is answered 500 (`FCMS3199`): the antiforgery cookie's `SecurePolicy` is `Always`, with or without a public origin, where a refusal that names the cause would serve better. Fixed since by [#3159](https://github.com/fluentcms/FluentCMS/issues/3159): every hosted page over plain http now answers 403 with a page that says it needs https (`FCMS3108`).
- The deny on Microsoft's consent, a user of the tenant with a mail attribute, and the `redirect_uri` edited on a provider's page were not run ([#3160](https://github.com/fluentcms/FluentCMS/issues/3160)).
- Apple was not verified ([#3165](https://github.com/fluentcms/FluentCMS/issues/3165)), and was dropped since ([#3709](https://github.com/fluentcms/FluentCMS/issues/3709)).

## Walk-through

### What the run needs

No secret goes into the repository, into an issue or onto this page.

1. **A public HTTPS origin** that reaches the reference host on the machine of the run, such as an ngrok static domain (`ngrok http 5080 --url=https://<domain>`), a Cloudflare Tunnel or a Tailscale Funnel to `http://localhost:5080`. The tunnel sends `X-Forwarded-For` and `X-Forwarded-Proto`, so it is also the reverse proxy of the last step. Call it `https://<origin>` below.
2. **Google**: an OAuth client of type "Web application" in a Google Cloud project, with the consent screen in Testing and the test accounts added. Authorized redirect URI `https://<origin>/account/members/sso/google`. The scopes `openid`, `email` and `profile` need no verification.
3. **Entra ID**: one app registration, platform Web, supported account types "Accounts in any organizational directory and personal Microsoft accounts" (the Microsoft handler uses the `common` endpoint). Redirect URIs `https://<origin>/account/members/sso/microsoft` and `https://<origin>/account/members/sso/entra`. Token configuration: the optional claim `email` on the ID token. One client secret serves both providers.
4. **GitHub**: an OAuth App, not a GitHub App, with the callback `https://<origin>/account/members/sso/github`. For the negative check, a second GitHub account whose primary address is not verified.
5. **Mail**: a local catcher receives the verification mails of the Microsoft and Entra paths. Mailpit needs no account: `docker run -d --name fluentcms-mailpit -p 127.0.0.1:1025:1025 -p 127.0.0.1:8025:8025 axllent/mailpit`, and the mails are read at `http://127.0.0.1:8025`. An SMTP account works too, with `Security` left at `StartTls`.
6. **Accounts to sign in with**, each in a browser profile of its own: one Google account added as a test user, one personal Microsoft account and one work account of the tenant, and the two GitHub accounts.

### The host

Set the configuration in the environment of the host, from a file outside the repository (`source ~/fluentcms-verify.env`):

| Variable | Value |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Development`. In any other environment the host also needs `FluentCms__Security__AllowGeneratedSigningKey=true` and `FluentCms__Security__AllowUnencryptedKeyRing=true`, or a certificate and an encryptor (see [startup refusals](host-configuration.md#startup-refusals)). |
| `ASPNETCORE_URLS` | `http://localhost:5080`, where the tunnel points |
| `FluentCms__PublicOrigin` | `https://<origin>` |
| `FluentCms__ReverseProxy__ForwardedHeaders__Enabled` | `true` |
| `FluentCms__ReverseProxy__ForwardedHeaders__KnownProxies__0`, `__1` | `127.0.0.1` and `::1`, the tunnel's agent on the same machine |
| `FluentCms__Members__AllowRegistration` | `true` |
| `FluentCms__Members__Providers__google__Type`, `ClientId`, `ClientSecret` | `Google` and the client of step 2 |
| `FluentCms__Members__Providers__microsoft__Type`, `ClientId`, `ClientSecret` | `Microsoft` and the registration of step 3 |
| `FluentCms__Members__Providers__entra__Type`, `Authority`, `ClientId`, `ClientSecret` | `Oidc`, `https://login.microsoftonline.com/<tenant id>/v2.0`, and the same registration |
| `FluentCms__Members__Providers__entra__Scopes__0`, `__1` | `openid` and `profile`; `email` is always asked for |
| `FluentCms__Members__Providers__github__Type`, `ClientId`, `ClientSecret` | `GitHub` and the app of step 4 |
| `FluentCms__Email__Smtp__Host`, `Port`, `Security`, `From` | `127.0.0.1`, `1025`, `None`, `cms@example.com` for Mailpit; `UserName` and `Password` unset |
| `FluentCms__Clients__Applications__verify__Pool` | `Members` |
| `FluentCms__Clients__Applications__verify__RedirectUris__0` | `https://<origin>/signed-in`, a path the host does not serve: the code is read from the address bar of the 404 |
| `FluentCms__Clients__Applications__verify__Scopes__0`, `__1`, `__2` | `openid`, `profile`, `email` |
| `Logging__LogLevel__Default` | `Debug` |

Build the Release host, apply the migrations and start it from the directory of the run, with SQLite in that directory:

```bash
dotnet build src/FluentCMS.Host -c Release
cd <run>
export ConnectionStrings__FluentCms="Data Source=$PWD/verify.db"
dotnet <repo>/src/FluentCMS.Host/bin/Release/net10.0/FluentCMS.Host.dll fluentcms migrate
dotnet <repo>/src/FluentCMS.Host/bin/Release/net10.0/FluentCMS.Host.dll > host.log 2>&1 &
```

`https://<origin>/account/members/login` then shows one "Continue with ..." button per provider. The startup report in `host.log` (event 9130) checks the public origin in every environment but Development, where it lists `public-origin-unset` when none is set and `AllowRequestOrigin` lets the host start, so in Development check the origin on the provider's page instead (step 9). Open the pages through the tunnel only: the antiforgery cookie is always `Secure`, so a page with a form asked for over plain http, such as `http://localhost:5080`, is answered 403 with a page that says it needs https (`FCMS3108`, [#3159](https://github.com/fluentcms/FluentCMS/issues/3159)).

### The steps

Each step is walked on the hosted pages at `https://<origin>`, in the browser profile of the account it names. What a step sends is read in the address bar of the provider (`redirect_uri`, `scope`, `state`) before consent, and in `host.log`: the request log of ASP.NET Core writes each callback with its query, and events 9090 to 9094 say what the sign-in decided and why.

1. **Google, a new visitor.** "Continue with Google", consent, back to the site signed in. The member is verified at once: `GET https://<origin>/api/members/v1/me` with the member's token (step 2) says `emailVerified: true`, no mail arrived, and event 9090 says "its address is verified: True".
2. **Google, tokens through the code flow.** In the same profile, open `/connect/authorize` for the client `verify` with PKCE, as `MemberExitCriteriaTests` does:

   ```bash
   VERIFIER=$(openssl rand -base64 32 | tr -d '=+/' | cut -c1-43)
   CHALLENGE=$(printf '%s' "$VERIFIER" | openssl dgst -sha256 -binary | openssl base64 | tr -d '=' | tr '+/' '-_')
   echo "https://<origin>/connect/authorize?client_id=verify&response_type=code&redirect_uri=https://<origin>/signed-in&scope=openid%20profile%20email&code_challenge=$CHALLENGE&code_challenge_method=S256&state=verify"
   ```

   The browser, already signed in as the member, lands on `/signed-in?code=...&state=verify`. Redeem the code:

   ```bash
   curl -s https://<origin>/connect/token -d grant_type=authorization_code -d client_id=verify -d "redirect_uri=https://<origin>/signed-in" -d "code=<code>" -d "code_verifier=$VERIFIER"
   ```

   The answer holds `access_token` and `id_token`; the access token reads `/api/members/v1/me`.
3. **Microsoft, a new visitor.** A personal Microsoft account through "Continue with Microsoft". The page says a mail was sent and nobody is signed in; Mailpit shows the mail naming Microsoft, its link on `https://<origin>/account/members/verify-email`. Following it verifies the address, and "Continue with Microsoft" again signs in. Event 9090 says "its address is verified: False".
4. **GitHub, a verified primary address.** The first GitHub account through "Continue with GitHub": the member is verified at once, with the primary address of the account and not the public one of the profile (set them to differ beforehand).
5. **GitHub, no verified primary address.** The second account, whose primary address is not verified. No member is made: back to the login page as a failed sign-in, event 9092 with `FCMS3181`, and the member list of the Management API holds no new member.
6. **Generic OIDC, Entra ID.** "Continue with entra" with the work account, then with the personal account. Entra ID sends no `email_verified`, so an address that is no member's gets the verification mail as in step 3; an address that is a member's is refused with `FCMS3182` and gets the mail "You already have an account", and an account without an `email` is refused with `FCMS3181`. In the run of 15 Nov 2026 the personal account's address was a member's and the user of the tenant had no mail attribute, so neither got the verification mail: use an account whose address is no member's, such as a user of the tenant with a mail attribute. Record what `email` held for each kind of account.
7. **Linking, listing and removing.** Signed in as the Google member of step 1, open `/account/members/logins`, link a GitHub account that no member is linked to (not the account of step 4, whose login is that member's and is answered "linked to another account here") and confirm on the page that names the account. The list shows both; remove GitHub, which ends the sessions and signs the browser in again; the list shows Google alone, and removing it is refused as the last way to sign in (`FCMS3184`). `GET /api/members/v1/me/logins` lists the same.
8. **A cancelled consent and a provider error.** On the Google consent screen press Cancel, and deny on Microsoft's where one is shown: Microsoft asks a personal account for no consent for `user.read`, so its deny needs an account that is asked. Each returns to `/account/members/login?Status=external-failed` and not to an error page, with a `member.login_failed` row whose method is the provider's name, and event 9091 with the provider's message. For an error of the provider, edit the `redirect_uri` in the address bar of the provider before consent to a path that is not registered: the provider refuses on its own page, and the host is not involved. Then open `https://<origin>/account/members/sso/google?state=stale&code=none` in a fresh profile: that callback belongs to no sign-in and goes back to the login page the same way.
9. **Behind the reverse proxy.** The whole run is behind the tunnel with `FluentCms__PublicOrigin` set, so the `redirect_uri` on each provider's page, the links in the mails and the `issuer` of `https://<origin>/.well-known/openid-configuration` are all `https://<origin>`, and the `member.login_succeeded` rows carry the browser's address and not the tunnel's. Then stop the host, unset `FluentCms__PublicOrigin`, start it again and open the login page through the tunnel: the `redirect_uri` on Google's page should then be built from the forwarded headers and still be `https://<origin>`, since `ForwardProto` is on and the tunnel keeps the `Host` header; record what it was.
10. **The log.** `grep` in `host.log` for the client secrets finds nothing. `Bearer` is only the `token_type` of the token response, in which OpenIddict writes `access_token` and `id_token` as `[redacted]`, and the names of its events. A `code` is only on the callback lines of Google, Microsoft and GitHub, and on the `/signed-in` of step 2, which is on this host only because the client of the run lives here.

Record each run as a section of this page, as the AI assist page does for [its runs](ai-assist-verification.md): the date, the providers and the kinds of account, and for each step what the provider sent, what the host decided and the event that says so. Where a provider differs from `FakeMemberProviders` or `FakeOidcProvider`, correct the stand-in and the tests in `tests/FluentCMS.Core.IntegrationTests/Members/`, and record a difference that is not fixed in an issue of its own.

### Clean up

Stop the host, delete the directory of the run, rotate the client secrets at each provider or delete the development applications, and remove the mail catcher:

```bash
docker rm -f fluentcms-mailpit
```
