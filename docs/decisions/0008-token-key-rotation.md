# 0008 Rotate the token keys through dated states in `cms_signing_key`, with validation-only certificates and a separate revocation

## Status

Proposed.

## Date

2026-11-23

## Context

[#3313](https://github.com/fluentcms/FluentCMS/issues/3313) asks that the keys of the authorization server (spec 4.7) can be replaced without signing everyone out, on a schedule and on demand, and for this record before any code.

What the code does today:

- **Signing has two modes.** With `FluentCms:Security:SigningCertificate` set, `ServerCredentials` hands OpenIddict that one certificate. Without it, it hands over the RSA 2048 keys of `cms_signing_key` with `use = sig`, of which there is one: `SigningKeyStore` inserts a key only when a use has none, under a fixed id. Outside Development the generated key needs `AllowGeneratedSigningKey` (`FCMS1102`).
- **Encryption has one.** The 256-bit key with `use = enc` is always generated and kept in the same table, in both modes. It wraps (`A256KW`, content `A256CBC-HS512`) authorization codes and refresh tokens, which only the server reads. Access tokens are not encrypted (`DisableAccessTokenEncryption`) and identity tokens never are.
- **What a key signs.** Access tokens (`AccessTokenLifetime`, an hour), identity tokens (20 minutes), codes (5 minutes) and refresh tokens (`RefreshTokenLifetime`, 14 days, at most 365; a used one is taken again for 30 seconds). All are self-contained JWTs: reference tokens are not turned on. `PreviewTokenIssuer` signs preview tokens with the same key and stores nothing (`FluentCms:PreviewTokens:MaximumLifetime`, at most a day), and the logout endpoint checks the signature of an `id_token_hint` whatever its age.
- **Who validates.** Only the host itself: OpenIddict's validation on the local server, which is the bearer scheme of the FluentCMS APIs and of `/mcp`, then `PreviewTokenIssuer` and the hint check. All three take the keys from `OpenIddictServerOptions`. Nothing in the repository reads `/.well-known/jwks`; it is there for a client that checks an identity token, which the admin does not ([ADR 0007](0007-admin-sign-in-flow.md)), and for a resource server or gateway of the host's own, which FluentCMS neither ships nor documents.
- **Read once.** The options are built on the first request and never again, so an instance sees a row that a command or another instance adds only when it restarts.
- **At rest.** The columns are `id`, `use`, `algorithm`, `protected_key` and `created_at`. `protected_key` is the material under Data Protection (purpose `FluentCMS.SigningKeys`), bound to the ring key that protected it.

What OpenIddict 7.8.0 does, read in its source at that tag:

- It takes several credentials "to support key rollover". It signs with the first signing credential and encrypts with the first encryption credential, validates and decrypts with all of them, and lists every asymmetric signing credential in the key set and no encryption key.
- It sorts both lists when the options are built: certificates first, the one that expires last ahead, one not yet valid behind. Two keys that are not certificates compare as equal, and `List<T>.Sort` does not keep the order of equal items. So with two generated keys OpenIddict does not say which signs, and with two certificates it is the one that expires last, which need not be the one the operator means.
- It refuses to build the options when every signing credential is a certificate outside its validity period.
- The server and the validation each take `IOptionsMonitor<T>.CurrentValue` per request, and the validation copies the server's keys when its own options are built.
- The entry checks of the validation (`EnableTokenEntryValidation`, `EnableAuthorizationEntryValidation`) run for a token that names an entry. A token signed with a stolen key that names none passes them, so revoking entries does not stop a forged token; only refusing the key does.

What constrains the design:

- **Instances.** They share the database and nothing else: no signal reaches a running instance, a command runs in a process of its own, and each instance sees another's change of sites or settings within 30 seconds by reading again (4.15).
- **The model.** A change of `cms_signing_key` uses the repository's one model slot and folds into `Initial`; a database made by an earlier build of the baseline is then refused with `FCMS1308` and recreated ([system migrations](../system-migrations.md)).
- **The key ring.** Its certificate is replaced with previous certificates that only read ([#3435](https://github.com/fluentcms/FluentCMS/issues/3435)), and [#3584](https://github.com/fluentcms/FluentCMS/issues/3584) notes that a row of `cms_signing_key` stays under the ring key that protected it.
- **The key set over HTTP.** The well-known group takes the security headers, whose default for an answer outside Delivery is `Cache-Control: no-store`.

Options considered for where a key's state lives:

- **A. Worked out from `created_at` and the options.** A key signs once it is `PublishLead` old and is deleted an overlap after its successor signs. No model change. But what signs then depends on options that can differ between instances during a rolling deploy and change later, an emergency can only delete rows, a key that a certificate replaced has no date to retire at, and the dates an operator sees are a computation and not a record.
- **B. Dates on the row.** Each key carries when it starts to sign and when it stops being accepted, written once by whoever made its successor. Every instance works out the same state from the same rows and its clock.
- **C. Tokens under Data Protection** (`OpenIddict.Server.DataProtection`), so that the ring's own rotation covers codes and refresh tokens. A new package and a second token format, not tried here; access, identity and preview tokens stay signed JWTs, so the signing key needs a rotation anyway.

## Decision

We will adopt option B, for the signing and the encryption key alike:

- **States.** A key is upcoming from `created_at` until `active_from`, active while it is the key of its use with the highest `sequence` whose `active_from` has passed and that has not retired, retiring once a later key is active, and retired from `retired_at`. An upcoming, active or retiring key is accepted, and a signing key in any of the three is listed in the key set; only the active key signs or encrypts. Because OpenIddict's order does not say which that is, FluentCMS puts the active credential first after OpenIddict has sorted, and a test holds it to that. Rejected: relying on the sort, and a status column, which an instance would have to be told about when it changes.
- **Published before it signs.** `active_from` is `created_at` plus `PublishLead`, a day by default and at least 10 minutes. The lower bound is what the instances need: each has read the row before any of them signs with it. The rest of the day is for validators outside the host, whose caches FluentCMS does not control. The encryption key is not published, and keeps the same lead so that there is one rule.
- **Overlap.** When a successor is made, its predecessor gets `retired_at`: the successor's `active_from`, plus the longest lifetime of anything the key signed or encrypted (the largest token lifetime of `OpenIddictServerOptions` and `PreviewTokens:MaximumLifetime`), plus the 30 seconds of reuse leeway and 5 minutes for clocks. By default that is 14 days and under 6 minutes. A token is verified by its signature before its entry is read, so the entries in the database change nothing here; turning reference tokens on would need this looked at again. The job only ever moves `retired_at` later, when a lifetime was raised meanwhile. Rejected: a fixed overlap, which a host with a longer `RefreshTokenLifetime` would outlive.
- **Removal.** The job deletes a row at its first run after `retired_at`, material included. No row is kept for the record; the log is the record.
- **Generated keys.** A recurring job, `oidc.keys` (daily, attempt timeout 5 minutes, scheduled a minute after the start as `oidc.prune` is), makes a successor when the active key of a use is `Interval` minus `PublishLead` old and none is upcoming, sets the dates above and deletes what has retired. A run writes a few rows, so it is not resumable, and a run that is missed only makes a rotation late. `fluentcms keys rotate [--use sig|enc]` makes the successor now, with the same lead, for both uses when `--use` is left out; it does nothing when one is upcoming, and refuses `sig` while a certificate signs. `fluentcms keys list` shows id, use, algorithm, state and dates, and the configured certificates by thumbprint and validity. The `keys` commands run in the host only, as `settings reprotect` does.
- **Configured certificates.** `SigningCertificate` signs. A new list, `FluentCms:Security:ValidationCertificates` (`Path` and `Password` per entry, as `FluentCms:DataProtection:PreviousCertificates`), is accepted and listed in the key set and never signs. It holds the previous certificate for the overlap, and the next one for the lead before it becomes the signing certificate. An entry that cannot be loaded stops the host with `FCMS3022`, and so does a `SigningCertificate` outside its validity period, since FluentCMS and no longer OpenIddict's sort decides that it signs. Outside Development the startup report lists `signing-validation-certificates` with their thumbprints while any is set. Rejected: one `Secondary` certificate, which cannot hold the previous and the next one at the same time.
- **Moving between the modes.** With a certificate configured, the generated signing keys still in the table stay accepted until they retire: the job gives each one without a `retired_at` the overlap from its run. Going back, the certificate that signed is listed under `ValidationCertificates` for the overlap, and the newest generated key that has not retired signs, or a new one.
- **Encryption keys.** They follow the same states, job and command in both modes, since the key is generated in both. Nothing is encrypted for longer than a refresh token lives, so the same overlap holds. Rejected: an encryption certificate, a second configured secret for a key nobody outside the host reads.
- **How instances learn.** Each instance reads the rows every 30 seconds, as it does sites and settings, and rebuilds the OpenIddict options when the set changed or a date has passed. An instance that cannot read keeps the set it has and still moves keys between states by its clock. Routine rotation therefore never has an instance sign with a key another does not hold, since `PublishLead` is far above 30 seconds. Rejected: reading on an unknown `kid`, which lets an unauthenticated caller cause database reads, and a signal, which the host does not have.
- **Storage.** `cms_signing_key` gets `sequence` (int), `active_from` and `retired_at` (nullable), and a unique index on `use` and `sequence`. This is the one model change: it uses the model slot and folds into `Initial`. Whoever makes a key inserts the next `sequence`; of two that try at once, the index lets one in and the other reads the winner's, as the fixed ids do for the first key today. The ids become random, so a `kid` is never used for two keys. Rejected: `IDistributedLock`, which is asynchronous only while the keys are read as the options are built, and which lets two in when it is lost.
- **Protection at rest.** Unchanged: a new key is protected under the ring's default key of that moment. A row the ring cannot read is skipped with an error that names its id, and is never replaced silently; when it is the active key the server issues nothing until the operator restores the ring or revokes. For #3584, rotation is the answer for this table: once `keys rotate` has run after the ring's new default key exists, no row is under an older ring key after `PublishLead` plus the overlap, and the previous ring certificate is no longer needed for it.
- **Revocation, when a key is suspected.** `fluentcms keys revoke [--use sig|enc] --force` is a separate command, because it does the opposite of a rotation. It sets `retired_at` to now on every stored key of the use, upcoming ones included, without reading them; makes a new key that is active at once, with no lead; and revokes every valid token and every ad-hoc authorization of every user and client, leaving consents. Without `--force` it changes nothing and says what it would end; `restore` asks for the same flag before it replaces what the host has. Every access token, refresh token, code, logout hint and preview token is refused from then on: at once on an instance that restarts, within 30 seconds on one that does not. For those 30 seconds an instance that has not read still accepts the old key and refuses the tokens of the new one, which is the only time an instance signs with a key another does not hold. Revoking the entries makes the instances that have not read yet refuse the tokens the server did issue, and keeps the session lists true. It does not stop a forged token, which is why the key is retired and not only the entries revoked. A suspected certificate is replaced without listing it under `ValidationCertificates`, every instance restarted, and the command run for the entries and the encryption key. Rejected: an endpoint of the Management API, whose caller would authenticate with a token of the key in doubt.
- **What the key set promises.** It lists every key that may have signed a token that has not expired, and a new key for `PublishLead` before it signs, except after `keys revoke`. It keeps `Cache-Control: no-store`, so that a cache that honours it holds no revoked key. A validator outside the host reads the set again at least once per `PublishLead` and on a `kid` it does not know, accepts every listed key, and pins none. Rejected: a `max-age`, which is how long a gateway would go on accepting a revoked key.
- **Options and messages.** `FluentCms:Security:KeyRotation` has `Enabled` (true), `Interval` (`90.00:00:00`, more than `PublishLead`), `PublishLead` (`1.00:00:00`) and `Overlap` (unset, which is the derived one; a shorter one stops the host). The 30 seconds are fixed. The startup report also lists `signing-key-rotation-off` outside Development when a generated key signs with `Enabled` off. New events of `OidcLog` say that a key was made, signs, retired, was removed or could not be read, and that keys were revoked, each with the key's id, use and dates and never its material or `protected_key`.

## Scenarios

| What the operator wants | What the operator does |
| --- | --- |
| Routine, generated keys | Nothing: the job rotates every 90 days. `fluentcms keys rotate` does it now. |
| Replace the certificate, one instance | Set the new one as `SigningCertificate` and the old one under `ValidationCertificates`, restart, and remove the old one after the overlap. |
| Replace the certificate, several instances or validators outside | First list the new one under `ValidationCertificates` on every instance and wait `PublishLead`; then swap the two as above. Without the first step, an instance not yet restarted refuses the tokens of one that is. |
| A generated key is suspected | `fluentcms keys revoke --force`, then restart every instance. If the database or the ring is what leaked, follow [replacing the key ring certificate](../host-configuration.md#replacing-the-key-ring-certificate) first; the command also works on rows the ring can no longer read. |
| The certificate is suspected | Replace it with no validation entry for it, restart every instance, then `fluentcms keys revoke --force`. |
| Retire the previous key ring certificate | After the ring's new default key exists, `fluentcms keys rotate`, then wait until `keys list` shows no older key, which is `PublishLead`, the overlap and the job's next run. |

How the acceptance of #3313 is met:

| Acceptance | Part of the decision |
| --- | --- |
| ADR accepted | This record, once its status says so. |
| A token issued before a rotation validates until it expires | Overlap: the retiring key, or the previous certificate, is accepted for the longest lifetime after its successor signs. |
| A retired key is removed after the overlap | Removal: the job deletes the row. A validation certificate is removed by the operator, and the startup report lists it until then. |
| Replacing the configured certificate with the previous one set as secondary signs nobody out | Configured certificates: `ValidationCertificates` is accepted and never signs, whatever the two certificates' expiry dates. |
| The discovery document lists both keys during the overlap | States: the key set lists every upcoming, active and retiring signing key and every validation certificate. |

## Open questions

For the maintainer to decide:

- The model change (three columns and an index, in the model slot, with preview databases recreated) or option A without one.
- Routine rotation on by default, with 90 days and a day of lead, or off until a host turns it on. A validator outside the host that pinned a key breaks at the first rotation.
- The name `ValidationCertificates`, or `PreviousSigningCertificates` to match #3435, which would not fit the next certificate.
- That `keys revoke` always revokes the entries, leaves consents, and leaves the sign-in cookie of the hosted pages, which is under Data Protection and not under these keys: a browser gets new tokens without a prompt.
- Whether a revocation writes one activity row for the instance, which we recommend, or `user.sessions_ended` and `member.sessions_ended` for every user it ends.
- Whether #3584 leaves `cms_signing_key` to rotation, as above, or also protects its rows again in place.

Not verified, and to be proven by a test in the pull request that depends on it:

- That OpenIddict works with options rebuilt at run time through a change token. Its factories take `CurrentValue` per request, but its documentation does not promise it as far as we found. If it does not, an instance learns keys only at its start and this record is revised.
- Whether a key that only validates can be handed over without its private half, so that a wrong order fails a signature instead of signing with the wrong key. OpenIddict's builder refuses a signing key without one; adding to the options directly, as `ServerCredentials` does today, is not refused in 7.8.0.
- That a signature made by a certificate that has since expired still validates while the certificate is listed.
- That the key set answers `no-store` today: read in `SecurityHeadersMiddleware`, not asserted by a test.
- How long common validators keep a key set. The promise is stated as a number for that reason.

## Consequences

- An operator can replace either key, or the certificate, without ending a session, and can end every token at once when a key is in doubt. Nothing that validates a token today is weakened: every key accepted is one the operator configured or the server generated and protects as today, and a retired one stops being accepted, where today a key is accepted for as long as its row or its setting exists.
- A host keeps two keys of a use for most of a rotation, and more when `RefreshTokenLifetime` is longer than `Interval`: five with the longest lifetime and the default interval, and a sixth for the day of lead.
- An identity token of a retired key is no longer taken as a logout hint, so such a logout asks on the logout page, as spec 4.7 says of a hint that is not the server's.
- A revocation with no lead makes a validator outside the host that cached the set refuse new tokens until it reads the set again.
- A host in certificate mode still depends on the key ring for the encryption key, and its rotation runs there too.
- The model change makes every preview database be recreated, once, so no deployment runs a build from before it beside one from after. Later builds read the same rows and agree on what signs.
- Out of scope:
  - Rotating the Data Protection ring or its certificate, which #3435 and #3584 cover.
  - The secrets of sign-in providers, API keys, webhook secrets and the media URL signing key, each of which has or needs its own rotation.
  - Keys held in a vault or hardware module, which a host can already hand to OpenIddict itself.
  - A change of algorithm from RS256.
  - An admin page or API for any of this.
  - Refusing a bearer token that names no entry, which would let a revocation of entries stop some forged tokens and deserves an issue of its own.
- The implementation, each a pull request small enough to review:
  1. The columns and the index in `Initial` (the model slot), with `SigningKeyStore` reading several keys by state and FluentCMS ordering the credentials. With one key nothing changes.
  2. The 30-second read and the rebuilt options, which proves the first unverified point.
  3. The job `oidc.keys`, the options, their start checks, the report line and the log events.
  4. `fluentcms keys list` and `keys rotate`.
  5. `ValidationCertificates`, the move between the modes, and the guide in `docs/host-configuration.md`.
  6. `fluentcms keys revoke`, with its guide.
