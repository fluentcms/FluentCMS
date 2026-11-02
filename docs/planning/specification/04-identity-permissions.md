# 4. Identity & permissions — approved

One authorization server (OpenIddict) issues every token: admin sessions, API keys' bearer equivalents, member logins, MCP clients. Permissions are checked in one place, the in-process service, so REST, GraphQL, admin and MCP can never disagree. SSO, 2FA and audit are free, because they are what competitors paywall most.

| # | Feature | Tier | What it means |
| --- | --- | --- | --- |
| 1 | Authorization server | Must | OpenIddict in the API host: Authorization Code + PKCE, refresh tokens, client credentials for services, token introspection and revocation, discovery document. |
| 2 | Admin users | Must | Invite by email, password policy, Argon2id hashing, lockout after failed attempts, password reset, forced reset, deactivate, session list with revoke. |
| 3 | Roles | Must | Named roles with default Super Admin, Editor, Author; users hold several roles; roles scoped per site. |
| 4 | Type-level permissions | Must | Per content type: create, read, update, delete, publish, unpublish, restore; per media folder; per settings area. |
| 5 | Field-level permissions | Must | Read and write per field per role; hidden fields are omitted from API responses and admin forms, not blanked. |
| 6 | Row-level rules | Must | C# predicates (expression trees) per role and operation, such as own entries or entries in the user's region; compiled into list queries so filtering and authorization are one step. |
| 7 | Locale and site scoping | Must | Permissions can be limited to locales and sites. |
| 8 | Public access configuration | Must | Per type and operation, what anonymous Delivery requests may read; closed by default. |
| 9 | API keys | Must | Delivery, preview and management key classes; scoped to a role, sites and types; expiry, rotation, last-used, revoke; hashed at rest. |
| 10 | Member authentication | Must | End-user accounts for sites and apps as a separate identity pool: registration, email verification, password reset, login, refresh tokens; member groups usable in row-level rules and public access. |
| 11 | External identity providers for members | Must | Google, Microsoft, GitHub, Apple and generic OIDC for member login. |
| 12 | Permission checks in one place | Must | All checks run in the in-process service; HTTP, GraphQL and MCP layers only carry identity. Admin UI hides what the user cannot do by reading the same permission set. |
| 13 | Security defaults | Must | No anonymous access until configured, CORS allow-list, secure cookie flags, rate-limited auth endpoints, no secrets in config files (user secrets, env vars, Key Vault providers). |
| 14 | Activity log | Must | Who, what, when and origin (admin, API, MCP, job) for content, media, user, role, key and settings changes; 30-day default retention; queryable via Management API. |
| 15 | Admin SSO via OIDC | Must | External providers (Entra ID, Okta, Keycloak, generic OIDC) federated through the authorization server; auto-provisioning, default role, group-to-role mapping, optional local-login disable. |
| 16 | Two-factor authentication | Must | TOTP for admin users, enforceable per role; recovery codes. |
| 17 | Permission-aware schema introspection | Must | The schema endpoint returns only the types and fields the caller may see, so generated admin and agent tools match permissions. |
| 18 | SAML 2.0 | Should | For enterprises without OIDC. |
| 19 | Passkeys (WebAuthn) | Should | Passwordless admin login and second factor. |
| 20 | Full audit log | Should | Configurable retention, login and token events, before/after diffs, filter UI, CSV export, forwarding to SIEM. |
| 21 | Service accounts | Should | Non-human identities with client credentials, used by CI, sync jobs and MCP clients, with the same RBAC as users. |
| 22 | Member profile fields and groups management | Should | Admin UI for members, custom profile fields defined like a content type, group assignment. |
| 23 | IP allow-lists | Should | Per role or per API key. |
| 24 | Session and device management | Should | Users see active sessions and devices and can revoke them. |
| 25 | SCIM provisioning | Could | User and group sync from identity providers. |
| 26 | Approval for permission changes | Could | Four-eyes rule for role edits. |
| 27 | External token issuer | Could | Accept tokens from a configured external IdP directly instead of federating. |
| 28 | Attribute-based policies | Could | Declarative policy rules beyond C# predicates. |
| 29 | Anonymous member sessions | Could | Guest identities that can be upgraded to accounts. |
| 30 | Paid or gated SSO, audit or 2FA | Won't | All free. |
| 31 | Shared identity pool for admins and members | Won't | Separate pools; different threat models. |
| 32 | Custom crypto or password hashing | Won't | ASP.NET Core Identity primitives only. |

## Decisions this category forces

- [ ] Identity foundation: ASP.NET Core Identity with OpenIddict on top, or a custom user store. Recommendation: ASP.NET Core Identity; it brings hashing, lockout, 2FA and external logins.
- [ ] Permission model shape: role-based with additive policies (Directus) or role-based with deny rules. Recommendation: additive only; simpler to reason about and to show in the admin.
- [ ] Row-level rule authoring: C# only, or also a JSON rule format editable in the admin. Recommendation: C# in the first release; JSON rules as Should if agencies ask.
- [ ] Member identity pool: separate database tables and token audience from admins (recommended), or one Identity store with a user type flag.
