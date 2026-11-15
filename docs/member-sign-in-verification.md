# Verifying member sign-in against the real providers

CI tests the [member providers](host-configuration.md#member-sign-in-with-an-external-provider) against `FakeMemberProviders` and `FakeOidcProvider`, stand-ins on a stubbed back channel, so no test calls a provider. This page records what has been run against what, and holds the walk-through for a run against the real providers. For M7 the plan verifies Google, Entra ID (for the Microsoft handler and for generic OIDC) and GitHub ([spec](planning/backend-technical-spec.md) 10.4, H3).

| Provider | Status |
| --- | --- |
| Stand-ins in the tests | Every CI run; see [On the wire](#on-the-wire). |
| Google | Not run yet. |
| Microsoft, through Entra ID | Not run yet. |
| GitHub | Not run yet. |
| Generic OIDC, against Entra ID | Not run yet. |
| Apple | Not verified, and named as such in the notes of the preview. It is verified before 1.0.0. |

## On the wire

`MemberProviderTests`, `MemberProviderStartupTests` and `MemberTokenTests` run the handlers against `FakeMemberProviders`, which answers the token and user information endpoints of Google, Microsoft and GitHub and the discovery, keys and token endpoints of Apple, and `FakeOidcProvider` for generic OIDC:

- Google's user information carries `email_verified`, which the handler maps to the claim the sign-in reads; `false` or a missing flag makes an unverified member, who gets the verification mail.
- Microsoft's `me` carries `mail` and `userPrincipalName` and no flag, so its member is never verified by the provider.
- GitHub's `user` carries the public address, which is never used; `user/emails`, asked for with `user:email`, lists the addresses with `primary` and `verified`, and only a primary verified one names a member. An account without one is refused with `FCMS3181`, and a `user/emails` that fails is a sign-in with no address (event 9094).
- Apple's id token carries `email` and `email_verified` as text (`"true"`), and the client secret is a token signed with the key of the run in the name of the team, which the stub validates before it answers.
- Generic OIDC carries `sub`, `email`, `email_verified` and `name` in the id token; the user information endpoint is not read.
- The `redirect_uri` sent to the provider, and again when the code is redeemed, is on `FluentCms:PublicOrigin` when one is set, for every handler.
- An `error` from the provider, and a callback whose `state` belongs to no sign-in of that browser, go back to `/account/members/login?Status=external-failed` with a `member.login_failed` row; one started from the logins page goes back to `/account/members/logins?Status=external-failed`.

A stand-in cannot tell what a real provider does, so a real run checks in particular:

- whether Google sends `email_verified` in the user information, and as a boolean;
- what Entra ID puts in `email` for a personal account and for a work account, with and without the optional claim, and that no `email_verified` comes;
- whether GitHub's `user/emails` answers with the `user:email` scope alone, and how an account whose primary address is not verified is listed;
- whether a cancelled consent comes back as `error=access_denied` on the callback, and whether the provider sends the `state` back with it;
- whether the `redirect_uri` the provider sees is the public origin, behind the tunnel.

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
| `ASPNETCORE_ENVIRONMENT` | `Development`. In Production the host also needs `FluentCms__Security__AllowGeneratedSigningKey=true` and `FluentCms__Security__AllowUnencryptedKeyRing=true`, or a certificate and an encryptor (see [production refusals](host-configuration.md#production-refusals)). |
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

`https://<origin>/account/members/login` then shows one "Continue with ..." button per provider. The startup report in `host.log` (event 9130) names the public origin and no insecure default about it.

### The steps

Each step is walked on the hosted pages at `https://<origin>`, in the browser profile of the account it names. What a step sends is read in the address bar of the provider (`redirect_uri`, `scope`, `state`) before consent, and in `host.log`: the request log of ASP.NET Core writes each callback with its query, and events 9090 to 9094 say what the sign-in decided and why.

1. **Google, a new visitor.** "Continue with Google", consent, back to the site signed in. The member is verified at once: `GET https://<origin>/api/members/v1/me` with the member's token (step 9) says `emailVerified: true`, no mail arrived, and event 9090 says `Verified: True`.
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
3. **Microsoft, a new visitor.** A personal Microsoft account through "Continue with Microsoft". The page says a mail was sent and nobody is signed in; Mailpit shows the mail naming Microsoft, its link on `https://<origin>/account/members/verify-email`. Following it verifies the address, and "Continue with Microsoft" again signs in. Event 9090 says `Verified: False`.
4. **GitHub, a verified primary address.** The first GitHub account through "Continue with GitHub": the member is verified at once, with the primary address of the account and not the public one of the profile (set them to differ beforehand).
5. **GitHub, no verified primary address.** The second account, whose primary address is not verified. No member is made: back to the login page as a failed sign-in, event 9092 with `FCMS3181`, and the member list of the Management API holds no new member.
6. **Generic OIDC, Entra ID.** "Continue with entra" with the work account, then with the personal account. Entra ID sends no `email_verified`, so each is expected to get the verification mail as in step 3, and the record says what `email` held for each kind of account.
7. **Linking, listing and removing.** Signed in as the Google member of step 1, open `/account/members/logins`, link GitHub (the first account) and confirm on the page that names the account. The list shows both; remove GitHub, which ends the sessions and signs the browser in again; the list shows Google alone, and removing it is refused as the last way to sign in (`FCMS3184`). `GET /api/members/v1/me/logins` lists the same.
8. **A cancelled consent and a provider error.** On the Google consent screen press Cancel, and on the Microsoft one deny. Each returns to `/account/members/login?Status=external-failed` and not to an error page, with a `member.login_failed` row whose method is the provider's name, and event 9091 with the provider's message. For an error of the provider, edit the `redirect_uri` in the address bar of the provider before consent to a path that is not registered: the provider refuses on its own page, and the host is not involved. Then open `https://<origin>/account/members/sso/google?state=stale&code=none` in a fresh profile: that callback belongs to no sign-in and goes back to the login page the same way.
9. **Behind the reverse proxy.** The whole run is behind the tunnel with `FluentCms__PublicOrigin` set, so the `redirect_uri` on each provider's page, the links in the mails and the `issuer` of `https://<origin>/.well-known/openid-configuration` are all `https://<origin>`, and the `member.login_succeeded` rows carry the browser's address and not the tunnel's. Then stop the host, unset `FluentCms__PublicOrigin`, start it again and open the login page through the tunnel: the `redirect_uri` on Google's page should then be built from the forwarded headers and still be `https://<origin>`, since `ForwardProto` is on and the tunnel keeps the `Host` header; record what it was.
10. **The log.** `grep` in `host.log` for the client secrets and for `Bearer` finds nothing, and the callbacks of Google, Microsoft and GitHub are the only lines holding a `code`.

Record each run as a section of this page, as the AI assist page does for [its runs](ai-assist-verification.md): the date, the providers and the kinds of account, and for each step what the provider sent, what the host decided and the event that says so. Where a provider differs from `FakeMemberProviders` or `FakeOidcProvider`, correct the stand-in and the tests in `tests/FluentCMS.Core.IntegrationTests/Members/`, and record a difference that is not fixed in an issue of its own.

### Clean up

Stop the host, delete the directory of the run, rotate the client secrets at each provider or delete the development applications, and remove the mail catcher:

```bash
docker rm -f fluentcms-mailpit
```
