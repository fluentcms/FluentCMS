# ADR-0009: Human sign-in with ASP.NET Core Identity; agents are not users

- Status: accepted
- Date: 2026-10-16
- Related: USR-01 to USR-03, SEC-01, SEC-07, D23, P8, P10

## Context

M1 needs email and password sign-in plus at least one external identity option, with every user an administrator (USR-01, D23). M2 adds roles and scoped approvals (USR-02, USR-03). Agents must have their own credentials, never a shared account, and the audit log must always say which agent acted (SEC-01, P8). The product must run with no required external service other than the model provider (P10).

## Decision

- People authenticate with ASP.NET Core Identity, stored in the application database, using cookie authentication for the admin UI. Email and password are built in. External sign-in is a generic OpenID Connect provider configured by the owner, which covers Google, Microsoft Entra, Keycloak, Authentik and similar without a dependency on any of them.
- Roles are modelled as claims from M1 even though everyone holds `Administrator`. M2 introduces the real roles without a change to the user store.
- Agents are not Identity users. An agent credential is its own aggregate in Core with display name, key hash or OAuth client, scopes, caps, expiry and revocation (ADR-0007). Authorization checks take a principal that is either a user or an agent, and the audit log records which.
- Standard web hardening applies to the admin and delivery endpoints: anti-forgery, rate limiting, input validation, a content security policy, and secrets encrypted at rest with ASP.NET Core Data Protection (SEC-07).

## Consequences

- No external identity provider is required. One can be plugged in through OpenID Connect.
- Human and agent identities cannot be confused in code or in the log.
- Identity's schema adds tables to the application database, accepted for a single-deployable product.

## Alternatives considered

- Require an external identity provider: simpler code, but violates P10 for the smallest teams.
- Model agents as Identity users with a flag: convenient, but shared tables and semantics invite exactly the confusion SEC-01 forbids.
- OpenIddict as the full identity server in M1: needed in M2 for agent OAuth (ADR-0007), premature for M1 human sign-in.
